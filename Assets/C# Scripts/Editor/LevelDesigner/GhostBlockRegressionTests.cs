using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    public static class GhostBlockRegressionTests
    {
        private static string folder;

        public static void RunBatch()
        {
            if (!Application.isBatchMode || Directory.Exists(Path.Combine(Application.dataPath, "../.git")))
                throw new InvalidOperationException("Run in an isolated batch project without a .git directory.");
            var results = new List<string>();
            int exit = 0;
            try
            {
                folder = "Assets/GhostBlockRegression_" + Guid.NewGuid().ToString("N");
                LevelPrefabBuilder.EnsureFolder(folder);
                var palette = AssetDatabase.LoadAssetAtPath<LevelPalette>(LevelEditorAssets.DefaultPalettePath);
                LevelBrush ghost = palette.Brushes.Find(brush => brush != null && brush.HandlerId == GhostBlockLevelBrushHandler.HandlerId);
                Check(ghost != null && ghost.Prefab == null, "Default palette is missing the data-only ghost brush.");
                Check(!LevelBrushHandlers.Get(ghost.HandlerId).GeneratesSceneObject, "Ghost brush creates scene objects.");
                var customPalette = ScriptableObject.CreateInstance<LevelPalette>();
                AssetDatabase.CreateAsset(customPalette, folder + "/Palette.asset");
                LevelEditorAssets.EnsureGhostBlockInPalette(customPalette);
                LevelEditorAssets.EnsureGhostBlockInPalette(customPalette);
                Check(customPalette.Brushes.Count == 1 && customPalette.Brushes[0] == ghost, "Palette upgrade duplicates the ghost brush.");
                results.Add("PASS: Default and upgraded palettes expose one ghost brush without a prefab.");

                var level = ScriptableObject.CreateInstance<LevelDefinition>();
                level.Size = new Vector2Int(8, 8);
                level.Palette = palette;
                LevelBrush terrain = AssetDatabase.LoadAssetAtPath<LevelBrush>("Assets/Runtime/LevelDesigner/Brushes/Terrain.asset");
                level.Put(Vector2Int.zero, terrain, null);
                AssetDatabase.CreateAsset(level, folder + "/Layout.asset");
                GameObject baseline = LevelPrefabBuilder.Save(level, folder + "/Level.prefab");
                int transforms = baseline.GetComponentsInChildren<Transform>(true).Length;
                int renderers = baseline.GetComponentsInChildren<Renderer>(true).Length;
                int colliders = baseline.GetComponentsInChildren<Collider2D>(true).Length;

                Vector2Int min = new Vector2Int(1, 1), max = new Vector2Int(5, 5);
                Undo.IncrementCurrentGroup();
                Check(LevelGridEditing.Apply(level, LevelGridEditing.Rectangle(min, max, true), ghost,
                    new EmptyPlacementSettings(), ghost.LayerId, false, false,
                    () => Undo.RegisterCompleteObjectUndo(level, "Paint ghost boundary")), "Cannot paint the ghost rectangle.");
                Undo.FlushUndoRecordObjects();
                Check(level.Footprint.Contains(new Vector2Int(3, 3)) && level.Footprint.Contains(max),
                    "Ghost boundary does not enclose the intended mask area.");
                Undo.PerformUndo();
                Check(level.Placements.Count == 1, "Undo did not remove ghost cells.");
                Undo.PerformRedo();
                Check(level.Placements.Count == 17, "Redo did not restore the ghost border.");
                level.InvalidateFootprint();

                // Ghost occupancy has its own layer and can coexist with actual terrain.
                level.Put(Vector2Int.zero, ghost, null);
                Check(level.Find(Vector2Int.zero, terrain.LayerId) != null, "Ghost painting replaces real terrain.");
                Check(level.Erase(Vector2Int.zero, ghost.LayerId), "Cannot erase ghost occupancy.");
                Check(level.Find(Vector2Int.zero, terrain.LayerId) != null, "Ghost erasing removes real terrain.");
                Vector2Int gap = new Vector2Int(3, 1);
                Check(level.Erase(gap, ghost.LayerId), "Cannot open the ghost border.");
                Check(!level.Footprint.Contains(new Vector2Int(3, 3)), "Erasing a ghost does not update the mask interior.");
                level.Put(gap, ghost, null);
                Check(level.Footprint.Contains(new Vector2Int(3, 3)), "Closing the ghost border does not restore its interior.");
                results.Add("PASS: Ghost rectangle painting, Undo/Redo, erasing, and enclosure update the footprint without replacing terrain.");

                level.UseCustomBoundary = true;
                level.BoundaryCells = new List<Vector2Int> { Vector2Int.zero };
                level.InvalidateFootprint();
                Check(level.Footprint.Contains(max), "Custom boundaries discard ghost occupancy.");
                level.UseCustomBoundary = false;
                level.InvalidateFootprint();
                Check(LevelPrefabBuilder.Validate(level).Count == 0, "Ghost placements fail prefab validation.");
                GameObject prefab = LevelPrefabBuilder.Save(level, folder + "/Level.prefab");
                Check(prefab.GetComponentsInChildren<Transform>(true).Length == transforms
                    && prefab.GetComponentsInChildren<Renderer>(true).Length == renderers
                    && prefab.GetComponentsInChildren<Collider2D>(true).Length == colliders,
                    "Ghost cells generate extra objects, renderers or colliders.");
                Check(level.Outline.Bounds.Contains(max), "Baked level outline excludes ghost cells.");
                AssetDatabase.SaveAssetIfDirty(level);
                AssetDatabase.ImportAsset(folder + "/Layout.asset", ImportAssetOptions.ForceSynchronousImport);
                level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(folder + "/Layout.asset");
                Check(level.Placements.Count(placement => placement.Brush == ghost) == 16, "Ghost placements were not saved.");

                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    LevelRegion region = instance.GetComponent<LevelRegion>();
                    float unit = region.UnitSize;
                    Check(region.ContainsWorldPoint(new Vector2(5.5f, 5.5f) * unit), "Runtime region excludes a ghost cell.");
                    MethodInfo build = typeof(WorldExplorationMask).GetMethod("BuildInterior", BindingFlags.NonPublic | BindingFlags.Static);
                    Mesh mask = (Mesh)build.Invoke(null, new object[] { region });
                    try
                    {
                        Check(mask.bounds.Contains(new Vector3(5.5f * unit, 5.5f * unit, 0f))
                            && mask.GetIndexCount(0) == region.Source.Footprint.Runs.Count * 6,
                            "Runtime exploration mask does not include the ghost footprint.");
                    }
                    finally { Object.DestroyImmediate(mask); }
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
                results.Add("PASS: Saved ghost occupancy reaches the baked outline and runtime exploration mask with zero extra scene objects or collisions.");

                var broken = ScriptableObject.CreateInstance<LevelBrush>();
                broken.LayerId = "broken";
                level.Put(Vector2Int.one, broken, null);
                Check(LevelPrefabBuilder.Validate(level).Any(error => error.Contains("Prefab")), "Ordinary brushes no longer require a prefab.");
                level.Erase(Vector2Int.one, broken.LayerId);
                Object.DestroyImmediate(broken);
                results.Add("PASS: Ordinary object brushes still require valid prefab assets.");
            }
            catch (Exception exception) { results.Add("FAIL: " + exception); Debug.LogException(exception); exit = 1; }
            File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath, "../ghost-block-results.txt")), results);
            EditorApplication.Exit(exit);
        }

        private static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
    }
}
