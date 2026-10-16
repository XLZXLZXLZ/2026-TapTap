using UnityEngine;

namespace TapTap
{
    public enum PlayerExitStage { None, Settling, Retracting, Approaching, TurningIn, Entering, TurningFront, Holding, Complete }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerExitView : MonoBehaviour
    {
        private PlayerController player;
        private PlayerView view;
        private PlayerView.FacingState initialFacing;
        private Vector3 initialBody, initialHead, settledBody, frontBody, insideBody, joinedOffset;
        private float clock, settleTime, retractTime, approachTime, enterTime, turnTime, holdTime;
        private float approachYaw, inwardYaw, frontYaw;
        private bool captured;
        public PlayerExitStage Stage { get; private set; }
        public bool IncludesHead { get; private set; }
        public bool RecallsHead { get; private set; }
        public bool IsPlaying => captured && Stage != PlayerExitStage.Complete;
        public float Duration => settleTime + retractTime + approachTime + enterTime + turnTime * 2f + holdTime;

        public bool Begin(LevelEndpoint endpoint)
        {
            if (captured || endpoint == null) return false;
            player = GetComponent<PlayerController>();
            view = GetComponent<PlayerView>();
            if (view == null || player.Body == null || player.Head == null || player.Body.Visual == null
                || player.Head.Visual == null || player.Body.IsReturning) return false;
            // Only the extension family recalls the upper half. A detached head, including a
            // not-yet-completed pull, stays in the world while the lower half enters on its own.
            RecallsHead = player.Phase == PlayerPhase.Extending || player.Phase == PlayerPhase.Holding
                || player.Phase == PlayerPhase.Retracting;
            IncludesHead = RecallsHead || player.Phase == PlayerPhase.Joined || player.Phase == PlayerPhase.PickingUp;
            initialFacing = view.CaptureFacing();
            view.BeginExitPresentation(IncludesHead);
            initialBody = player.Body.Visual.position;
            initialHead = player.Head.Visual.position;
            float footOffset = initialBody.y - player.Body.Motor.CollisionBounds.min.y;
            frontBody = endpoint.FrontFootPosition + Vector3.up * footOffset;
            insideBody = endpoint.InsideFootPosition(player.Config.UnitSize) + Vector3.up * footOffset;
            settledBody = initialBody;
            float fall = Mathf.Max(0f, initialBody.y - frontBody.y);
            if (fall > player.Config.ToWorld(0.02f))
            {
                settledBody.y = frontBody.y;
                settleTime = Mathf.Clamp(Mathf.Sqrt(2f * fall / player.Config.WorldGravity), 0.08f, 0.4f);
            }
            else settleTime = 0f;
            joinedOffset = Vector3.up * player.Config.ToWorld(player.Config.JoinedOffset);
            retractTime = RecallsHead ? Mathf.Max(0.02f, endpoint.EntryRetractDuration) : 0f;
            float speed = Mathf.Max(0.1f, endpoint.EntryWalkSpeed) * player.Config.UnitSize;
            approachTime = Vector3.Distance(settledBody, frontBody) / speed;
            enterTime = Vector3.Distance(frontBody, insideBody) / speed;
            turnTime = Mathf.Max(0.02f, endpoint.EntryTurnDuration);
            holdTime = Mathf.Max(0f, endpoint.EntryHoldDuration);
            frontYaw = 180f;
            approachYaw = approachTime < 0.001f ? initialFacing.BodyYaw : frontBody.x >= settledBody.x ? 120f : 240f;
            Vector3 inward = insideBody - frontBody;
            inwardYaw = inward.sqrMagnitude < 0.0001f ? 0f : Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg;
            clock = 0f; captured = true;
            Simulate(0f);
            return true;
        }

        private void Update() => Simulate(Time.unscaledDeltaTime);

        public void Simulate(float dt)
        {
            if (!IsPlaying || dt < 0f) return;
            clock += dt;
            float remaining = clock;
            Vector3 lower = initialBody, upper = initialHead;
            float yaw = initialFacing.BodyYaw;
            bool walking = false;
            if (Take(ref remaining, settleTime, out float t))
            {
                Stage = PlayerExitStage.Settling;
                lower = Vector3.Lerp(initialBody, settledBody, t * t);
                upper = initialHead + lower - initialBody;
            }
            else if (Take(ref remaining, retractTime, out t))
            {
                Stage = PlayerExitStage.Retracting;
                lower = settledBody;
                upper = Vector3.Lerp(initialHead + settledBody - initialBody, lower + joinedOffset, Smooth(t));
            }
            else if (Take(ref remaining, approachTime, out t))
            {
                Stage = PlayerExitStage.Approaching;
                lower = Vector3.Lerp(settledBody, frontBody, t);
                upper = lower + joinedOffset;
                yaw = Mathf.LerpAngle(initialFacing.BodyYaw, approachYaw, Smooth(Mathf.Clamp01(t * approachTime / 0.12f)));
                walking = true;
            }
            else if (Take(ref remaining, turnTime, out t))
            {
                Stage = PlayerExitStage.TurningIn;
                lower = frontBody; upper = lower + joinedOffset;
                yaw = Mathf.LerpAngle(approachYaw, inwardYaw, Smooth(t));
            }
            else if (Take(ref remaining, enterTime, out t))
            {
                Stage = PlayerExitStage.Entering;
                lower = Vector3.Lerp(frontBody, insideBody, t); upper = lower + joinedOffset;
                yaw = inwardYaw; walking = true;
            }
            else
            {
                lower = insideBody; upper = lower + joinedOffset;
                if (Take(ref remaining, turnTime, out t))
                {
                    Stage = PlayerExitStage.TurningFront;
                    yaw = Mathf.LerpAngle(inwardYaw, frontYaw, Smooth(t));
                }
                else
                {
                    yaw = frontYaw;
                    Stage = remaining < holdTime ? PlayerExitStage.Holding : PlayerExitStage.Complete;
                }
            }
            view.RenderExitPose(lower, IncludesHead ? upper : initialHead, yaw, walking, IncludesHead);
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);
        private static bool Take(ref float remaining, float duration, out float t)
        {
            if (duration > 0.0001f && remaining < duration) { t = remaining / duration; return true; }
            remaining -= duration; t = 1f; return false;
        }

        public void Cancel()
        {
            if (!captured) return;
            captured = false;
            if (view != null) { view.EndExitPresentation(); view.RestoreFacing(in initialFacing); }
            Stage = PlayerExitStage.None;
        }

        private void OnDisable() => Cancel();
    }
}
