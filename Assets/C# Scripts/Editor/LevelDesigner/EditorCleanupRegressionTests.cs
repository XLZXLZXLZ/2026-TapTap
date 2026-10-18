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
    public static class EditorCleanupRegressionTests
    {
        private const string Folder = "Assets/EditorCleanupRegression";
        private static readonly List<string> results = new List<string>();
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

        public static void RunBatch()
        {
            if (!Application.isBatchMode || Directory.Exists(Path.Combine(Application.dataPath, "../.git")))
                throw new InvalidOperationException("Run in an isolated batch project without a .git directory.");
            int exit = 0;
            try
            {
                LevelPrefabBuilder.EnsureFolder(Folder);
                AuthoredMap();
                ReadOnlyOpening();
                ValidationBeforeWrites();
                StableTestScene();
                ExistingSelection();
            }
            catch (Exception exception) { results.Add("FAIL: " + exception); Debug.LogException(exception); exit = 1; }
            File.WriteAllLines(Path.Combine(Application.dataPath, "../editor-cleanup-results.txt"), results);
            EditorApplication.Exit(exit);
        }

        private static void Pass(string value) { results.Add("PASS: " + value); Debug.Log(results[results.Count - 1]); }
        private static LevelDefinition Fixture(string name)
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.Size = new Vector2Int(4, 3);
            level.Palette = AssetDatabase.LoadAssetAtPath<LevelPalette>(LevelEditorAssets.DefaultPalettePath);
            var brush = AssetDatabase.LoadAssetAtPath<LevelBrush>("Assets/Runtime/LevelDesigner/Brushes/Terrain.asset");
            level.Put(Vector2Int.zero, brush, null);
            AssetDatabase.CreateAsset(level, Folder + "/" + name + ".asset");
            return level;
        }

        private static void AuthoredMap()
        {
            var map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
            Check(map != null && map.Modules.Count == 22, "Authored map module count changed.");
            List<string> errors = WorldMapValidation.Validate(map);
            Check(errors.Count == 0, string.Join("\n", errors));
            Check(map.Modules[1].Sequence == 4 && map.Modules[5].Sequence == 2,
                "Authored sequence was renumbered by list order.");
            Check(WorldMapRegionCreator.Validate(map, new RectInt(-1000, 1000, 4, 3)) == null,
                "Retired template still restricts region creation.");
            Pass("Existing 22-module map validates without changing IDs, positions or sequence; regions allow free coordinates");
        }

        private static void ReadOnlyOpening()
        {
            LevelDefinition level = Fixture("ReadOnly");
            string palettePath = AssetDatabase.GetAssetPath(level.Palette);
            string before = File.ReadAllText(palettePath);
            int version = EditorUtility.GetDirtyCount(level.Palette);
            var window = ScriptableObject.CreateInstance<LevelEditorWindow>();
            try
            {
                typeof(LevelEditorWindow).GetMethod("UseLayout", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { level });
                Check(File.ReadAllText(palettePath) == before && EditorUtility.GetDirtyCount(level.Palette) == version,
                    "Opening the editor migrated or saved the shared palette.");
            }
            finally { Object.DestroyImmediate(window); }
            Pass("Opening and switching the level editor leaves shared palette assets unchanged");
        }

        private static void ValidationBeforeWrites()
        {
            LevelDefinition level = Fixture("Validation");
            var map = ScriptableObject.CreateInstance<WorldMapDefinition>();
            map.Modules.Add(new WorldMapModule { Level = level, Sequence = 1 });
            AssetDatabase.CreateAsset(map, Folder + "/ValidationMap.asset");
            string before = File.ReadAllText(AssetDatabase.GetAssetPath(map));
            var duplicate = new WorldMapModule { Level = level, Sequence = 1 };
            map.Modules.Add(duplicate);
            EditorUtility.SetDirty(map);
            bool rejected = false;
            try { WorldMapSceneSync.SaveAndSynchronize(map); }
            catch (InvalidOperationException exception) { rejected = exception.Message.Contains("序号"); }
            Check(rejected, "Duplicate sequence did not fail synchronization.");
            Check(File.ReadAllText(AssetDatabase.GetAssetPath(map)) == before && level.OutputPrefab == null,
                "Invalid map was saved or generated before preflight completed.");
            duplicate.Sequence = 2; duplicate.Id = map.Modules[0].Id;
            Check(WorldMapValidation.Validate(map).Any(error => error.Contains("ID")), "Duplicate ID accepted.");
            duplicate.Id = Guid.NewGuid().ToString("N"); duplicate.Level = null;
            Check(WorldMapValidation.Validate(map).Any(error => error.Contains("缺少关卡")), "Missing source silently omitted.");
            duplicate.Level = level;
            level.Placements[0].Cell = new Vector2Int(999, 999);
            Check(WorldMapValidation.Validate(map).Any(error => error.Contains("边界外")), "Invalid level accepted.");
            Pass("Invalid sequence, ID, source and level data are rejected; synchronization fails before saving or generating");
        }

        private static void StableTestScene()
        {
            LevelDefinition level = Fixture("StableScene");
            string levelPath = AssetDatabase.GetAssetPath(level);
            string first = LevelTestSceneBuilder.Generate(level, false);
            level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(levelPath);
            string firstGuid = AssetDatabase.AssetPathToGUID(first);
            Scene scene = SceneManager.GetSceneByPath(first);
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(first, OpenSceneMode.Additive);
            LevelRegion region = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<LevelRegion>(true)).Single();
            int identity = region.GetInstanceID();
            var manual = new GameObject("Artist custom probe");
            manual.transform.SetParent(region.transform.Find("Custom"), false);
            manual.transform.localPosition = new Vector3(2, 1, 3);
            var light = new GameObject("Artist scene light").AddComponent<Light>();
            SceneManager.MoveGameObjectToScene(light.gameObject, scene); light.intensity = 2.75f;
            EditorSceneManager.SaveScene(scene);
            int count = AssetDatabase.FindAssets("t:Scene", new[] { LevelTestSceneBuilder.TestSceneFolder }).Length;
            level.Put(Vector2Int.right, level.Placements[0].Brush, null);
            EditorUtility.SetDirty(level);
            string second = LevelTestSceneBuilder.Generate(level, false);
            Check(second == first && AssetDatabase.AssetPathToGUID(second) == firstGuid, "Regeneration created another scene/GUID.");
            Check(AssetDatabase.FindAssets("t:Scene", new[] { LevelTestSceneBuilder.TestSceneFolder }).Length == count,
                "Updating the test scene accumulated another scene asset.");
            Check(region != null && region.GetInstanceID() == identity && manual != null
                && manual.transform.localPosition == new Vector3(2, 1, 3), "Updating replaced authored region/Custom content.");
            Check(light != null && Mathf.Approximately(light.intensity, 2.75f), "Updating rewrote artist scene lighting.");
            Check(region.transform.Find("Generated").GetComponentsInChildren<WorldSurface>(true).Length == 2,
                "Updated terrain did not reach the existing test scene.");
            Check(scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PlayerController>(true)).Count() == 1
                && scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<LevelSceneRuntime>(true)).Count() == 1,
                "Updating duplicated player/runtime objects.");
            EditorSceneManager.CloseScene(scene, true);
            Pass("Test scene update reuses path, GUID, region, Custom objects and lights while refreshing terrain without duplicate player/runtime");
        }

        private static void ExistingSelection()
        {
            var map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
            string path = AssetDatabase.GUIDToAssetPath(map.SelectionSceneGuid);
            string before = File.ReadAllText(path);
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var menu = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<WorldLevelMenu>(true)).Single();
            WorldMapAssemblyView view = menu.GetComponent<WorldMapAssemblyView>();
            Check(view != null && view.AvailableModules(map).Count == 20, "Authored selection binding no longer resolves its 20 pieces.");
            Check(scene.GetRootGameObjects().All(root => root.name != "World Outline Preview"), "Legacy selection renderer was recreated.");
            EditorSceneManager.CloseScene(scene, true);
            Check(File.ReadAllText(path) == before, "Selection lookup rewrote its authored scene.");
            Pass("Existing authored selection resolves its 20 pieces without regenerating UI or writing its scene");
        }
    }
}
