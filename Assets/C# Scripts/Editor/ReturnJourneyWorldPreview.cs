using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    [InitializeOnLoad]
    public static class ReturnJourneyWorldPreview
    {
        private const string SourcePath = "Assets/Scenes/WorldMaps/World_Character_Play.unity";
        private const string Pending = "TapTap.ReturnJourneyWorldSpawn";
        private static double deadline;

        static ReturnJourneyWorldPreview()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
                deadline = EditorApplication.timeSinceStartup + 60;
                EditorApplication.update += VerifySpawn;
            };
        }

        public static void ValidateBatch()
        {
            ConfigureBatch();
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        private static void VerifySpawn()
        {
            try
            {
                if (!EditorApplication.isPlaying || Time.frameCount < 3) return;
                WorldMapProgression flow = WorldMapProgression.Current;
                if (flow == null || flow.CurrentModule == null)
                {
                    if (EditorApplication.timeSinceStartup < deadline) return;
                    throw new TimeoutException("World preview initialization timed out.");
                }
                PlayerController player = UnityEngine.Object.FindObjectOfType<PlayerController>();
                if (flow.CurrentNumber != 20) throw new InvalidOperationException("Spawned in level " + flow.CurrentNumber);
                Vector2 entry = flow.CurrentRegion.EntrySpawnPosition;
                if (Vector2.Distance(player.Body.Motor.Position, entry) > 0.5f)
                    throw new InvalidOperationException("Body did not spawn at the level 20 entry.");
                if (Vector2.Distance(player.GetComponent<RespawnService>().Checkpoint, entry) > 0.01f)
                    throw new InvalidOperationException("Respawn point is not the level 20 entry.");
                DemoCamera camera = UnityEngine.Object.FindObjectOfType<DemoCamera>();
                if (camera.Region != flow.CurrentRegion) throw new InvalidOperationException("Camera did not select level 20.");
                File.AppendAllText("Logs/ReturnJourney/world-preview-config.txt", "PASS: Play Mode starts in level 20 with its entry spawn, respawn point and fixed camera.\n");
                Finish(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                File.AppendAllText("Logs/ReturnJourney/world-preview-config.txt", "FAIL: " + error + "\n");
                Finish(1);
            }
        }

        private static void Finish(int code)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= VerifySpawn;
            EditorApplication.Exit(code);
        }

        // Only the independent scene is opened/saved; the production scene is read as bytes.
        public static void ConfigureBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            byte[] before = File.ReadAllBytes(SourcePath);
            Scene scene = EditorSceneManager.OpenScene(ReturnJourneyAssets.WorldPreviewPath, OpenSceneMode.Single);
            WorldMapProgression flow = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                flow = root.GetComponentInChildren<WorldMapProgression>(true);
                if (flow != null) break;
            }
            if (flow == null) throw new InvalidOperationException("Independent preview does not contain the complete world map.");
            WorldMapRoot world = flow.GetComponent<WorldMapRoot>();
            WorldMapModule module = world.Source.OrderedModules().Find(item => world.Source.SequenceNumber(item) == 20);
            if (module == null) throw new InvalidOperationException("Level 20 is missing.");
            LevelRegion region = world.FindRegion(module);
            if (region == null || region.EntryCheckpoint == null) throw new InvalidOperationException("Level 20 entry is missing.");
            flow.InitialLevelNumber = 20;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ReturnJourneyAssets.WorldPreviewPath);
            if (Convert.ToBase64String(before) != Convert.ToBase64String(File.ReadAllBytes(SourcePath)))
                throw new InvalidOperationException("Protected production scene was modified.");
            File.WriteAllText("Logs/ReturnJourney/world-preview-config.txt", "PASS: Complete-world preview starts at level 20. Production scene unchanged.\n");
        }
    }
}
