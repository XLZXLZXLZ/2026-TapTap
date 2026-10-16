using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Run in an isolated copy: -batchmode -executeMethod TapTap.Editor.LevelStyleRegressionTests.RunBatch
    public static class LevelStyleRegressionTests
    {
        private const string Pending = "TapTap.LevelStyles.Pending";
        private static readonly string[] Names = { "Region1_Red", "Region2_Blue", "Region3_Yellow", "Region4_Other" };
        private static string Results => Path.Combine(Application.dataPath, "../level-style-results.txt");
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Pass(string message) => File.AppendAllText(Results, "PASS: " + message + "\n");

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated project.");
            File.WriteAllText(Results, "");
            try
            {
                var levels = AssetDatabase.FindAssets("t:LevelDefinition", new[] { "Assets/Runtime/Levels" })
                    .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<LevelDefinition>).ToArray();
                var layouts = levels.ToDictionary(l => l, LayoutSignature);
                var physics = PhysicsSignatures();
                LevelStyleAssets.InstallPresets();
                foreach (var entry in layouts) Check(entry.Value == LayoutSignature(entry.Key), "Gameplay layout changed: " + entry.Key.name);
                Check(physics.SequenceEqual(PhysicsSignatures()), "Existing 2D collisions or mechanisms changed.");
                Pass("All placements, boundaries, cameras, 2D colliders and mechanism settings preserved");

                LevelStyle[] styles = Styles();
                Check(styles.All(s => s != null && s.TerrainPrefab != null && s.TerrainModel != null), "Style assets incomplete.");
                Check(styles.Select(s => s.TerrainPrefab).Distinct().Count() == 4, "Four distinct brick prefabs required.");
                Check(styles.All(s => !s.HasModelOverrides), "Non-brick models must remain unchanged in the presets.");
                foreach (LevelStyle style in styles)
                {
                    Check(style.Models.Count >= 14, "Segment-model configuration slots missing.");
                    Check(style.Prefabs.All(p => p.Prefab == p.Brush.Prefab), "Non-brick prefab changed in a style preset.");
                    Material[] originalMaterials = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Terrain/Grid/Block_1_Visual.prefab")
                        .GetComponentInChildren<MeshRenderer>(true).sharedMaterials;
                    Material[] styledMaterials = style.TerrainModel.GetComponentInChildren<MeshRenderer>(true).sharedMaterials;
                    Check(styledMaterials.Length == originalMaterials.Length && styledMaterials.Skip(1).SequenceEqual(originalMaterials.Skip(1)),
                        "Only the main brick material may change.");
                    var box = style.TerrainPrefab.GetComponent<BoxCollider2D>();
                    Check(box != null && box.size == Vector2.one && !box.isTrigger, "Styled brick changed gameplay collision.");
                }
                var map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
                var counts = new int[4]; var active = new HashSet<LevelDefinition>();
                foreach (WorldMapModule module in map.OrderedModules())
                {
                    int sequence = map.SequenceNumber(module);
                    int index = sequence <= 5 ? 0 : sequence <= 12 ? 1 : sequence <= 20 ? 2 : 3;
                    Check(module.Level.Style == styles[index], "Wrong style for sequence " + sequence + ": " + module.Level.name);
                    counts[index]++; active.Add(module.Level);
                }
                Check(counts.SequenceEqual(new[] { 5, 7, 8, 1 }), "World group sizes incorrect.");
                Check(levels.Where(l => !active.Contains(l)).All(l => l.Style == styles[3]), "Non-world levels must use the fourth style.");
                Pass("Sequence 1-5 red, 6-12 blue, 13-20 yellow, 21 and all other levels gray; four brick prefabs and 14 configurable model slots per style");
                VerifyModelSelection();
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) RenderPreview(styles);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionState.SetBool(Pending, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static string LayoutSignature(LevelDefinition level)
        {
            // The style is the sole new authoring value.
            var old = level.Style; level.Style = null;
            string result = JsonUtility.ToJson(level); level.Style = old;
            return WithoutInstanceIds(result);
        }
        private static string WithoutInstanceIds(string value) => Regex.Replace(value, "\"instanceID\":-?\\d+", "\"instanceID\":0");
        private static string[] PhysicsSignatures()
        {
            var result = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Levels" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (Collider2D box in asset.GetComponentsInChildren<Collider2D>(true))
                    result.Add(path + "|" + box.name + "|" + WithoutInstanceIds(EditorJsonUtility.ToJson(box))
                        + "|" + box.transform.localPosition.ToString("F6") + "|" + box.transform.localRotation.ToString("F6")
                        + "|" + box.transform.localScale.ToString("F6") + "|" + AssetDatabase.GetAssetPath(box.sharedMaterial));
                foreach (PhaseBlock block in asset.GetComponentsInChildren<PhaseBlock>(true))
                    result.Add(path + "|phase|" + block.name + "|" + block.SolidWhenActive);
                foreach (WorldSurface surface in asset.GetComponentsInChildren<WorldSurface>(true))
                    result.Add(path + "|surface|" + surface.name + "|" + WithoutInstanceIds(JsonUtility.ToJson(surface)));
            }
            result.Sort(StringComparer.Ordinal); return result.ToArray();
        }
        private static LevelStyle[] Styles() => Names.Select(n => AssetDatabase.LoadAssetAtPath<LevelStyle>(LevelStyleAssets.StyleFolder + "/" + n + ".asset")).ToArray();

        private static void VerifyModelSelection()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            var style = ScriptableObject.CreateInstance<LevelStyle>();
            var level = ScriptableObject.CreateInstance<LevelDefinition>(); level.Style = style;
            try
            {
                var root = new GameObject("Style model fixture"); SceneManager.MoveGameObjectToScene(root, scene);
                root.AddComponent<LevelRegion>().Source = level;
                var generated = new GameObject("Generated"); generated.transform.SetParent(root.transform, false);
                var marker = new GameObject("Style Test Model"); SceneManager.MoveGameObjectToScene(marker, scene);
                new GameObject("Style Test Model Child").transform.SetParent(marker.transform, false);
                var platform = Spawn<PlatformTileVisual>("Assets/Prefabs/Terrain/Grid/GridPlatform.prefab", generated.transform, scene);
                var conveyor = Spawn<ConveyorTileVisual>("Assets/Prefabs/Terrain/Grid/GridConveyor.prefab", generated.transform, scene);
                var piston = Spawn<PhasePiston>(PistonModelAssets.PistonPath, generated.transform, scene);
                foreach (GameObject source in new[] { platform.SingleModel, conveyor.LeftModel, piston.BaseModel })
                    style.Models.Add(new LevelStyleModel { SourceModel = source, Model = marker });
                Check(LevelStyle.For(platform.transform) == style, "Preview hierarchy did not resolve its level style.");
                var view = root.AddComponent<LevelStyleView>(); view.Refresh();
                foreach (Component item in new Component[] { platform, conveyor, piston })
                {
                    if (item == platform) platform.ApplyPart(PlatformPart.Single);
                    if (item == conveyor) conveyor.ApplyPart(ConveyorPart.Left);
                    Check(item.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("Style Test Model", StringComparison.Ordinal) && EnabledInTree(t, item.transform)),
                        "Style model selection failed: " + item.GetType().Name);
                }
                level.Style = null; view.Refresh();
                foreach (Component item in new Component[] { platform, conveyor, piston })
                    Check(!item.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("Style Test Model", StringComparison.Ordinal) && EnabledInTree(t, item.transform)),
                        "Clearing style did not restore original model.");
                Pass("Platform, conveyor and piston segment models resolve by scene style and restore correctly when style is cleared");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); Object.DestroyImmediate(level); Object.DestroyImmediate(style); }
        }
        private static T Spawn<T>(string path, Transform parent, Scene scene) where T : Component
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
            root.transform.SetParent(parent, false); return root.GetComponent<T>();
        }
        private static bool EnabledInTree(Transform item, Transform root)
        {
            for (Transform current = item; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == root) return true;
            }
            return false;
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
            try
            {
                foreach (LevelStyle style in Styles())
                {
                    var source = ScriptableObject.CreateInstance<LevelDefinition>(); source.Style = style;
                    var root = new GameObject(style.name); root.AddComponent<LevelRegion>().Source = source;
                    var generated = new GameObject("Generated"); generated.transform.SetParent(root.transform, false);
                    var brick = Object.Instantiate(style.TerrainBrush.Prefab, generated.transform);
                    var reveal = root.AddComponent<RegionRevealView>();
                    root.AddComponent<LevelStyleView>().Refresh();
                    var renderers = brick.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.gameObject.activeInHierarchy).ToArray();
                    Check(renderers.Length > 0 && renderers.All(r => r.sharedMaterials[0].name.StartsWith(style.name, StringComparison.Ordinal)), "Runtime brick style not applied.");
                    reveal.SetDiscovered(false); Check(renderers.All(r => r.forceRenderingOff), "Undiscovered region leaked styled models.");
                    reveal.SetDiscovered(true); Check(renderers.All(r => !r.forceRenderingOff), "Styled models did not reveal.");
                    source.Style = null; root.GetComponent<LevelStyleView>().Refresh();
                    Check(brick.GetComponent<TerrainTileVisual>().LegacyVisual.activeSelf, "Clearing style did not restore original brick.");
                    Object.DestroyImmediate(root); Object.DestroyImmediate(source);
                }
                Pass("Four styles load on existing common terrain prefabs in play mode, follow discovery visibility and support clearing the style");
                Pass("COMPLETE"); EditorApplication.Exit(0);
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static void RenderPreview(LevelStyle[] styles)
        {
            Type type = Type.GetType("UnityEngine.Rendering.DebugManager, Unity.RenderPipelines.Core.Runtime");
            object debug = type?.GetProperty("instance")?.GetValue(null); type?.GetMethod("Reset", Type.EmptyTypes)?.Invoke(debug, null);
            Shader.DisableKeyword("DEBUG_DISPLAY"); bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            for (int i = 0; i < styles.Length; i++)
            {
                var brick = (GameObject)PrefabUtility.InstantiatePrefab(styles[i].TerrainPrefab, scene);
                brick.transform.position = new Vector3((i - 1.5f) * 1.5f, 0f, 0f);
            }
            Camera camera = new GameObject("Style preview camera").AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1.35f;
            camera.transform.position = new Vector3(0.2f, 1.9f, -8f); camera.transform.LookAt(new Vector3(0f, 0f, 0.5f));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.12f, 0.15f, 0.19f);
            Light light = new GameObject("Key light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
            var target = new RenderTexture(1200, 420, 24); var pixels = new Texture2D(1200, 420, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render(); camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(Application.dataPath, "../level-styles-preview.png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
                ShaderUtil.allowAsyncCompilation = async;
            }
        }
        private static void Fail(Exception exception)
        {
            File.AppendAllText(Results, "FAIL: " + exception + "\n"); Debug.LogException(exception); EditorApplication.Exit(1);
        }
    }
}
