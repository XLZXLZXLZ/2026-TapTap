using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Run only in an isolated project: -batchmode -executeMethod TapTap.Editor.PistonModelRegressionTests.RunBatch
    public static class PistonModelRegressionTests
    {
        private const string Pending = "TapTap.PistonModels.Pending";
        private static string Results => Path.Combine(Application.dataPath, "../piston-model-results.txt");
        private static IEnumerator runtime;
        private static double deadline;
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Pass(string message) { File.AppendAllText(Results, "PASS: " + message + "\n"); }

        public static void RunCompatibilityBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) RenderPreview();
            SmartTileRegressionTests.RunBatch();
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project.");
            File.WriteAllText(Results, "");
            try
            {
                var sources = Directory.GetFiles("Assets/Runtime/Levels", "*.asset").ToDictionary(p => p, File.ReadAllBytes);
                var before = CollisionSignatures();
                PistonModelAssets.InstallModels();
                Check(before.SequenceEqual(CollisionSignatures()), "Level piston directions, lengths, collider bounds or polarity changed.");
                foreach (var pair in sources) Check(pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)), "Source layout changed: " + pair.Key);
                Pass("All source layouts unchanged; 43 saved level pistons retain original direction, length, bounds and switch polarity");
                Check(before.Length == 43, "Expected 43 saved level pistons.");
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Levels" }))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                    foreach (var piston in prefab.GetComponentsInChildren<PhasePiston>(true)) VerifyAssembly(piston);
                }
                Pass("All saved level pistons use the Telescopic mesh models and thin-cover assembly");
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) RenderPreview();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionState.SetBool(Pending, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static string[] CollisionSignatures()
        {
            var result = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Levels" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var piston in prefab.GetComponentsInChildren<PhasePiston>(true))
                {
                    var box = piston.GetComponent<BoxCollider2D>();
                    result.Add(path + "|" + piston.name + "|" + piston.QuarterTurns + "|" + piston.MaxLength + "|"
                        + piston.GetComponent<PhaseBlock>().SolidWhenActive + "|" + box.size.ToString("F6") + "|" + box.offset.ToString("F6")
                        + "|" + piston.transform.localPosition.ToString("F6") + "|" + piston.transform.localScale.ToString("F6"));
                }
            }
            result.Sort(StringComparer.Ordinal);
            return result.ToArray();
        }

        private static void VerifyAssembly(PhasePiston piston)
        {
            Check(piston.UseThinCover, "Thin-cover mode missing: " + piston.name);
            Check(piston.BaseModel.name == "TelescopicBase" && piston.BodyModel.name == "TelescopicBody"
                && piston.TipModel.name == "TelescopicCover", "Wrong model slots: " + piston.name);
            Transform visual = piston.transform.Find("Piston Visual");
            Check(visual != null && visual.childCount == piston.MaxLength + 2, "Saved visual references/body count incorrect: " + piston.name);
            int activeAssemblies = 0;
            foreach (Transform child in piston.transform)
                if (child.name.StartsWith("Piston Visual", StringComparison.Ordinal) && child.gameObject.activeSelf) activeAssemblies++;
            Check(activeAssemblies == 1, "Duplicate visible assemblies after shared prefab update: " + piston.name);
            Check(visual.GetComponentsInChildren<MeshRenderer>(true).Length == piston.MaxLength + 2, "Meshes missing from saved visual.");
            Check(visual.GetComponentsInChildren<SpriteRenderer>(true).Length == 0, "Placeholder sprite survived in active assembly.");
            foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    Check(material != null && material.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal), "Incompatible material.");
        }

        [InitializeOnLoadMethod] private static void Subscribe()
        {
            EditorApplication.playModeStateChanged -= Changed;
            EditorApplication.playModeStateChanged += Changed;
        }
        private static void Changed(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            runtime = Runtime(); deadline = EditorApplication.timeSinceStartup + 30;
            EditorApplication.update += Tick;
        }
        private static void Tick()
        {
            try
            {
                Check(EditorApplication.timeSinceStartup < deadline, "Runtime tests timed out.");
                if (runtime.MoveNext()) return;
                EditorApplication.update -= Tick;
                Pass("COMPLETE: Telescopic art, fixed cover thickness, four directions, single/multiple cells, inverse switches and rewind");
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { EditorApplication.update -= Tick; Fail(exception); }
        }
        private static IEnumerator Runtime()
        {
            var state = WorldPhaseState.Instance;
            state.RestoreState(false);
            var all = new List<PhasePiston>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PistonModelAssets.PistonPath);
            foreach (int turns in new[] { 0, 1, 2, 3 })
                foreach (int length in new[] { 1, 3, 8 })
                    foreach (bool polarity in new[] { false, true })
                    {
                        var root = Object.Instantiate(prefab);
                        root.transform.position = new Vector3(all.Count * 12, 100);
                        root.GetComponent<PhaseBlock>().SolidWhenActive = polarity;
                        var piston = root.GetComponent<PhasePiston>();
                        piston.Configure(turns, length);
                        all.Add(piston);
                    }
            foreach (bool active in new[] { true, false })
            {
                state.RestoreState(active);
                foreach (var piston in all) CheckPose(piston, active);
                state.SetActive(!active);
                float start = Time.time;
                while (Time.time - start < 0.3f)
                {
                    foreach (var piston in all) CheckCover(piston);
                    yield return null;
                }
                foreach (var piston in all) CheckPose(piston, !active);
            }
            Pass("24 real pistons: fixed-thickness cover stays visible throughout extension/retraction and seats on base after rewind; polarity and collider toggle immediately");
            foreach (var piston in all) Object.DestroyImmediate(piston.gameObject);
        }
        private static void CheckCover(PhasePiston piston)
        {
            Transform tip = ActiveVisual(piston).Find("Tip");
            Check(tip.gameObject.activeInHierarchy, "Cover vanished.");
            Bounds bounds = PlayerModelAssets.LocalBounds(tip);
            Check(Mathf.Abs(bounds.size.x * tip.localScale.x - piston.CoverThickness) < 0.001f, "Cover thickness changed during animation.");
            Check(Mathf.Abs(tip.localPosition.x - Mathf.Max(0f, piston.MaxLength * piston.VisualExtension - piston.CoverThickness)) < 0.001f,
                "Cover separated from body.");
        }
        private static void CheckPose(PhasePiston piston, bool active)
        {
            CheckCover(piston);
            bool extended = active == piston.GetComponent<PhaseBlock>().SolidWhenActive;
            Check(piston.GetComponent<BoxCollider2D>().enabled == extended, "Collider polarity changed.");
            Check(Mathf.Abs(piston.VisualExtension - (extended ? 1f : 0f)) < 0.001f, "Visual state incorrect.");
            Transform visual = ActiveVisual(piston);
            Transform tip = visual.Find("Tip");
            float bodyEnd = extended ? piston.MaxLength - piston.CoverThickness : 0f;
            Check(Mathf.Abs(tip.localPosition.x - bodyEnd) < 0.001f, "Resting/extended cover position incorrect.");
            float filled = 0;
            for (int i = 0; i < piston.MaxLength; i++)
            {
                Transform body = visual.Find("Body " + i);
                if (body.gameObject.activeSelf) filled += body.localScale.x;
            }
            Check(Mathf.Abs(filled - bodyEnd) < 0.001f, "Body does not fill the full extended range, including length one.");
        }

        private static Transform ActiveVisual(PhasePiston piston)
        {
            // Runtime Configure retires the old assembly with Destroy, which completes at frame end.
            return piston.transform.Cast<Transform>().Single(t => t.name == "Piston Visual" && t.gameObject.activeSelf);
        }

        private static void RenderPreview()
        {
            // Reset any Rendering Debugger state copied with the isolated project's editor cache.
            Type debugType = Type.GetType("UnityEngine.Rendering.DebugManager, Unity.RenderPipelines.Core.Runtime");
            object debug = debugType?.GetProperty("instance")?.GetValue(null);
            debugType?.GetMethod("Reset", Type.EmptyTypes)?.Invoke(debug, null);
            Shader.DisableKeyword("DEBUG_DISPLAY");
            bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PistonModelAssets.PistonPath);
            foreach (int row in new[] { 0, 1, 2 })
                foreach (int length in new[] { 1, 3, 5 })
                {
                    var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    root.transform.position = new Vector3(length == 1 ? -7f : length == 3 ? -3f : 2f, 3f - row * 3f, 0f);
                    var piston = root.GetComponent<PhasePiston>(); piston.Configure(0, length);
                    piston.SetExtended(row != 2, true);
                    if (row == 1) piston.transform.rotation = Quaternion.Euler(0f, 40f, 0f);
                }
            Camera camera = new GameObject("Preview Camera").AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 6.5f;
            camera.transform.position = new Vector3(0f, 0f, -20f);
            camera.backgroundColor = new Color(0.12f, 0.15f, 0.19f); camera.clearFlags = CameraClearFlags.SolidColor;
            Light light = new GameObject("Key Light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
            File.WriteAllLines(Path.Combine(Application.dataPath, "../piston-material-diagnostics.txt"),
                Object.FindObjectsOfType<MeshRenderer>().SelectMany(r => r.sharedMaterials).Distinct()
                    .Select(m => m.name + " | " + m.shader.name + " | " + (m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "no base color")));
            var target = new RenderTexture(1440, 1080, 24); var pixels = new Texture2D(1440, 1080, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render(); camera.Render(); RenderTexture.active = target;
                File.AppendAllText(Path.Combine(Application.dataPath, "../piston-material-diagnostics.txt"),
                    "Pipeline: " + GraphicsSettings.currentRenderPipeline + "; DEBUG_DISPLAY=" + Shader.IsKeywordEnabled("DEBUG_DISPLAY")
                    + "; materialMode=" + Shader.GetGlobalInt("_DebugMaterialMode") + "; validation=" + Shader.GetGlobalInt("_DebugMaterialValidationMode") + "\n");
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(Application.dataPath, "../piston-model-preview.png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous; target.Release();
                Object.DestroyImmediate(target); Object.DestroyImmediate(pixels); ShaderUtil.allowAsyncCompilation = async;
            }
        }
        private static void Fail(Exception exception)
        {
            File.AppendAllText(Results, "FAIL: " + exception + "\n"); Debug.LogException(exception); EditorApplication.Exit(1);
        }
    }
}
