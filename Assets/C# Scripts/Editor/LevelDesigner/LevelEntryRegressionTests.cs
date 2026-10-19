using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Run in an isolated project: -batchmode -executeMethod TapTap.Editor.LevelEntryRegressionTests.RunBatch
    [InitializeOnLoad]
    public static class LevelEntryRegressionTests
    {
        private const string Key = "TapTap.LevelEntryRegression";
        private static readonly List<string> results = new List<string>();
        private static readonly string[] ScenePaths =
        {
            "Assets/Scenes/WorldMaps/World_Character_Play.unity",
            "Assets/Scenes/WorldMaps/World_Character_Play_Backup.unity",
            "Assets/Scenes/WorldMaps/World_Character_Play_BackupFqh.unity",
            "Assets/Scenes/WorldMaps/World_Character_Play_BackupHYY.unity",
            "Assets/Scenes/WorldMaps/World_Character_Play_CLX.unity",
            "Assets/Scenes/ReturnJourneyPreview.unity"
        };
        private static string ResultPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../level-entry-results.txt"));
        private static double deadline;
        private static int nextIndex;
        private static float spawnTime;
        private static bool activated;

        static LevelEntryRegressionTests()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
                deadline = EditorApplication.timeSinceStartup + 120d;
                EditorApplication.update += Tick;
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project.");
            try
            {
                results.Clear();
                Recovery();
                foreach (string path in ScenePaths)
                {
                    Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    int checkedEntries = 0;
                    foreach (GameObject root in scene.GetRootGameObjects())
                        foreach (LevelRegion region in root.GetComponentsInChildren<LevelRegion>(true))
                        {
                            if (region.Source?.EntryPlacement == null) continue;
                            // Inspect the saved binding before exercising runtime recovery.
                            Check(region.EntryCheckpoint != null, path + ": missing entry in " + region.name);
                            Check(region.EntryCheckpoint.GetComponentInParent<LevelRegion>() == region,
                                path + ": entry belongs to another region in " + region.name);
                            Check(region.ContainsWorldPoint(region.EntrySpawnPosition),
                                path + ": spawn outside own footprint in " + region.name);
                            checkedEntries++;
                        }
                    Check(checkedEntries >= 20, path + ": expected at least 20 ordinary level entries");
                    Pass("Saved entries belong to their regions and spawn inside them: " + path + " (" + checkedEntries + ")");
                }
                Scene play = EditorSceneManager.OpenScene(ScenePaths[0], OpenSceneMode.Single);
                WorldMapProgression flow = Object.FindObjectOfType<WorldMapProgression>();
                flow.InitialLevelNumber = 6;
                EditorSceneManager.MarkSceneDirty(play);
                EditorSceneManager.SaveScene(play);
                File.WriteAllLines(ResultPath, results);
                SessionState.SetBool(Key, true);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static void Recovery()
        {
            var layout = ScriptableObject.CreateInstance<LevelDefinition>();
            var brush = ScriptableObject.CreateInstance<LevelBrush>();
            var root = new GameObject("Entry recovery");
            var foreign = new GameObject("Another region");
            try
            {
                brush.HandlerId = "checkpoint";
                brush.Anchor = new Vector2(0.5f, 0f);
                layout.Size = new Vector2Int(32, 16);
                LevelPlacement wrong = layout.Put(new Vector2Int(1, 1), brush, new CheckpointPlacementSettings());
                LevelPlacement entry = layout.Put(new Vector2Int(22, 2), brush, new CheckpointPlacementSettings());
                layout.EntryPlacementId = entry.Id;
                LevelRegion region = root.AddComponent<LevelRegion>();
                region.Source = layout;
                region.transform.position = new Vector3(54, 84, 0);
                CheckpointFlag first = Marker(region, wrong);
                CheckpointFlag intended = Marker(region, entry);
                intended.gameObject.SetActive(false);
                Check(region.EntryCheckpoint == null, "Fixture entry was already bound");
                Near(region.EntrySpawnPosition, new Vector2(76.5f, 86.45f), "Missing binding used the region origin");
                Check(region.EntryCheckpoint == intended && region.EntryCheckpoint != first,
                    "Recovery picked the first checkpoint instead of the authored ID");
                foreign.AddComponent<LevelRegion>();
                region.EntryCheckpoint = foreign.AddComponent<CheckpointFlag>();
                Check(region.ResolveEntryCheckpoint() == intended, "Foreign region entry was accepted");
                intended.transform.localPosition += Vector3.right;
                Near(region.EntrySpawnPosition, new Vector2(77.5f, 86.45f), "Recovery discarded a scene entry position override");
                Object.DestroyImmediate(intended.gameObject);
                region.UnitSize = 2f;
                Near(region.EntrySpawnPosition, new Vector2(99f, 88.9f), "Destroyed marker did not use the scaled authored spawn");
                region.Source = null;
                Near(region.EntrySpawnPosition, region.transform.position, "Unauthored region fallback changed");
                Pass("Missing, destroyed and foreign bindings recover the authored entry; multiple/inactive checkpoints, scene overrides and unit scale remain correct");
            }
            finally
            {
                Object.DestroyImmediate(root); Object.DestroyImmediate(foreign);
                Object.DestroyImmediate(layout); Object.DestroyImmediate(brush);
            }
        }

        private static CheckpointFlag Marker(LevelRegion region, LevelPlacement placement)
        {
            var owner = new GameObject("Checkpoint " + placement.Id);
            owner.transform.SetParent(region.transform, false);
            owner.transform.localPosition = (Vector2)placement.Cell + placement.Brush.Anchor;
            return owner.AddComponent<CheckpointFlag>();
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                Check(EditorApplication.timeSinceStartup < deadline, "Play validation timed out");
                WorldMapProgression flow = WorldMapProgression.Current;
                if (flow?.CurrentModule == null) return;
                if (!activated)
                {
                    Check(flow.CurrentNumber == 6, "Entering level 6 immediately selected another level: " + flow.CurrentNumber);
                    nextIndex = flow.CurrentIndex;
                    spawnTime = Time.fixedTime;
                    activated = true;
                }
                PlayerController player = Object.FindObjectOfType<PlayerController>();
                player.GetComponent<PlayerInput>().UseKeyboard = false;
                flow.UseKeyboard = false;
                flow.SetExitsEnabled(false);
                Check(flow.CurrentIndex == nextIndex, "Spawn drift switched level " + (nextIndex + 1) + " to " + flow.CurrentNumber);
                if (Time.fixedTime - spawnTime < 0.6f) return;
                Pass("Play: level " + flow.CurrentNumber + " remains in its region for 0.6 simulation seconds after spawning");
                // Check level 6 first, then every other ordinary entry in sequence.
                nextIndex = nextIndex == 5 ? 0 : nextIndex + 1;
                if (nextIndex == 5) nextIndex++;
                while (nextIndex < flow.LevelCount && flow.Modules[nextIndex].Level.EntryPlacement == null) nextIndex++;
                if (nextIndex >= flow.LevelCount) { Finish(null); return; }
                typeof(WorldMapProgression).GetMethod("Activate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(flow, new object[] { nextIndex, false });
                spawnTime = Time.fixedTime;
                Near(player.Body.Motor.Position, flow.CurrentRegion.EntrySpawnPosition, "Activation did not use the resolved entry");
                Check(flow.CurrentRegion.ContainsWorldPoint(player.Body.Motor.Position + player.Body.Motor.CenterOffset),
                    "Activated outside level " + flow.CurrentNumber);
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Near(Vector2 actual, Vector2 expected, string message)
            => Check(Vector2.Distance(actual, expected) < 0.001f, message + ": " + actual + " / " + expected);
        private static void Pass(string message) { results.Add("PASS: " + message); Debug.Log("LEVEL ENTRY PASS: " + message); }
        private static void Finish(Exception exception)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(Key, false);
            var all = File.Exists(ResultPath) ? new List<string>(File.ReadAllLines(ResultPath)) : new List<string>();
            foreach (string result in results) if (!all.Contains(result)) all.Add(result);
            if (exception != null) { all.Add("FAIL: " + exception); Debug.LogException(exception); }
            File.WriteAllLines(ResultPath, all);
            EditorApplication.Exit(exception == null ? 0 : 1);
        }
    }
}
