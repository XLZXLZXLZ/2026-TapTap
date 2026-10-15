using System;
using System.Collections;
using System.IO;
using DG.Tweening;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Isolated project only: installs the art prefab, renders it, then exercises live switch input.
    public static class ButtonModelRegressionTests
    {
        private const string Pending = "TapTap.ButtonModels.Pending";
        private static string Results => Path.Combine(Application.dataPath, "../button-model-results.txt");

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated project.");
            File.WriteAllText(Results, "");
            try
            {
                var before = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonModelAssets.SwitchPath).GetComponent<BoxCollider2D>();
                Vector2 size = before.size, offset = before.offset;
                ButtonModelAssets.InstallModels();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonModelAssets.SwitchPath);
                var box = prefab.GetComponent<BoxCollider2D>();
                Check(box.size == size && box.offset == offset && box.isTrigger, "2D trigger changed.");
                Check(prefab.GetComponentsInChildren<Collider>(true).Length == 0, "3D colliders were introduced.");
                Bounds bounds = PlayerModelAssets.LocalBounds(prefab.transform.Find("Button3D"));
                Near(bounds.size, new Vector3(1f, ButtonModelAssets.Height, 1f), "Fitted dimensions");
                Near(bounds.min, new Vector3(-0.5f, 0f, 0f), "Front edge must stay at Z=0");
                Check(!prefab.transform.Find("Base").gameObject.activeSelf && !prefab.transform.Find("Button").gameObject.activeSelf,
                    "Old sprite placeholders remain visible.");
                foreach (Renderer renderer in prefab.transform.Find("Button3D").GetComponentsInChildren<Renderer>())
                    foreach (Material material in renderer.sharedMaterials)
                        Check(material != null && material.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal), "Button material is incompatible.");
                Pass("Imported base and cap use supplied URP materials; dimensions are 1 x 0.22 x 1, front/back Z=0/1, original 2D trigger is unchanged");
                int inherited = 0;
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Levels" }))
                    foreach (MechanismSwitch button in AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)).GetComponentsInChildren<MechanismSwitch>(true))
                    {
                        Check(button.transform.Find("Button3D/Visual/Cap") != null, "A saved level failed to inherit the button model.");
                        inherited++;
                    }
                Pass(inherited + " saved level buttons inherit the replacement visual without rebuilding their source layouts");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionState.SetBool(Pending, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception) { Finish(exception); }
        }

        [InitializeOnLoadMethod]
        private static void Subscribe()
        {
            EditorApplication.playModeStateChanged -= Changed;
            EditorApplication.playModeStateChanged += Changed;
        }

        private static void Changed(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            EditorApplication.delayCall += Runtime;
        }

        private static void Runtime()
        {
            try
            {
                if (RewindManager.Current != null) Object.DestroyImmediate(RewindManager.Current.gameObject);
                var state = WorldPhaseState.Instance;
                state.RestoreState(false);
                var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ButtonModelAssets.SwitchPath));
                root.transform.position = new Vector3(100f, 100f, 0f);
                var button = root.GetComponent<MechanismSwitch>();
                Transform cap = root.transform.Find("Button3D/Visual/Cap");
                Vector3 rest = cap.localPosition, scale = cap.localScale;
                var actorObject = new GameObject("Button test actor");
                actorObject.transform.position = root.transform.position + Vector3.up * 0.15f;
                actorObject.AddComponent<BoxCollider2D>().size = Vector2.one * 0.2f;
                var actor = actorObject.AddComponent<MovableEntity>();
                actor.ControlledExternally = true;
                Physics2D.SyncTransforms();
                int changes = 0;
                Action<bool> changed = _ => changes++;
                state.Changed += changed;
                try
                {
                    var released = button.CaptureState();
                    button.SimulateStep();
                    Check(button.CaptureState().Occupied && state.Active && changes == 1, "Stepping on the button did not toggle once.");
                    DOTween.Goto(cap, 0.04f);
                    Check(cap.localPosition.y < rest.y && cap.localPosition.y > rest.y - ButtonModelAssets.Travel,
                        "Press skipped the intermediate movement.");
                    DOTween.Complete(cap);
                    Near(cap.localPosition, rest + Vector3.down * ButtonModelAssets.Travel, "Pressed cap position");
                    Near(cap.localScale, scale, "Press must move the model without squashing it");
                    var pressed = button.CaptureState();
                    button.SimulateStep();
                    Check(changes == 1, "Standing on the button toggled it repeatedly.");
                    actor.Motor.Teleport(new Vector2(110f, 100f));
                    Physics2D.SyncTransforms();
                    button.SimulateStep();
                    Check(!button.CaptureState().Occupied && changes == 1, "Release changed the switch phase.");
                    DOTween.Goto(cap, 0.08f);
                    Check(cap.localPosition.y > rest.y - ButtonModelAssets.Travel, "Release did not lift the cap.");
                    DOTween.Complete(cap);
                    Near(cap.localPosition, rest, "Released cap position");
                    Pass("Actual 2D occupancy presses only the cap, holds without retriggering, and releases with spring easing; base/scale/phase stay stable");
                    button.RestoreState(in pressed);
                    Near(cap.localPosition, rest + Vector3.down * ButtonModelAssets.Travel, "Pressed rewind pose");
                    button.RestoreState(in released);
                    Near(cap.localPosition, rest, "Released rewind pose");
                    Check(changes == 1, "Restoring a pose replayed phase events.");
                    root.transform.rotation = Quaternion.Euler(0f, 0f, 180f);
                    Vector3 before = cap.position;
                    button.RestoreState(in pressed);
                    Check(cap.position.y > before.y, "Ceiling button pressed away from its base.");
                    button.enabled = false;
                    Near(cap.localPosition, rest, "Disabling restores the cap");
                    button.enabled = true;
                    button.RestoreState(in pressed);
                    Near(cap.localPosition, rest + Vector3.down * ButtonModelAssets.Travel, "Re-enabled button drifted");
                    Pass("Rewind restores pressed/released poses immediately without events; ceiling rotation and disable/re-enable retain the correct travel");
                }
                finally
                {
                    state.Changed -= changed;
                    Object.DestroyImmediate(actorObject); Object.DestroyImmediate(root);
                }
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) RenderPreview();
                Finish();
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static void RenderPreview()
        {
            DebugManager.instance.Reset();
            Shader.DisableKeyword("DEBUG_DISPLAY");
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            Scene scene = SceneManager.GetActiveScene();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonModelAssets.VisualPath);
            var released = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            released.transform.position = Vector3.left * 0.75f;
            var pressed = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            pressed.transform.position = Vector3.right * 0.75f;
            pressed.transform.Find("Visual/Cap").localPosition += Vector3.down * ButtonModelAssets.Travel;
            var diagnostics = new System.Text.StringBuilder();
            foreach (Renderer renderer in released.GetComponentsInChildren<Renderer>(true))
            {
                diagnostics.AppendLine(renderer.name + " enabled=" + renderer.enabled + " active=" + renderer.gameObject.activeInHierarchy + " bounds=" + renderer.bounds);
                foreach (Material material in renderer.sharedMaterials)
                    diagnostics.AppendLine(material.name + " shader=" + material.shader.name + " color=" + material.GetColor("_BaseColor"));
            }
            File.WriteAllText(Path.Combine(Application.dataPath, "../button-render-diagnostics.txt"), diagnostics.ToString());
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0f, -0.06f, 0.5f);
            floor.transform.localScale = new Vector3(3.5f, 0.1f, 1.8f);
            floor.GetComponent<Renderer>().sharedMaterial = released.GetComponentInChildren<Renderer>().sharedMaterial;
            var camera = new GameObject("Button Preview Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(2.1f, 2.2f, -3.4f);
            camera.transform.LookAt(new Vector3(0f, 0f, 0.5f));
            camera.orthographic = true; camera.orthographicSize = 1.3f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.08f, 0.1f, 0.14f);
            var light = new GameObject("Button Key Light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.gray;
            var target = new RenderTexture(1200, 700, 24);
            var pixels = new Texture2D(1200, 700, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(Application.dataPath, "../button-model-preview.png"), pixels.EncodeToPNG());
                int goldPixels = 0;
                foreach (Color32 pixel in pixels.GetPixels32())
                    if (pixel.r > 100 && pixel.g > 70 && pixel.r > pixel.b * 1.5f && pixel.g > pixel.b * 1.2f) goldPixels++;
                Check(goldPixels > 500, "The supplied gold cap material did not appear in the rendered preview.");
                Pass("Graphics device rendered the raised and pressed Button FBX models to button-model-preview.png");
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous; target.Release();
                Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
                ShaderUtil.allowAsyncCompilation = async;
            }
        }

        private static void Near(Vector3 actual, Vector3 expected, string message) => Check(Vector3.Distance(actual, expected) < 0.001f, message + ": " + actual);
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Pass(string message) => File.AppendAllText(Results, "PASS: " + message + "\n");
        private static void Finish(Exception exception = null)
        {
            if (exception != null) { File.AppendAllText(Results, "FAIL: " + exception + "\n"); Debug.LogException(exception); }
            EditorApplication.Exit(exception == null ? 0 : 1);
        }
    }
}
