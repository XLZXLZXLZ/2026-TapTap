using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    [InitializeOnLoad]
    public static class WorldMenuRegressionTests
    {
        private const string Key = "TapTap.WorldMenuRegression";
        private const string Fixture = "Assets/WorldFlowRegression/Flow.asset";
        private static int stage, sceneHandle;
        private static double deadline;
        private static WorldMapDefinition map;
        static WorldMenuRegressionTests()
        {
            if (!SessionState.GetBool(Key, false)) return;
            deadline = EditorApplication.timeSinceStartup + 100d;
            EditorApplication.update += Tick;
        }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
            try
            {
                map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(Fixture);
                Check(map != null, "Run WorldFlowRegressionTests first.");
                string path = WorldMapMenuSceneBuilder.Generate(map, false);
                map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(Fixture);
                Check(WorldMapMenuSceneBuilder.Generate(map, false) == path, "Selection regeneration replaced the scene.");
                map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(Fixture);
                Check(EditorBuildSettings.scenes[0].path == path && EditorBuildSettings.scenes[0].enabled,
                    "Initial scene is not the first enabled build scene.");
                WorldMapProgressStore.Clear(map);
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                File.WriteAllText(ResultPath, "PASS: Stable initial scene, linked play scene, initial build entry\n");
                SessionState.SetBool(Key, true);
                deadline = EditorApplication.timeSinceStartup + 100d;
                EditorApplication.update -= Tick; EditorApplication.update += Tick;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception) { Fail(exception); }
        }
        public static void RunBootstrapBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
            map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(Fixture);
            map.PlaySceneGuid = ""; map.PlayScenePath = "";
            EditorUtility.SetDirty(map); AssetDatabase.SaveAssetIfDirty(map);
            RunBatch();
        }
        private static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
            try
            {
                Check(EditorApplication.timeSinceStartup < deadline, "Menu regression timed out at stage " + stage);
                if (WorldMapSceneRouter.IsLoading) return;
                if (map == null) map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(Fixture);
                var modules = map.OrderedModules();
                WorldLevelMenu menu = WorldLevelMenu.Current;
                WorldMapProgression flow = WorldMapProgression.Current;
                if (flow != null && flow.IsTransitioning) return;
                switch (stage)
                {
                    case 0:
                        if (menu == null || menu.View == null) return;
                        Check(WorldMapProgression.InputBlocked && !SceneResetManager.Instance.ResetCurrentScene(), "Menu input guard failed.");
                        Check(WorldMapProgressStore.IsUnlocked(map, modules[0]) && !WorldMapProgressStore.IsUnlocked(map, modules[1]),
                            "Fresh save should only unlock the first level.");
                        foreach (WorldMapModule module in modules)
                            Check(menu.View.TryPick(menu.View.LabelScreenPosition(module), out WorldMapModule picked) && picked == module,
                                "Puzzle picking failed for " + map.SequenceNumber(module));
                        Capture(menu.View, "world-menu-locked.png");
                        menu.Select(modules[1]); Check(!menu.EnterSelected(), "Locked level can be entered.");
                        menu.UnlockAll();
                        Check(modules.TrueForAll(module => WorldMapProgressStore.IsUnlocked(map, module))
                            && WorldMapProgressStore.Completed(map).Count == 0, "P unlock changed completion.");
                        Capture(menu.View, "world-menu-unlocked.png");
                        Check(menu.EnterSelected() && !menu.EnterSelected(), "Cannot start selected level or double load guard failed.");
                        Pass("Footprint picking, locked access, P unlock keeps completion empty, keyboard/menu input guards");
                        stage = 1; break;
                    case 1:
                        if (flow == null || flow.CurrentNumber != 20) return;
                        Check(!WorldLevelMenu.IsOpen && flow.CompletedCount == 0 && UnityEngine.Object.FindObjectsOfType<LevelRegion>().Length == 1,
                            "Selected launch or room visibility failed.");
                        sceneHandle = SceneManager.GetActiveScene().handle;
                        flow.FadeDuration = 0.05f; flow.AssemblyDuration = 0.1f; flow.AssemblyHold = 0.05f;
                        var endpoint = flow.CurrentRegion.GetComponentInChildren<LevelEndpoint>();
                        UnityEngine.Object.FindObjectOfType<PlayerController>().TeleportJoined((Vector2)endpoint.transform.position + Vector2.up * 0.5f);
                        Physics2D.SyncTransforms();
                        stage = 2; break;
                    case 2:
                        if (flow == null || flow.CurrentNumber != 30) return;
                        Check(flow.CompletedCount == 1 && SceneManager.GetActiveScene().handle == sceneHandle,
                            "Completion changed scene or progress was not recorded.");
                        Check(WorldMapProgressStore.Completed(map).Contains(modules[1].Id), "Completion was not saved.");
                        Check(flow.ReturnToSelection(), "Cannot return to selection.");
                        stage = 3; break;
                    case 3:
                        if (menu == null || menu.View == null) return;
                        Check(WorldMapProgressStore.Completed(map).Count == 1
                            && modules.TrueForAll(module => WorldMapProgressStore.IsUnlocked(map, module)), "Returning lost progress/unlock.");
                        menu.Select(modules[1]); Check(menu.EnterSelected(), "Completed puzzle cannot be replayed.");
                        stage = 4; break;
                    case 4:
                        if (flow == null || flow.CurrentNumber != 20) return;
                        Check(flow.CompletedCount == 1, "Replay did not load the saved completion.");
                        Pass("Selected level launch, same-scene next level, saved completion, return to menu, completed level replay");
                        WorldMapProgressStore.Clear(map);
                        SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
                        EditorApplication.Exit(0); break;
                }
            }
            catch (Exception exception) { Fail(exception); }
        }
        private static void Capture(WorldMapAssemblyView view, string name)
        {
            var texture = (RenderTexture)typeof(WorldMapAssemblyView).GetField("texture",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(view);
            RenderTexture previous = RenderTexture.active; RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Application.dataPath, "../" + name), image.EncodeToPNG());
            UnityEngine.Object.Destroy(image); RenderTexture.active = previous;
        }
        private static string ResultPath => Path.Combine(Application.dataPath, "../world-menu-regression-results.txt");
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Pass(string message) { File.AppendAllText(ResultPath, "PASS: " + message + "\n"); Debug.Log(message); }
        private static void Fail(Exception exception)
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
            File.AppendAllText(ResultPath, "FAIL: " + exception + "\n"); Debug.LogException(exception); EditorApplication.Exit(1);
        }
    }
}
