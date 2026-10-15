using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public static partial class RewindRegressionTests
    {
        private static LevelEndpoint ExitEndpoint(Fixture f)
        {
            LevelEndpoint endpoint = f.NewObject("Test Door", Origin + Vector2.right * 2f).AddComponent<LevelEndpoint>();
            var front = new GameObject("Front").transform;
            var inside = new GameObject("Inside").transform;
            front.SetParent(endpoint.transform, false); inside.SetParent(endpoint.transform, false);
            inside.localPosition = Vector3.forward;
            endpoint.ConfigureEntryAnchors(front, inside);
            return endpoint;
        }

        private static void PositionNear(Vector3 actual, Vector3 expected, string message) =>
            Check(Vector3.Distance(actual, expected) < 0.002f, message + " actual=" + actual + " expected=" + expected);

        private static void ExitHeadRules()
        {
            foreach (PlayerPhase phase in System.Enum.GetValues(typeof(PlayerPhase)))
                using (var f = new Fixture())
                {
                    f.CreatePlayer(true); PlayerView view = FacingView(f);
                    var state = f.Player.CaptureState(); state.Phase = phase; f.Player.RestoreState(in state);
                    f.Head.Motor.Teleport(f.Spawn + new Vector2(5f, 3f));
                    f.Head.transform.position = f.Head.Motor.Position;
                    Vector3 head = f.Head.Visual.position, body = f.Body.Visual.position;
                    LevelEndpoint endpoint = ExitEndpoint(f);
                    var exit = f.Player.gameObject.AddComponent<PlayerExitView>();
                    Check(exit.Begin(endpoint), "Cannot begin " + phase);
                    bool recall = phase == PlayerPhase.Extending || phase == PlayerPhase.Holding || phase == PlayerPhase.Retracting;
                    bool included = recall || phase == PlayerPhase.Joined || phase == PlayerPhase.PickingUp;
                    Check(exit.RecallsHead == recall && exit.IncludesHead == included, "Wrong head rule for " + phase);
                    if (recall)
                    {
                        Check(exit.Stage == PlayerExitStage.Retracting, "Extension must retract first");
                        exit.Simulate(endpoint.EntryRetractDuration * 0.5f);
                        Check(Vector3.Distance(f.Head.Visual.position, body + Vector3.up * f.Config.JoinedOffset)
                            < Vector3.Distance(head, body + Vector3.up * f.Config.JoinedOffset), "Head did not retract smoothly");
                    }
                    exit.Simulate(exit.Duration + 1f);
                    Check(!exit.IsPlaying && exit.Stage == PlayerExitStage.Complete, "Entry never completes");
                    PositionNear(f.Head.Motor.Position, head, "Entry moved logical head");
                    PositionNear(f.Body.Motor.Position, body, "Entry moved logical body");
                    if (!included)
                    {
                        PositionNear(f.Head.Visual.position, head, "Detached head followed the body");
                        YawNear(view.CaptureFacing().HeadYaw, 180f, "Detached head turned");
                    }
                    else PositionNear(f.Head.Visual.position, f.Body.Visual.position + Vector3.up * f.Config.JoinedOffset, "Joined offset changed");
                    Check(f.Player.Phase == phase, "Presentation changed logical phase");
                    exit.Cancel();
                    PositionNear(f.Body.Visual.position, body, "Cancel left visual body displaced");
                    PositionNear(f.Head.Visual.position, head, "Cancel left visual head displaced");
                    Check(!view.ExitPresentation, "Override leaked on cancel");
                }
        }

        private static void ExitTimeline()
        {
            using (var f = new Fixture())
            {
                f.CreatePlayer(true); PlayerView view = FacingView(f);
                LevelEndpoint endpoint = ExitEndpoint(f);
                endpoint.transform.localScale = Vector3.one * 2f;
                PositionNear(endpoint.InsideFootPosition(2f) - endpoint.FrontFootPosition, Vector3.forward * 2f, "Depth did not scale");
                f.Player.TeleportJoined(f.Spawn + Vector2.up * 2f);
                f.Body.transform.position = f.Body.Motor.Position;
                f.Head.transform.position = f.Head.Motor.Position;
                Physics2D.SyncTransforms();
                Vector3 start = f.Body.Visual.position;
                float footOffset = start.y - f.Body.Motor.CollisionBounds.min.y;
                var exit = f.Player.gameObject.AddComponent<PlayerExitView>();
                Check(exit.Begin(endpoint) && exit.Stage == PlayerExitStage.Settling, "Airborne entry did not settle");
                exit.Simulate(0.1f);
                Check(f.Body.Visual.position.y < start.y, "Settling did not lower the player");
                bool approached = false, entered = false, turned = false;
                for (int i = 0; i < 1000 && exit.IsPlaying; i++)
                {
                    exit.Simulate(0.01f);
                    if (exit.Stage == PlayerExitStage.Approaching) approached = true;
                    if (exit.Stage == PlayerExitStage.Entering)
                    {
                        entered = true; YawNear(view.CaptureFacing().BodyYaw, 0f, "Player is not facing into the door");
                        PositionNear(new Vector3(f.Body.Visual.position.x, f.Body.Visual.position.y, 0f),
                            endpoint.FrontFootPosition + Vector3.up * footOffset, "Depth walk left its baseline");
                    }
                    if (exit.Stage == PlayerExitStage.TurningFront) turned = true;
                    Vector3 pose = f.Body.Visual.position;
                    view.Simulate(0.1f); view.RenderRewindState();
                    PositionNear(f.Body.Visual.position, pose, "Ordinary presentation overwrote the entry pose");
                }
                Check(approached && entered && turned && exit.Stage == PlayerExitStage.Complete, "Entry skipped stages");
                PositionNear(f.Body.Visual.position, endpoint.InsideFootPosition(2f) + Vector3.up * footOffset, "Final anchor differs");
                YawNear(view.CaptureFacing().BodyYaw, 180f, "Final facing does not face camera");
                Vector3 finish = f.Body.Visual.position;
                exit.Cancel(); Check(exit.Begin(endpoint), "Entry cannot restart");
                exit.Simulate(exit.Duration + 0.1f);
                PositionNear(f.Body.Visual.position, finish, "Entry depends on frame size");
                exit.enabled = false;
                Check(!view.ExitPresentation, "Disable leaked presentation override");
                PositionNear(f.Body.Visual.position, start, "Disable did not restore visuals");
            }
        }

        private static void ExitAnimation()
        {
            using (var f = new Fixture())
            {
                f.CreatePlayer(true); PlayerView view = FacingView(f);
                Animator AddAnimator(MovableEntity entity, string part)
                {
                    GameObject model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Art/3D Resources/Player/" + part + "_rigged.fbx"), entity.Visual.Find("Facing"));
                    Animator animator = model.GetComponent<Animator>();
                    animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animator/Player/" + part + ".controller");
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Update(0f);
                    return animator;
                }
                Animator lower = AddAnimator(f.Body, "down"), upper = AddAnimator(f.Head, "up");
                view.ConfigureModels(f.Body.Visual.Find("Facing"), f.Head.Visual.Find("Facing"), lower, upper);
                LevelEndpoint endpoint = ExitEndpoint(f);
                var exit = f.Player.gameObject.AddComponent<PlayerExitView>();
                Check(exit.Begin(endpoint), "Cannot start animated entry");
                for (int i = 0; i < 15; i++)
                {
                    exit.Simulate(0.02f); view.RenderMovementAnimations(true);
                    lower.Update(0.02f); upper.Update(0.02f);
                }
                Check(lower.speed == 1f && upper.speed == 1f && lower.GetBool("Moving") && upper.GetBool("Moving"), "Blocked gameplay paused entry walking");
                Check(lower.updateMode == AnimatorUpdateMode.UnscaledTime && upper.updateMode == AnimatorUpdateMode.UnscaledTime, "Entry uses scaled animation");
                Check(Mathf.Abs(lower.GetCurrentAnimatorStateInfo(0).normalizedTime - upper.GetCurrentAnimatorStateInfo(0).normalizedTime) < 0.01f,
                    "Entry machines started out of phase");
                exit.Cancel();
                f.Detach(f.Spawn, f.Spawn + Vector2.right * 5f);
                Check(exit.Begin(endpoint), "Cannot start body-only entry");
                Check(upper.speed == 0f, "Detached head animation was not frozen");
                exit.Simulate(exit.Duration + 0.1f);
                Check(!lower.GetBool("Moving"), "Player still walks after reaching final anchor");
                exit.Cancel();
                Check(lower.speed == 1f && upper.speed == 1f && lower.updateMode == AnimatorUpdateMode.Normal
                    && upper.updateMode == AnimatorUpdateMode.Normal, "Animation modes leaked after entry");
            }
        }
    }
}
