using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TapTap.Editor
{
    public static class WorldMapReferenceRegressionTests
    {
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated project.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var results = new List<string>();
            int exit = 0;
            var map = ScriptableObject.CreateInstance<WorldMapDefinition>();
            var a = ScriptableObject.CreateInstance<LevelDefinition>();
            var b = ScriptableObject.CreateInstance<LevelDefinition>();
            GameObject sceneWorld = null, square = null;
            Texture2D export = null;
            try
            {
                a.name = "A"; b.name = "B";
                a.Size = b.Size = Vector2Int.one;
                a.Palette = b.Palette = AssetDatabase.LoadAssetAtPath<LevelPalette>(LevelEditorAssets.DefaultPalettePath);
                var module = new WorldMapModule { Level = a, Position = new Vector2Int(-100, 700) };
                map.Modules.Add(module);
                foreach (bool visible in new[] { true, false })
                {
                    map.ShowTemplate = visible;
                    Check(WorldMapPrefabBuilder.Validate(map).Count == 0, "Reference template still blocks outside/negative placement");
                    Check(map.PreviewBounds.Contains(module.Position), "Outside module is clipped from fit/export bounds");
                }
                export = WorldMapDrawing.Export(map, null);
                Check(export.width == 15 && export.height == 15, "Disabled template still controls export extent");
                results.Add("PASS: Template toggle is independent of placement; negative/outside modules remain in fit and export bounds");
                LevelBrush spikes = AssetDatabase.LoadAssetAtPath<LevelBrush>("Assets/Runtime/LevelDesigner/Brushes/Spikes.asset");
                a.Put(Vector2Int.zero, spikes, new SpikePlacementSettings());
                b.Put(Vector2Int.zero, spikes, new SpikePlacementSettings());
                Check(!WorldMapOverlap.HasConflict(a, a.Footprint, Vector2Int.zero, b, b.Footprint, Vector2Int.zero), "Matching tiles conflict");
                ((SpikePlacementSettings)b.Placements[0].Settings).QuarterTurns = 1; EditorUtility.SetDirty(b);
                var cells = WorldMapOverlap.ConflictCells(a, a.Footprint, new Vector2Int(5, 7), b, b.Footprint, new Vector2Int(5, 7));
                Check(cells.Count == 1 && cells[0] == new Vector2Int(5, 7), "Different orientations are not reported at the exact cell");
                ((SpikePlacementSettings)b.Placements[0].Settings).QuarterTurns = 0; EditorUtility.SetDirty(b);
                sceneWorld = new GameObject("Scene conflict fixture");
                WorldMapRoot world = sceneWorld.AddComponent<WorldMapRoot>(); world.Source = map;
                Transform generated = Child("Generated", world.transform);
                for (int i = 0; i < 2; i++)
                {
                    Transform regionRoot = Child("Region " + i, generated);
                    LevelRegion region = regionRoot.gameObject.AddComponent<LevelRegion>(); region.Source = i == 0 ? a : b;
                    Transform geometry = Child("Geometry", Child("Generated", regionRoot));
                    var tile = (GameObject)PrefabUtility.InstantiatePrefab(spikes.Prefab);
                    tile.transform.SetParent(geometry, false); tile.transform.localPosition = new Vector3(0.5f, 0.5f);
                    if (i == 1) tile.transform.localRotation = Quaternion.Euler(0, 0, 90);
                }
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
                typeof(WorldMapSceneConflicts).GetMethod("Refresh", flags).Invoke(null, null);
                var conflicts = (System.Collections.ICollection)typeof(WorldMapSceneConflicts).GetField("conflicts", flags).GetValue(null);
                Check(conflicts.Count == 1, "Actual scene overrides are not checked independently of source layout");
                results.Add("PASS: Overlap checks identify setting differences and actual scene rotations, with exact conflict cells");
                square = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Terrain/Grid/GridSquarePlatform.prefab"));
                SpriteRenderer fill = null;
                foreach (SpriteRenderer renderer in square.GetComponentsInChildren<SpriteRenderer>())
                    if (renderer.name == "Surface") fill = renderer;
                Check(fill != null && Mathf.Abs(fill.color.r - 0.30f) < 0.001f && Mathf.Abs(fill.color.g - 0.38f) < 0.001f
                    && Mathf.Abs(fill.color.b - 0.43f) < 0.001f, "Square platform color is not terrain-like and distinct");
                Check(square.GetComponent<WorldSurface>().Kind == SurfaceKind.OneWay
                    && square.GetComponent<BoxCollider2D>().size == Vector2.one, "Color edit changed platform geometry or rules");
                results.Add("PASS: Square platform uses a distinct terrain-like color and retains full-square one-way collision");
            }
            catch (Exception exception) { results.Add("FAIL: " + exception); Debug.LogException(exception); exit = 1; }
            finally
            {
                if (sceneWorld != null) UnityEngine.Object.DestroyImmediate(sceneWorld);
                if (square != null) UnityEngine.Object.DestroyImmediate(square);
                if (export != null) UnityEngine.Object.DestroyImmediate(export);
                UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(map);
            }
            File.WriteAllLines(Path.Combine(Application.dataPath, "../world-reference-results.txt"), results);
            EditorApplication.Exit(exit);
        }
        private static Transform Child(string name, Transform parent)
        { var child = new GameObject(name); child.transform.SetParent(parent, false); return child.transform; }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    }
}
