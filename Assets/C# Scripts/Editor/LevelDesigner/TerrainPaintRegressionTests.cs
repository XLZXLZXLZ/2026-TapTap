using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    [InitializeOnLoad]
    public static class TerrainPaintRegressionTests
    {
        private const string Folder = "Assets/TerrainPaintRegression";
        private const string Session = "TapTap.TerrainPaintRegression";
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../TerrainPaintValidation"));
        private static double deadline;

        static TerrainPaintRegressionTests()
        {
            if (!SessionState.GetBool(Session, false)) return;
            deadline = EditorApplication.timeSinceStartup + 60;
            EditorApplication.update += RuntimeTick;
        }

        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Pass(string message)
        {
            File.AppendAllText(Path.Combine(Output, "results.txt"), "PASS: " + message + "\n");
            Debug.Log(message);
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode || Directory.Exists(Path.Combine(Application.dataPath, "../.git")))
                throw new InvalidOperationException("Use an isolated batch project without .git.");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "results.txt"), "");
            try
            {
                ValidatePalettes();
                LevelPrefabBuilder.EnsureFolder(Folder);
                var map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
                LevelDefinition authored = map.OrderedModules()[0].Level;
                var level = ScriptableObject.CreateInstance<LevelDefinition>();
                level.Size = new Vector2Int(10, 3); level.Palette = authored.Palette; level.Style = authored.Style;
                AssetDatabase.CreateAsset(level, Folder + "/Fixture.asset");
                var profile = ScriptableObject.CreateInstance<TerrainPaintProfile>();
                profile.Source = level; profile.BaseMaterial = authored.TerrainPaint.BaseMaterial;
                profile.Variants = new List<Material>(authored.TerrainPaint.Variants); profile.PreviewModuleId = "module-a";
                AssetDatabase.CreateAsset(profile, Folder + "/Paint.asset"); level.TerrainPaint = profile;
                LevelBrush brush = level.Style.TerrainBrush;
                for (int x = 0; x < 5; x++) level.Put(new Vector2Int(x, 0), brush, null);
                level.Put(new Vector2Int(8, 0), brush, null);
                EditorUtility.SetDirty(level); AssetDatabase.SaveAssetIfDirty(level);
                GameObject prefab = LevelPrefabBuilder.Save(level, Folder + "/Fixture.prefab");
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                LevelRegion a = Instantiate(prefab, "module-a", Vector3.zero, scene);
                LevelRegion b = Instantiate(prefab, "module-b", new Vector3(0, 3, 0), scene);
                TerrainPaintRegion index = TerrainPaintSceneIndex.Build().Single(entry => entry.Region == a);
                Check(index.Tiles.Count == 6 && index.Tiles.Values.All(tile => tile.CanPaint), "Scene index did not find real paintable models.");
                var tiles = index.Tiles;
                var stroke = new TerrainPaintStroke();
                for (int x = 0; x < 3; x++) stroke.Apply(tiles[new Vector2Int(x, 0)], profile.Variants[0]);
                Check(!stroke.Apply(tiles[Vector2Int.zero], profile.Variants[0]) && stroke.Count == 3, "A stroke did not deduplicate cells.");
                stroke.Finish();
                Check(Enumerable.Range(0, 3).All(x => Painted(tiles[new Vector2Int(x, 0)], profile.Variants[0])), "Live materials did not update.");
                Check(b.GetComponentsInChildren<TerrainTileVisual>().All(tile => tile.PaintMaterial == null), "Paint leaked into another module using the same prefab.");
                Pass("Real scene models update immediately; duplicate hits are ignored and repeated modules stay independent");

                Undo.PerformUndo(); TerrainPaintSceneIndex.RefreshLoadedPaint();
                Check(tiles.Values.All(tile => tile.PaintMaterial == null), "One undo did not revert the whole stroke.");
                Undo.PerformRedo(); TerrainPaintSceneIndex.RefreshLoadedPaint();
                Check(Enumerable.Range(0, 3).All(x => Painted(tiles[new Vector2Int(x, 0)], profile.Variants[0])), "Redo did not restore the stroke.");
                stroke = new TerrainPaintStroke(); stroke.Apply(tiles[Vector2Int.right], null); stroke.Finish();
                Check(Painted(tiles[Vector2Int.right], profile.BaseMaterial), "Eraser did not restore original material.");
                Pass("A complete stroke supports one-step undo/redo; erasing restores the base material");

                Check(TerrainPaintSceneIndex.Fill(index, new Vector2Int(3, 0)).Count == 2, "Fill crossed a differently painted tile or a gap.");
                Check(TerrainPaintSceneIndex.Line(new Vector2Int(-4, -3), new Vector2Int(7, 5)).Count() == 12, "Fast brush interpolation left gaps.");
                var farA = new Vector2Int(-1000000, 0); var farB = new Vector2Int(1000000, 0);
                Check(TerrainPaintSceneIndex.ClipLine(ref farA, ref farB, new RectInt(0, 0, 10, 3))
                    && farA.x == 0 && farB.x == 9, "Far-off pointer rays were not clipped before rasterization.");
                a.transform.SetPositionAndRotation(new Vector3(12, -5, 0), Quaternion.Euler(0, 0, 20));
                Vector3 point = a.transform.TransformPoint(new Vector3(2.5f, 0.5f, 0) * a.UnitSize);
                Check(TerrainPaintSceneIndex.Project(new Ray(point - a.transform.forward * 10, a.transform.forward), a, out Vector2Int cell)
                    && cell == new Vector2Int(2, 0), "Picking failed for a moved/rotated region.");
                a.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Pass("Brush interpolation covers fast drags; fill respects appearance and connectivity; picking follows region transforms");

                level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Folder + "/Fixture.asset");
                profile = level.TerrainPaint;
                string firstId = tiles[Vector2Int.zero].PlacementId;
                profile.CopyModule("module-a", "module-c");
                Check(profile.GetPaint("module-c", firstId) == profile.Variants[0] && profile.GetPaint("module-b", firstId) == null,
                    "Module copy lost or shared paint: " + firstId + " / " + string.Join(";", profile.Entries.Select(entry => entry.ModuleId + ":" + entry.PlacementId + ":" + entry.Material.name)));
                profile.SetPaint("module-c", firstId, profile.Variants[1]);
                Check(profile.GetPaint("module-a", firstId) == profile.Variants[0], "Copied paint still shares mutable records.");
                Clipboard(level, profile);
                Pass("Duplicated modules and editor copy/paste carry independent paint records");

                TerrainTileVisual first = tiles[Vector2Int.zero];
                int rendererId = first.GetComponentInChildren<Renderer>().GetInstanceID();
                a.GetComponent<LevelStyleView>().Refresh(true);
                Check(first.GetComponentInChildren<Renderer>().GetInstanceID() != rendererId && Painted(first, profile.Variants[0]),
                    "Model refresh erased paint or reused old model.");
                profile.SetPaint("module-a", level.Find(Vector2Int.right, brush.LayerId).Id, null);
                level.Placements[0].Cell = new Vector2Int(0, 1);
                EditorUtility.SetDirty(level); AssetDatabase.SaveAssetIfDirty(level);
                prefab = LevelPrefabBuilder.Save(level, Folder + "/Fixture.prefab");
                Object.DestroyImmediate(a.gameObject); Object.DestroyImmediate(b.gameObject);
                a = Instantiate(prefab, "module-a", Vector3.zero, scene);
                b = Instantiate(prefab, "module-b", new Vector3(0, 3, 0), scene);
                first = a.GetComponentsInChildren<TerrainTileVisual>().Single(tile => tile.PlacementId == firstId);
                Check(Painted(first, profile.Variants[0]), "Full Generated rebuild or placement movement erased paint.");
                LevelRegion standalone = Instantiate(prefab, "", new Vector3(0, 6, 0), scene);
                Check(Painted(standalone.GetComponentsInChildren<TerrainTileVisual>().Single(tile => tile.PlacementId == firstId), profile.Variants[0]),
                    "Single-level test did not use configured module preview.");
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                Pass("Forced style/model refresh and full Generated rebuild preserve paint by ID; standalone test scenes preview the configured module");

                Check(EditorSceneManager.SaveScene(scene, Folder + "/Fixture.unity"), "Could not save fixture scene.");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                scene = EditorSceneManager.OpenScene(Folder + "/Fixture.unity");
                CheckScenePaint(scene);
                Pass("Saved scene reopens with paint restored; no source material or source model is modified");
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) Capture(scene);
                TerrainPaintWindow window = EditorWindow.GetWindow<TerrainPaintWindow>(); window.Show(); window.Repaint();
                SessionState.SetBool(Session, true); deadline = EditorApplication.timeSinceStartup + 60;
                EditorApplication.update -= RuntimeTick; EditorApplication.update += RuntimeTick;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static LevelRegion Instantiate(GameObject prefab, string moduleId, Vector3 position, Scene scene)
        {
            var region = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, scene)).GetComponent<LevelRegion>();
            region.WorldModuleId = moduleId; region.transform.position = position;
            region.name = "Region-" + (string.IsNullOrEmpty(moduleId) ? "standalone" : moduleId);
            region.GetComponent<LevelStyleView>().Refresh();
            LevelPrefabBuilder.RecordInstanceOverrides(region.gameObject);
            return region;
        }

        private static bool Painted(TerrainTileVisual tile, Material material) =>
            tile.GetComponentsInChildren<Renderer>(true).Any(renderer => renderer.sharedMaterials.Contains(material));

        private static void Clipboard(LevelDefinition level, TerrainPaintProfile profile)
        {
            var window = ScriptableObject.CreateInstance<LevelEditorWindow>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                typeof(LevelEditorWindow).GetMethod("UseLayout", flags).Invoke(window, new object[] { level });
                typeof(LevelEditorWindow).GetField("hasAreaSelection", flags).SetValue(window, true);
                typeof(LevelEditorWindow).GetField("selectionArea", flags).SetValue(window, new RectInt(0, 0, 1, 1));
                typeof(LevelEditorWindow).GetMethod("CopyArea", flags).Invoke(window, null);
                typeof(LevelEditorWindow).GetMethod("PasteArea", flags).Invoke(window, new object[] { new Vector2Int(6, 0) });
                Check(profile.GetPaint("module-a", level.Find(new Vector2Int(6, 0), level.Style.TerrainBrush.LayerId).Id) == profile.Variants[0],
                    "Editor clipboard lost terrain paint.");
            }
            finally { Object.DestroyImmediate(window); }
        }

        private static void ValidatePalettes()
        {
            var levels = AssetDatabase.FindAssets("t:LevelDefinition", new[] { "Assets/Runtime/Levels" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<LevelDefinition>(AssetDatabase.GUIDToAssetPath(guid))).ToArray();
            var materials = new HashSet<Material>();
            foreach (LevelDefinition level in levels)
            {
                TerrainPaintProfile profile = level.TerrainPaint;
                Check(profile != null && profile.Source == level && profile.Variants.Count == 3, "Missing palette for " + level.name);
                Color source = TerrainPaintAssets.ReadColor(profile.BaseMaterial);
                foreach (Material material in profile.Variants)
                {
                    Check(material != null && materials.Add(material) && material != profile.BaseMaterial && material.shader == profile.BaseMaterial.shader,
                        "Variant reused or changed shader: " + level.name);
                    Color color = TerrainPaintAssets.ReadColor(material);
                    Check(color != source && Vector3.Distance(new Vector3(color.r, color.g, color.b), new Vector3(source.r, source.g, source.b)) < 0.25f,
                        "Variant is identical or too far from the base color: " + level.name);
                    foreach (string property in material.GetTexturePropertyNames())
                        Check(material.GetTexture(property) == profile.BaseMaterial.GetTexture(property), "Variant lost a source texture.");
                }
            }
            Pass(levels.Length + " levels have exactly 3 independent nearby-color variants preserving source shaders and textures (" + materials.Count + " materials)");
        }

        public static void ValidateExistingScene()
        {
            var map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
            EditorSceneManager.OpenScene(map.PlayScenePath);
            var regions = TerrainPaintSceneIndex.Build();
            Check(regions.Count >= 20, "Existing gameplay scene cannot be painted without regeneration.");
            TerrainPaintRegion first = regions.First(entry => entry.Tiles.Values.Any(tile => tile.CanPaint));
            TerrainTileVisual tile = first.Tiles.Values.First(item => item.CanPaint);
            TerrainPaintProfile profile = first.Profile;
            Material previous = tile.PaintMaterial;
            string sourcePath = AssetDatabase.GetAssetPath(profile.BaseMaterial);
            string sourceBefore = File.ReadAllText(sourcePath);
            try
            {
                var stroke = new TerrainPaintStroke(); stroke.Apply(tile, profile.Variants[1]); stroke.Finish();
                Check(Painted(tile, profile.Variants[1]), "Legacy saved terrain did not accept paint.");
                tile.ApplyModel(first.Region.Source.Style.TerrainModel, true);
                Check(Painted(tile, profile.Variants[1]), "Legacy saved terrain lost paint during model refresh.");
                Check(File.ReadAllText(sourcePath) == sourceBefore, "Source material was modified.");
            }
            finally
            {
                profile.SetPaint(first.Region.WorldModuleId, tile.PlacementId, previous);
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile); tile.RefreshPaint();
            }
            Pass("Existing gameplay scene exposes " + regions.Count + " paintable regions without map synchronization; legacy placement IDs and source material protection verified");
        }

        private static void CheckScenePaint(Scene scene)
        {
            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Folder + "/Fixture.asset");
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                LevelRegion region = root.GetComponent<LevelRegion>(); if (region == null) continue;
                foreach (TerrainTileVisual tile in region.GetComponentsInChildren<TerrainTileVisual>())
                {
                    Material expected = level.TerrainPaint.GetPaint(region.WorldModuleId, tile.PlacementId) ?? level.TerrainPaint.BaseMaterial;
                    Check(Painted(tile, expected), "Scene/runtime material differs from stored paint: " + root.name + " / " + tile.PlacementId);
                }
            }
        }

        private static void RuntimeTick()
        {
            if (!SessionState.GetBool(Session, false)) return;
            try
            {
                Check(EditorApplication.timeSinceStartup < deadline, "Runtime test timed out.");
                if (!EditorApplication.isPlaying || Time.frameCount < 4) return;
                CheckScenePaint(SceneManager.GetActiveScene());
                Pass("Play mode restores the same per-module paint without material instantiation");
                SessionState.SetBool(Session, false); EditorApplication.update -= RuntimeTick;
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static void Capture(Scene scene)
        {
            var cameraObject = new GameObject("Paint validation camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 4.5f;
            camera.transform.position = new Vector3(4.5f, 3.5f, -15); camera.backgroundColor = new Color(0.12f, 0.14f, 0.17f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            var lightObject = new GameObject("Paint validation light"); SceneManager.MoveGameObjectToScene(lightObject, scene);
            Light light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(25, -20, 0);
            var texture = new RenderTexture(1200, 800, 24); texture.Create(); camera.targetTexture = texture;
            camera.Render(); RenderTexture previous = RenderTexture.active; RenderTexture.active = texture;
            var image = new Texture2D(1200, 800, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output, "painted-scene.png"), image.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null; texture.Release();
            Object.DestroyImmediate(image); Object.DestroyImmediate(texture);
            Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject);
        }

        private static void Fail(Exception exception)
        {
            File.AppendAllText(Path.Combine(Output, "results.txt"), "FAIL: " + exception + "\n");
            Debug.LogException(exception); SessionState.SetBool(Session, false); EditorApplication.update -= RuntimeTick;
            EditorApplication.Exit(1);
        }
    }
}
