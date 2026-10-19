using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class IndicatorLightRegressionTests
    {
        private const string ScenePath = "Assets/Scenes/WorldMaps/World_Character_Play.unity";
        private const string Folder = "Assets/IndicatorLightRegression";

        public static void RunBatch()
        {
            if (!Application.isBatchMode || Directory.Exists(Path.Combine(Application.dataPath, "../.git")))
                throw new InvalidOperationException("Run in an isolated batch project without a .git directory.");
            var results = new List<string>();
            int exit = 0;
            try
            {
                Check(LevelBrushHandlers.Get("indicator-light") == null, "Retired light handler is still registered.");
                var palette = AssetDatabase.LoadAssetAtPath<LevelPalette>(LevelEditorAssets.DefaultPalettePath);
                ProgressionEditorAssets.EnsureInPalette(palette);
                Check(palette.Brushes.All(brush => brush == null || brush.HandlerId != "indicator-light"),
                    "Resource maintenance reinstalls the light brush.");
                LegacyPlacement(palette);
                results.Add("PASS: Legacy light data is ignored by validation, footprint, editing and generation; maintenance keeps the brush removed.");

                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                string[] before = Capture(scene);
                Check(before.Length > 0, "Authored scene has no indicator lights.");
                var sources = new HashSet<LevelDefinition>();
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (LevelRegion region in root.GetComponentsInChildren<LevelRegion>(true))
                        if (region.Source != null && region.GetComponentInChildren<IndicatorLight>(true) != null)
                            sources.Add(region.Source);
                foreach (LevelDefinition source in sources)
                {
                    // Two saves detect duplication as well as loss of manually edited light settings.
                    string path = AssetDatabase.GetAssetPath(source.OutputPrefab);
                    LevelPrefabBuilder.Save(source, path);
                    LevelPrefabBuilder.Save(source, path);
                }
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Check(before.SequenceEqual(Capture(scene)), "Regeneration changed scene light transforms, settings or ownership.");
                results.Add($"PASS: {before.Length} scene lights retain their transforms, settings and Custom ownership after two prefab regenerations ({sources.Count} levels).");
            }
            catch (Exception exception) { results.Add("FAIL: " + exception); Debug.LogException(exception); exit = 1; }
            File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath, "../indicator-light-results.txt")), results);
            EditorApplication.Exit(exit);
        }

        private static void LegacyPlacement(LevelPalette palette)
        {
            LevelPrefabBuilder.EnsureFolder(Folder);
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.Size = new Vector2Int(4, 4);
            level.Palette = palette;
            LevelBrush terrain = AssetDatabase.LoadAssetAtPath<LevelBrush>("Assets/Runtime/LevelDesigner/Brushes/Terrain.asset");
            level.Put(Vector2Int.zero, terrain, null);
            var lightBrush = ScriptableObject.CreateInstance<LevelBrush>();
            lightBrush.HandlerId = "indicator-light";
            lightBrush.LayerId = "lights";
            AssetDatabase.CreateAsset(lightBrush, Folder + "/LegacyLightBrush.asset");
            // Invalid old data must not prevent saving or influence room bounds.
            var legacy = new LevelPlacement { Brush = lightBrush, Cell = new Vector2Int(1000, 1000),
                Settings = new LightPlacementSettings { Length = float.NaN } };
            level.Placements.Add(legacy);
            AssetDatabase.CreateAsset(level, Folder + "/Legacy.asset");
            Check(LevelPrefabBuilder.Validate(level).Count == 0, "Legacy light data still fails validation.");
            Check(!LevelFootprint.PlacedCells(level).Contains(legacy.Cell), "Legacy light changes the level footprint.");
            Check(level.Put(Vector2Int.one, lightBrush, null) == null, "Retired brush can still place lights.");
            Check(!LevelGridEditing.Apply(level, new[] { Vector2Int.one }, lightBrush, legacy.Settings, "lights", false, false),
                "Grid editing still paints lights.");
            GameObject prefab = LevelPrefabBuilder.Save(level, Folder + "/Legacy.prefab");
            Check(prefab.GetComponentInChildren<IndicatorLight>(true) == null, "Legacy data generated a light.");
            // A prefab that has not yet been migrated must also survive its first regeneration.
            GameObject contents = PrefabUtility.LoadPrefabContents(Folder + "/Legacy.prefab");
            try
            {
                var layer = new GameObject("Layer lights");
                layer.transform.SetParent(contents.transform.Find("Generated"), false);
                var light = new GameObject("Manual guide").AddComponent<IndicatorLight>();
                light.transform.SetParent(layer.transform, false);
                light.transform.localPosition = new Vector3(1.25f, 2.5f, 0.3f);
                light.LongWidth = 11f;
                PrefabUtility.SaveAsPrefabAsset(contents, Folder + "/Legacy.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            prefab = LevelPrefabBuilder.Save(level, Folder + "/Legacy.prefab");
            var preserved = prefab.GetComponentInChildren<IndicatorLight>(true);
            Check(preserved != null && preserved.transform.IsChildOf(prefab.transform.Find("Custom"))
                && preserved.LongWidth == 11f && preserved.transform.localPosition == new Vector3(1.25f, 2.5f, 0.3f),
                "First regeneration did not preserve the existing manual light.");
        }

        private static string[] Capture(Scene scene)
        {
            var result = new List<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (IndicatorLight light in root.GetComponentsInChildren<IndicatorLight>(true))
                {
                    LevelRegion region = light.GetComponentInParent<LevelRegion>();
                    Check(region != null && light.transform.IsChildOf(region.transform.Find("Custom")),
                        "Light is still under generated content: " + light.name);
                    Check(!light.transform.IsChildOf(region.transform.Find("Generated")), "Generated owns a light.");
                    result.Add(region.WorldModuleId + ":" + light.name + ":" + JsonUtility.ToJson(light)
                        + ":" + light.transform.position.ToString("R") + ":" + light.transform.rotation.ToString("R")
                        + ":" + light.transform.lossyScale.ToString("R") + ":" + light.gameObject.activeSelf);
                }
            result.Sort(StringComparer.Ordinal);
            return result.ToArray();
        }

        private static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
    }
}
