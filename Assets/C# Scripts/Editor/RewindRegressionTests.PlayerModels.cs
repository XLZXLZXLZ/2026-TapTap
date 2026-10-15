using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static partial class RewindRegressionTests
    {
        public static void RunPlayerModelPreviewBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project.");
            try
            {
                PlayerModelAssets.ReplacePlayerModels();
                ValidatePlayerModels();
                RenderPlayerModels();
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        // Isolated-project entry point: creates real Unity prefab assets, validates them, then runs play-mode regressions.
        public static void RunPlayerModelsBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project.");
            try
            {
                PlayerModelAssets.ReplacePlayerModels();
                ValidatePlayerModels();
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) RenderPlayerModels();
                RunBatch();
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(Application.dataPath, "../player-model-results.txt"), exception.ToString());
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static PlayerView FacingView(Fixture f)
        {
            PlayerView view = f.Player.GetComponent<PlayerView>();
            var lower = new GameObject("Facing").transform;
            var upper = new GameObject("Facing").transform;
            lower.SetParent(f.Body.Visual, false);
            upper.SetParent(f.Head.Visual, false);
            view.ConfigureModels(lower, upper, null, null);
            return view;
        }

        private static void YawNear(float actual, float expected, string message) =>
            Check(Mathf.Abs(Mathf.DeltaAngle(actual, expected)) < 0.01f, message + " actual=" + actual + " expected=" + expected);

        private static void ModelFacingRewind()
        {
            using (var f = new Fixture())
            {
                f.Floor(); f.CreatePlayer(true);
                PlayerView view = FacingView(f);
                f.Input.SetExternalInput(1f, false); f.Step(4);
                PlayerView.FacingState midway = view.CaptureFacing();
                Check(midway.BodyYaw > 120f && midway.BodyYaw < 175f, "Facing snapped instead of smoothing");
                YawNear(midway.HeadYaw, midway.BodyYaw, "Connected halves did not turn together");
                f.Step(22);
                PlayerView.FacingState complete = view.CaptureFacing();
                YawNear(complete.BodyYaw, 120f, "Right target yaw");
                f.Back(22);
                YawNear(view.CaptureFacing().BodyYaw, midway.BodyYaw, "Intermediate body yaw did not rewind");
                YawNear(f.Head.Visual.Find("Facing").localEulerAngles.y, midway.HeadYaw, "Rendered head did not restore immediately");
                f.Manager.EndRewind(); f.Step(22);
                YawNear(view.CaptureFacing().BodyYaw, complete.BodyYaw, "Replaying restored turn diverged");
                f.Input.SetExternalInput(0f, false); f.Step(24);
                YawNear(view.CaptureFacing().BodyYaw, 180f, "Idle did not face the camera");
                f.Input.SetExternalInput(-1f, false); f.Step(24);
                YawNear(view.CaptureFacing().BodyYaw, 240f, "Left target yaw");
                Check(f.Body.transform.rotation == Quaternion.identity && f.Head.transform.rotation == Quaternion.identity,
                    "Visual facing rotated a physics entity");
                f.Player.TeleportJoined(f.Spawn);
                YawNear(view.CaptureFacing().BodyYaw, 180f, "Teleport did not reset facing");
            }
        }

        private static void ModelFacingDetachedAndLocked()
        {
            using (var f = new Fixture())
            {
                f.Floor(); f.CreatePlayer(true);
                PlayerView view = FacingView(f);
                f.Input.SetExternalInput(1f, false); f.Step(20);
                f.Detach(f.Spawn, f.Spawn + new Vector2(6f, 3f));
                float headYaw = view.CaptureFacing().HeadYaw;
                f.Input.SetExternalInput(-1f, false); f.Step(26);
                PlayerView.FacingState detached = view.CaptureFacing();
                YawNear(detached.BodyYaw, 240f, "Detached lower half cannot turn");
                YawNear(detached.HeadYaw, headYaw, "Detached head followed lower movement");
                f.Input.SetExternalInput(1f, false); f.Step(8); f.Back(8);
                YawNear(view.CaptureFacing().BodyYaw, detached.BodyYaw, "Detached body yaw rewind");
                YawNear(view.CaptureFacing().HeadYaw, detached.HeadYaw, "Detached head yaw rewind");
                f.Manager.EndRewind();
                f.Player.TeleportJoined(f.Spawn);
                f.Input.SetExternalInput(1f, true); f.Step(3);
                Check(f.Player.Phase == PlayerPhase.Extending, "Expected extension lock");
                YawNear(view.CaptureFacing().BodyYaw, 180f, "Locked steering turned the model");
                f.Input.SetExternalInput(0f, false); f.Step(60);
                YawNear(view.CaptureFacing().HeadYaw, view.CaptureFacing().BodyYaw, "Joined head failed to align");
            }
        }

        private static void ModelSquashFeedback()
        {
            using (var f = new Fixture())
            {
                f.Floor(); f.CreatePlayer(true);
                PlayerView view = FacingView(f);
                PlayerVisualConfig settings = ScriptableObject.CreateInstance<PlayerVisualConfig>();
                try
                {
                    view.ConfigureVisuals(settings);
                    Vector2 bodySize = f.Body.Motor.Collider.size, headSize = f.Head.Motor.Collider.size;
                    f.Body.Velocity = Vector2.up * 10f;
                    view.Simulate(0.02f);
                    Check(f.Body.Visual.localScale.y > 1f && f.Body.Visual.localScale.x < 1f, "Vertical stretch was removed");
                    f.Body.Velocity = Vector2.zero;
                    view.ClearTransientEffects();
                    var contact = new PlayerContactFeedback { Entity = f.Body, Normal = Vector2.up, Speed = 8f };
                    typeof(PlayerView).GetMethod("OnContact", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .Invoke(view, new object[] { contact });
                    view.Simulate(0.02f);
                    Check(f.Body.Visual.localScale.y < 1f && f.Body.Visual.localScale.x > 1f, "Landing squash was removed");
                    view.ClearTransientEffects();
                    f.Head.NotifySpringBounce(f.Body);
                    // Move the spring timeline explicitly; the synchronous fixture does not run Unity frames.
                    var spring = (DG.Tweening.Tween)typeof(PlayerView).GetField("headTween", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .GetValue(view);
                    DG.Tweening.TweenExtensions.Goto(spring, settings.InteractionDuration * 0.15f, false);
                    view.Simulate(0.02f);
                    Check(f.Head.Visual.localScale.y < 1f && f.Head.Visual.localScale.x > 1f, "Head spring squash was removed");
                    Near(f.Body.Motor.Collider.size, bodySize, "Squash changed body collider");
                    Near(f.Head.Motor.Collider.size, headSize, "Squash changed head collider");
                }
                finally { view.ClearTransientEffects(); UnityEngine.Object.DestroyImmediate(settings); }
            }
        }

        private static void ValidatePlayerModels()
        {
            var lines = new List<string>();
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerModelAssets.PlayerPath);
            try
            {
                PlayerController player = root.GetComponent<PlayerController>();
                float lowerTop = 0f, upperBottom = 0f;
                foreach (MovableEntity entity in new[] { player.Body, player.Head })
                {
                    Check(entity.Visual.GetComponent<SpriteRenderer>() == null, "Old character sprite remains");
                    Animator[] animators = entity.Visual.GetComponentsInChildren<Animator>(true);
                    Check(animators.Length == 1 && animators[0].runtimeAnimatorController != null && !animators[0].applyRootMotion,
                        "Each half needs one in-place Animator with its own controller");
                    Bounds front = PlayerModelAssets.LocalBounds(entity.transform);
                    Vector2 collider = entity.GetComponent<BoxCollider2D>().size;
                    Check(front.size.x <= collider.x + 0.01f && front.size.y <= collider.y + 0.01f, "Model exceeds its collision footprint");
                    Check(front.size.x >= collider.x * 0.6f && front.size.y >= collider.y * 0.7f, "Model is too small relative to collider");
                    Check(front.center.magnitude < 0.001f, "Model is not centered in its collider");
                    Transform turn = entity.Visual.Find("Facing");
                    foreach (float yaw in new[] { 120f, 180f, 240f })
                    {
                        turn.localRotation = Quaternion.Euler(0f, yaw, 0f);
                        Bounds turned = PlayerModelAssets.LocalBounds(entity.transform);
                        Check(turned.size.x <= collider.x * 1.1f + 0.001f, "Turning greatly enlarges the collision silhouette");
                    }
                    turn.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    if (entity == player.Body) lowerTop = entity.transform.localPosition.y + front.max.y;
                    else upperBottom = entity.transform.localPosition.y + front.min.y;
                    lines.Add("PASS: " + entity.name + " model=" + front.size + " collider=" + collider + " animator=" + animators[0].runtimeAnimatorController.name);
                    foreach (Renderer renderer in entity.Visual.GetComponentsInChildren<Renderer>())
                        foreach (Material material in renderer.sharedMaterials)
                            lines.Add("Material: " + material.name + " shader=" + material.shader.name + " color="
                                + (material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color)
                                + " supported=" + material.shader.isSupported);
                }
                Check(upperBottom - lowerTop >= 0.049f, "Joined models need a small visible gap");
                Check(upperBottom - lowerTop <= 0.2f, "Joined visual gap is too large");
                lines.Add("PASS: Joined visual gap=" + (upperBottom - lowerTop));
                File.WriteAllLines(Path.Combine(Application.dataPath, "../player-model-results.txt"), lines);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void RenderPlayerModels()
        {
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Player preview camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1.25f;
            camera.transform.position = new Vector3(0f, 0.85f, -8f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.15f, 0.19f);
            var lightObject = new GameObject("Preview key light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
            foreach (float yaw in new[] { 240f, 180f, 120f })
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelAssets.PlayerPath), scene);
                root.transform.position = Vector3.right * ((180f - yaw) / 60f * 1.4f);
                PlayerController player = root.GetComponent<PlayerController>();
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    if (renderer is SpriteRenderer || renderer is LineRenderer || renderer is ParticleSystemRenderer) renderer.enabled = false;
                foreach (MovableEntity entity in new[] { player.Body, player.Head })
                {
                    entity.Visual.Find("Facing").localRotation = Quaternion.Euler(0f, yaw, 0f);
                    var outline = new GameObject("Collision outline").AddComponent<LineRenderer>();
                    outline.transform.SetParent(entity.transform, false);
                    outline.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Prototype/Lines.mat");
                    outline.startColor = outline.endColor = new Color(0.2f, 0.9f, 0.9f, 0.65f);
                    outline.startWidth = outline.endWidth = 0.006f;
                    outline.useWorldSpace = false; outline.loop = true; outline.positionCount = 4;
                    Vector2 half = entity.GetComponent<BoxCollider2D>().size * 0.5f;
                    outline.SetPositions(new[] { new Vector3(-half.x, -half.y, -0.6f), new Vector3(half.x, -half.y, -0.6f),
                        new Vector3(half.x, half.y, -0.6f), new Vector3(-half.x, half.y, -0.6f) });
                }
            }
            var target = new RenderTexture(1440, 720, 24);
            var pixels = new Texture2D(1440, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(Application.dataPath, "../player-model-preview.png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
                ShaderUtil.allowAsyncCompilation = previousAsync;
            }
        }
    }
}
