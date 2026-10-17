using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapTap.MagneticLinkRemake.Editor
{
    [InitializeOnLoad]
    public static class MagneticGuideValidation
    {
        private const string Pending = "TapTap.MagneticGuide.Validation";
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/MagneticGuide"));
        private static double deadline, clock;
        private static PlayerController player;
        private static MagneticLinkRemakeView adapter;
        private static FoldedMagneticLink effect;
        private static LineRenderer core;
        private static float near, far, unit;

        static MagneticGuideValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
                deadline = EditorApplication.timeSinceStartup + 120d;
                EditorApplication.update += Tick;
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "results.txt"), "");
            PlayerSettings.productName = "TapTap Magnetic Guide Validation";
            EditorSceneManager.OpenScene("Assets/Scenes/WorldMaps/World_Character_Play.unity");
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                Check(EditorApplication.timeSinceStartup < deadline, "Timed out waiting for player");
                if (WorldMapProgression.Current == null || WorldMapProgression.Current.CurrentModule == null) return;
                player = Object.FindObjectOfType<PlayerController>();
                adapter = player.GetComponent<MagneticLinkRemakeView>();
                if (adapter == null) return;
                Time.timeScale = 0f;
                effect = player.GetComponentInChildren<FoldedMagneticLink>();
                Check(effect != null && effect.Ready, "Runtime magnetic renderer failed");
                core = effect.GetComponentsInChildren<LineRenderer>().Single(line => line.name == "Bright core");
                foreach (Transform child in effect.GetComponentsInChildren<Transform>()) child.gameObject.layer = 30;
                unit = player.Config.UnitSize;
                float joined = player.Config.ToWorld(player.Config.JoinedOffset) + player.Config.JoinTolerance;
                near = Mathf.Lerp(joined, player.Config.ToWorld(player.Config.DistantThreshold), 0.5f);
                far = player.Config.ToWorld(player.Config.DistantThreshold) + unit * 2f;
                adapter.RefreshPresentation(clock, false);

                SetState(PlayerPhase.Joined, player.Config.ToWorld(player.Config.JoinedOffset));
                Advance(0.4f);
                Check(!core.enabled, "Joined player displays a connection");
                SetState(PlayerPhase.Detached, near, player.Config.ToWorld(player.Config.AlignmentTolerance) + unit);
                Advance(0.4f);
                Check(!player.CanRecall && !core.enabled, "Guide appears outside the gameplay alignment zone");

                SetState(PlayerPhase.Detached, near);
                Check(player.CanRecall, "Near fixture is not recallable");
                Advance(0.3f);
                float previewWidth = core.widthMultiplier;
                Color preview = Tint(core);
                Check(core.enabled && preview.a > 0.2f && preview.a <= 0.26f && preview.g > preview.r * 0.5f,
                    "Near preview is not translucent yellow");
                Capture("01-near-preview.png");
                Vector3[] frozen = Points(core);
                adapter.RefreshPresentation(clock, false);
                Check(Same(frozen, Points(core)) && Tint(core) == preview, "Paused clock advanced the guide");
                Activate();
                Check(player.Phase == PlayerPhase.Pulling && !player.IsDistantPull, "Space did not trigger normal gameplay pull");
                Advance(0.02f);
                Check(core.widthMultiplier > previewWidth && core.widthMultiplier < previewWidth / 0.35f,
                    "Preview-to-active width transition jumped");
                Advance(0.3f);
                Check(core.widthMultiplier > previewWidth * 2.8f && Tint(core).a > 0.8f,
                    "Active connection did not reach full width/opacity");
                Capture("02-near-active.png");
                Pass("Actual CanRecall geometry, no guide outside zone, translucent 35%-width preview, Space starts full-strength pull, pause freezes motion");

                SetState(PlayerPhase.Detached, far);
                Advance(0.02f);
                Color blend = Tint(core);
                Check(blend.g > blend.r * 0.05f, "Distance color snapped immediately");
                Advance(0.3f);
                Check(Tint(core).a <= 0.26f && Mathf.Abs(core.widthMultiplier / previewWidth - 1.15f) < 0.01f,
                    "Distant guide did not retain preview opacity with the 15% width increase");
                Check(core.numCornerVertices == 0, "Distant wave still has rounded corners");
                CheckRed();
                Capture("03-distant-preview.png");
                Activate();
                Check(player.IsDistantPull, "Space did not use the gameplay distant-pull threshold");
                Advance(0.3f); CheckRed();
                Check(Tint(core).a > 0.8f && core.widthMultiplier > previewWidth * 2.8f, "Distant pull is not a full-strength line");
                Capture("04-distant-active.png");
                player.Head.Motor.Teleport(player.Body.Motor.Position + Vector2.up * near);
                Advance(0.3f); CheckRed();
                Check(player.IsDistantPull, "Distant classification was lost while approaching");
                Pass("Distant preview and active pull turn all layers red smoothly; IsDistantPull retains red after the distance closes");

                SetState(PlayerPhase.Detached, far);
                adapter.RefreshPresentation(clock, true);
                CheckRed();
                Check(Tint(core).a <= 0.26f && Mathf.Abs(core.widthMultiplier / previewWidth - 1.15f) < 0.01f,
                    "Rewind did not reconstruct the wider distant preview style");
                frozen = Points(core);
                adapter.RefreshPresentation(clock - 0.05d, true);
                Check(!Same(frozen, Points(core)), "Rewind clock did not reverse wave motion");
                adapter.RefreshPresentation(clock, true);
                Check(Same(frozen, Points(core)), "Wave cannot reproduce the restored simulation time");
                SetState(PlayerPhase.Detached, near);
                adapter.RefreshPresentation(clock - 0.1d, true);
                Check(Tint(core).g > Tint(core).r * 0.5f, "Rewind retained future red state");
                player.GetComponent<RespawnService>().BeginSingleReturn(player.Head);
                clock -= 0.1d;
                Advance(0.02f);
                Check(core.enabled, "Death cut off the line before its fade");
                Advance(0.4f);
                Check(effect.GetComponentsInChildren<LineRenderer>().All(line => !line.enabled), "Death left a magnetic line visible");
                Pass("Rewind reconstructs preview/near/far styles and reproduces wave positions; death fades out all layers");
                ValidateWaves();
                ValidateExitCleanup();
                Finish(null);
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static void SetState(PlayerPhase phase, float height, float horizontal = 0f)
        {
            player.GetComponent<RespawnService>().CancelAll();
            var state = player.CaptureState();
            state.Phase = phase; state.DistantPull = false; state.Clock = 1f; state.RecallReadyAt = 0f;
            state.SpaceHeld = false; state.RecallBuffered = state.BufferedRelease = false;
            player.RestoreState(in state);
            player.Body.Motor.Teleport(new Vector2(2000f, 2000f));
            player.Head.Motor.Teleport(player.Body.Motor.Position + new Vector2(horizontal, height));
            player.Body.Velocity = player.Head.Velocity = Vector2.zero;
            Physics2D.SyncTransforms();
        }

        private static void Activate()
        {
            var input = player.GetComponent<PlayerInput>();
            input.SetExternalInput(0f, false); input.SetExternalInput(0f, true);
            player.SimulateStep(0.02f);
        }

        private static void Advance(float seconds)
        {
            for (int i = 0; i < Mathf.CeilToInt(seconds / 0.02f); i++)
            { clock += 0.02d; SyncRenderPose(); adapter.RefreshPresentation(clock, false); }
        }

        private static void SyncRenderPose()
        {
            // The fixture runs synchronously at timeScale=0, before Rigidbody interpolation.
            foreach (MovableEntity entity in new[] { player.Body, player.Head })
            {
                Vector2 position = entity.Motor.Position;
                entity.transform.position = new Vector3(position.x, position.y, entity.transform.position.z);
            }
        }

        private static Color Tint(LineRenderer line)
        {
            var block = new MaterialPropertyBlock(); line.GetPropertyBlock(block); return block.GetColor("_Tint");
        }

        private static void CheckRed()
        {
            foreach (LineRenderer line in effect.GetComponentsInChildren<LineRenderer>())
            { Color color = Tint(line); Check(color.r > color.g * 8f && color.r > color.b * 8f, "Non-red distant layer: " + line.name); }
        }

        private static Vector3[] Points(LineRenderer line)
        { var points = new Vector3[line.positionCount]; line.GetPositions(points); return points; }

        private static bool Same(Vector3[] a, Vector3[] b) => a.Length == b.Length
            && !a.Where((point, i) => Vector3.Distance(point, b[i]) > 0.00001f).Any();

        private static void ValidateWaves()
        {
            double nearEnergy = 0, farEnergy = 0, nearChange = 0, farChange = 0;
            Vector3[] lastNear = null, lastFar = null;
            for (int frame = 0; frame < 100; frame++)
            {
                effect.Render(Vector3.zero, Vector3.up * 6f, 1f, 0f, frame * 0.017d, 1f, 1f, 0f);
                Vector3[] a = Points(core);
                effect.Render(Vector3.zero, Vector3.up * 6f, 1f, 0f, frame * 0.017d, 1f, 1f, 1f);
                Vector3[] b = Points(core);
                for (int i = 1; i < a.Length - 1; i++)
                {
                    nearEnergy += a[i].x * a[i].x; farEnergy += b[i].x * b[i].x;
                    if (lastNear != null) { nearChange += Math.Abs(a[i].x - lastNear[i].x); farChange += Math.Abs(b[i].x - lastFar[i].x); }
                }
                lastNear = a; lastFar = b;
            }
            Check(farEnergy > nearEnergy * 1.4 && farChange > nearChange * 1.4, "Distant wave amplitude/frequency did not increase");
            Pass("Distant wave energy and temporal variation increase: " + (farEnergy / nearEnergy).ToString("F2") + "x / " + (farChange / nearChange).ToString("F2") + "x");
        }

        private static void ValidateExitCleanup()
        {
            PlayerView view = player.GetComponent<PlayerView>();
            foreach (PlayerPhase phase in new[] { PlayerPhase.Extending, PlayerPhase.Holding,
                PlayerPhase.Retracting, PlayerPhase.Detached, PlayerPhase.Pulling })
            {
                SetState(phase, near); Advance(0.4f);
                Check(core.enabled, "Exit fixture has no line: " + phase);
                view.BeginExitPresentation(true);
                // Reproduce completion with a frozen simulation clock and cached endpoints.
                for (int i = 0; i < 20; i++) adapter.RefreshPresentation(clock, false);
                Check(effect.GetComponentsInChildren<LineRenderer>().All(line => !line.enabled),
                    "Frozen-clock exit left a magnetic layer visible: " + phase);
                view.EndExitPresentation();
                SetState(PlayerPhase.Joined, player.Config.ToWorld(player.Config.JoinedOffset));
                SyncRenderPose(); adapter.RefreshPresentation(clock, false);
                Check(!core.enabled, "Exit cleanup retained a cached line after reset");
            }
            SetState(PlayerPhase.Extending, near); Advance(0.4f);
            WorldMapProgression flow = WorldMapProgression.Current;
            LevelEndpoint endpoint = flow.CurrentRegion.GetComponentInChildren<LevelEndpoint>();
            flow.SetExitsEnabled(true);
            Check(flow.CompleteCurrent(endpoint), "Could not trigger actual level completion");
            Check(flow.IsTransitioning && !view.ExitPresentation, "Did not reach pre-cinematic transition frame");
            adapter.RefreshPresentation(clock, false);
            Check(effect.GetComponentsInChildren<LineRenderer>().All(line => !line.enabled),
                "Completion during head extension left a line before the exit cinematic");
            Pass("Frozen-clock exit clears all layers in five player phases; actual completion during extension clears immediately before the exit cinematic");
        }

        private static void Capture(string name)
        {
            var node = new GameObject("Magnetic preview camera");
            var camera = node.AddComponent<Camera>(); camera.enabled = false;
            camera.orthographic = true; camera.orthographicSize = far * 0.6f + unit;
            camera.transform.position = new Vector3(2000f, 2000f + far * 0.45f, -10f);
            camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.008f, 0.012f, 0.02f);
            var target = new RenderTexture(600, 900, 24); target.Create(); camera.targetTexture = target;
            camera.Render(); RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 600, 900), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output, name), image.EncodeToPNG());
            Check(image.GetPixels32().Count(pixel => pixel.r > 50 || pixel.g > 50 || pixel.b > 50) > 10,
                "Captured magnetic line is blank: " + name);
            RenderTexture.active = previous; camera.targetTexture = null;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image); Object.DestroyImmediate(node);
        }

        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Pass(string message) { File.AppendAllText(Path.Combine(Output, "results.txt"), "PASS: " + message + "\n"); Debug.Log(message); }
        private static void Finish(Exception exception)
        {
            SessionState.SetBool(Pending, false); EditorApplication.update -= Tick;
            Time.timeScale = 1f;
            if (exception != null) { File.AppendAllText(Path.Combine(Output, "results.txt"), "FAIL: " + exception + "\n"); Debug.LogException(exception); }
            EditorApplication.Exit(exception == null ? 0 : 1);
        }
    }
}
