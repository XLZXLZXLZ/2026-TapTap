using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    // This modifies fixtures and the generated scene; use an isolated copy of the project.
    public static class WorldMapSceneSyncRegressionTests
    {
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated project.");
            int exit = 0;
            var results = new List<string>();
            try
            {
                var map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
                string path = WorldMapSceneSync.Synchronize(map);
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                WorldMapRoot world = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.GetComponent<WorldMapRoot>() != null) world = root.GetComponent<WorldMapRoot>();
                }
                Check(world != null && world.GetComponentsInChildren<LevelRegion>().Length == map.OrderedModules().Count,
                    "The saved scene does not contain the complete editable map");
                results.Add("PASS: Full scene contains all " + map.OrderedModules().Count + " regions, player and camera before Play");
                WorldMapModule module = map.OrderedModules()[0];
                LevelRegion region = world.FindRegion(module);
                Vector2Int original = module.Position;
                var custom = new GameObject("Manual Custom preservation probe"); custom.transform.SetParent(region.transform.Find("Custom"), false);
                var unrelated = new GameObject("Manual scene root preservation probe"); SceneManager.MoveGameObjectToScene(unrelated, scene);
                Undo.RecordObject(map, "Move module sync check"); module.Position += new Vector2Int(3, -2);
                EditorUtility.SetDirty(map); Undo.FlushUndoRecordObjects();
                WorldMapSceneSync.Synchronize(map, new HashSet<LevelDefinition>());
                Check(world.FindRegion(module) == region && custom != null && unrelated != null,
                    "Synchronizing destroyed retained instances or manual content");
                Check(Vector2.Distance(region.transform.localPosition, (Vector2)module.Position * region.UnitSize) < 0.001f,
                    "Module move did not reach scene");
                Undo.PerformUndo(); WorldMapSceneSync.Synchronize(map, new HashSet<LevelDefinition>());
                Check(module.Position == original && Vector2.Distance(region.transform.localPosition, (Vector2)original * region.UnitSize) < 0.001f,
                    "Undo did not reach scene");
                results.Add("PASS: Module moves and Undo update the open scene while preserving Custom, unrelated roots and instance identity");
                var added = new WorldMapModule { Level = module.Level, Position = module.Position + new Vector2Int(40, 40), Sequence = map.NextSequence() };
                map.Modules.Add(added); EditorUtility.SetDirty(map);
                WorldMapSceneSync.Synchronize(map, new HashSet<LevelDefinition>());
                Check(world.FindRegion(added) != null, "Added module missing");
                LevelRegion extra = world.FindRegion(added);
                map.Modules.Remove(added); EditorUtility.SetDirty(map);
                WorldMapSceneSync.Synchronize(map, new HashSet<LevelDefinition>());
                Check(extra == null && custom != null, "Removed module survived or retained Custom was lost");
                results.Add("PASS: Adding and removing modules updates scene membership");
                LevelPlacement tile = module.Level.Placements.Find(item => item?.Brush != null && item.Brush.LayerId == "geometry");
                Check(tile != null, "No geometry fixture");
                Vector2Int oldCell = tile.Cell;
                Vector2Int next = oldCell;
                foreach (Vector2Int cell in module.Level.CanvasBounds.allPositionsWithin)
                    if (module.Level.Find(cell, tile.Brush.LayerId) == null) { next = cell; break; }
                Check(next != oldCell, "No free fixture cell");
                tile.Cell = next; EditorUtility.SetDirty(module.Level);
                WorldMapSceneSync.Synchronize(map, new HashSet<LevelDefinition> { module.Level });
                Transform generated = region.transform.Find("Generated");
                Transform moved = null;
                foreach (Transform child in generated.GetComponentsInChildren<Transform>(true))
                    if (child.name.EndsWith(tile.Id, StringComparison.Ordinal)) { moved = child; break; }
                Check(moved != null && Vector2.Distance(moved.localPosition, ((Vector2)next + tile.Brush.Anchor) * region.UnitSize) < 0.001f,
                    "Edited level geometry did not propagate through its linked prefab");
                Check(custom != null, "Geometry update erased scene Custom");
                tile.Cell = oldCell; EditorUtility.SetDirty(module.Level);
                WorldMapSceneSync.Synchronize(map, new HashSet<LevelDefinition> { module.Level });
                SceneManager.SetActiveScene(scene);
                WorldMapSceneSync.Synchronize(map, new HashSet<LevelDefinition>());
                Check(scene.isLoaded && custom != null && unrelated != null && SceneManager.GetActiveScene() == scene,
                    "Updating selection unloaded an open gameplay scene or manual work");
                results.Add("PASS: Repeated synchronization preserves the open gameplay scene and its active editor context");
                UnityEngine.Object.DestroyImmediate(custom); UnityEngine.Object.DestroyImmediate(unrelated);
                results.Add("PASS: Single-level geometry updates its scene instance without erasing Custom");
                AssetDatabase.SaveAssetIfDirty(map);
                Check(EditorSceneManager.SaveScene(scene, path), "Cannot save complete scene");
                EditorSceneManager.CloseScene(scene, true);
                WorldMapSceneSync.Synchronize(map, new HashSet<LevelDefinition>());
                results.Add("PASS: Closed-scene synchronization saves and closes the projection without changing the active editor scene");
                File.WriteAllText(Path.Combine(Application.dataPath, "../world-scene-generated.txt"), path);
            }
            catch (Exception exception) { exit = 1; results.Add("FAIL: " + exception); Debug.LogException(exception); }
            File.WriteAllLines(Path.Combine(Application.dataPath, "../world-scene-sync-results.txt"), results);
            EditorApplication.Exit(exit);
        }

        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

        public static void RunManualAndTimeBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated project.");
            var results = new List<string>();
            int exit = 0;
            float originalScale = Time.timeScale;
            GameObject clockObject = null, actor = null, effectsObject = null;
            try
            {
                var map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
                WorldMapModule module = map.OrderedModules()[0];
                module.Position += Vector2Int.right;
                EditorUtility.SetDirty(map);
                string path = WorldMapSceneSync.SaveAndSynchronize(map);
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                WorldMapRoot world = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (root.GetComponent<WorldMapRoot>() != null) world = root.GetComponent<WorldMapRoot>();
                Check(world != null && !scene.isDirty && !EditorUtility.IsDirty(map)
                    && Vector2.Distance(world.FindRegion(module).transform.localPosition,
                        (Vector2)module.Position * module.Level.Palette.UnitSize) < 0.001f,
                    "Manual save did not persist the layout and scene projection");
                Check(!Attribute.IsDefined(typeof(WorldMapSceneSync), typeof(InitializeOnLoadAttribute)),
                    "Automatic scene sync still initializes on editor reload");
                results.Add("PASS: Explicit save persists map and scene together; automatic sync initializer removed");
                clockObject = new GameObject("Debug time check");
                GameDebugTime clock = clockObject.AddComponent<GameDebugTime>();
                foreach (float expected in new[] { 2f, 4f, 8f, 1f })
                { clock.CycleSpeed(); Check(clock.CurrentSpeed == expected, "Incorrect speed cycle"); }
                actor = new GameObject("Debug input actor");
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(GameDebugTime).GetField("player", flags).SetValue(clock, actor.AddComponent<PlayerController>());
                clock.CycleSpeed(); clock.CycleSpeed(); // 4x
                var tick = typeof(GameDebugTime).GetMethod("LateUpdate", flags);
                Time.timeScale = 0f; tick.Invoke(clock, null);
                Check(Time.timeScale == 0f, "Debug clock unpaused gameplay");
                Time.timeScale = 1f; tick.Invoke(clock, null);
                Check(Time.timeScale == 4f, "Debug clock did not apply its base speed");
                effectsObject = new GameObject("Debug slowdown check");
                EffectManager effects = effectsObject.AddComponent<EffectManager>();
                effects.Play(PlayerEffect.Joined, null);
                Check(Time.timeScale == 3f, "Join slowdown is not relative to debug speed");
                effects.SetGameplayTimeScale(8f);
                Check(Time.timeScale == 6f, "Changing speed during slowdown lost its multiplier");
                effects.Simulate(0.1f);
                Check(Time.timeScale == 8f, "Slowdown restored an outdated debug speed");
                results.Add("PASS: 1/2/4/8 clock cycles, preserves pause, and composes with join slowdown and resume speed");
            }
            catch (Exception exception) { results.Add("FAIL: " + exception); Debug.LogException(exception); exit = 1; }
            finally
            {
                if (effectsObject != null) UnityEngine.Object.DestroyImmediate(effectsObject);
                if (actor != null) UnityEngine.Object.DestroyImmediate(actor);
                if (clockObject != null) UnityEngine.Object.DestroyImmediate(clockObject);
                Time.timeScale = originalScale;
            }
            File.WriteAllLines(Path.Combine(Application.dataPath, "../manual-sync-debug-time-results.txt"), results);
            EditorApplication.Exit(exit);
        }
    }
}
