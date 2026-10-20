using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Run in an isolated batch project: exercises real scene loads and pickup initialization.
    [InitializeOnLoad]
    public static class FreshRunRegressionTests
    {
        private const string Pending = "TapTap.FreshRunRegression";
        private const string Folder = "Assets/FreshRunValidation";
        private const string PlayPath = Folder + "/Play.unity";
        private const string SelectPath = Folder + "/Selection.unity";
        private const string WorldPath = Folder + "/World.asset";
        private static readonly GameStage[] Stages = { GameStage.CarryingKey, GameStage.DoorOpened, GameStage.Returning };
        private static string Results => Path.GetFullPath("Logs/FreshRun/results.txt");
        private static int step, cycle, frame;
        private static double deadline;
        private static RewindManager previousRewind;

        static FreshRunRegressionTests()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
                step = cycle = 0;
                frame = -1;
                deadline = EditorApplication.timeSinceStartup + 120;
                EditorApplication.update += Tick;
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Results));
                File.WriteAllText(Results, "");
                LevelPrefabBuilder.EnsureFolder(Folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), SelectPath);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var floor = new GameObject("Floor");
                floor.transform.position = Vector3.down * 0.5f;
                floor.AddComponent<BoxCollider2D>().size = new Vector2(100, 1);
                floor.AddComponent<WorldSurface>();
                var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Player.prefab"));
                player.GetComponent<PlayerInput>().UseKeyboard = false;
                player.transform.position = Vector3.up;
                var key = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Interactables/ProgressKey.prefab"));
                key.transform.position = new Vector3(10, 1, 0);
                Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ReturnJourneyAssets.DoorPath))
                    .transform.position = new Vector3(-20, 1, 0);
                Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ReturnJourneyAssets.PausePath));
                new GameObject("Return Presentation").AddComponent<ReturnJourneyPresentation>();
                new GameObject("Guide Light").AddComponent<IndicatorLight>();
                new GameObject("World Phase").AddComponent<WorldPhaseState>();
                var platform = new GameObject("Return Platform").AddComponent<ReturnJourneyPlatform>();
                platform.View = new GameObject("Platform Visual").transform;
                platform.View.SetParent(platform.transform, false);
                platform.FallenLocalPosition = Vector3.down * 2f;
                var endpoint = new GameObject("Endpoint").AddComponent<LevelEndpoint>();
                endpoint.transform.position = new Vector3(30, 1, 0);
                var portalLight = new GameObject("Portal Light").AddComponent<Light>();
                portalLight.transform.SetParent(endpoint.transform, false);
                portalLight.color = Color.green;
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), PlayPath);
                var world = ScriptableObject.CreateInstance<WorldMapDefinition>();
                world.ProgressId = Guid.NewGuid().ToString("N");
                world.PlayScenePath = PlayPath;
                world.SelectionScenePath = SelectPath;
                var level = ScriptableObject.CreateInstance<LevelDefinition>();
                AssetDatabase.CreateAsset(level, Folder + "/Level.asset");
                for (int i = 0; i < 3; i++) world.Modules.Add(new WorldMapModule { Level = level, Sequence = i + 1 });
                AssetDatabase.CreateAsset(world, WorldPath);
                WorldMapProgressStore.Complete(world, world.Modules[0]);
                AssetDatabase.SaveAssets();
                SessionState.SetBool(Pending, true);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Step " + step);
                if (frame == Time.frameCount || WorldMapSceneRouter.IsLoading) return;
                frame = Time.frameCount;
                var world = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(WorldPath);
                var router = WorldMapSceneRouter.Instance;
                Scene scene = SceneManager.GetActiveScene();
                if (scene.path == SelectPath)
                {
                    Check(step == 3, "Unexpected selection scene");
                    Check(!BulletTimeController.BulletTimeOn && Time.timeScale == 1f, "Half speed leaked into selection");
                    Check(!PauseMenu.IsOpen, "Pause state leaked into selection");
                    Check(router.Enter(world, world.Modules[0]), "Fresh launch failed");
                    step = 4;
                    return;
                }
                Check(scene.path == PlayPath, "Unexpected play scene");
                var stage = GameStageProgression.ForScene(scene);
                var key = Object.FindObjectOfType<ProgressKey>();
                var player = Object.FindObjectOfType<PlayerController>();
                if (key == null || player == null || player.Body == null || player.Body.Motor == null) return;
                var door = Object.FindObjectOfType<ReturnJourneyDoor>();
                switch (step)
                {
                    case 0:
                        Check(stage.Stage == GameStage.Outbound && !key.IsCollected, "Initial run is not outbound");
                        Check(stage.TryMarkCue("fresh-run-cue"), "Cue already marked in fresh run");
                        Check(stage.BeginReturn(), "Cannot begin return");
                        if (Stages[cycle] == GameStage.Returning) step = 2;
                        else
                        {
                            Check(key.TryCollect(player), "Key pickup failed");
                            step = 1;
                        }
                        break;
                    case 1:
                        if (GameStageProgression.InputBlocked) return;
                        var menu = PauseMenu.Current;
                        if (PauseMenu.IsOpen)
                        {
                            var portrait = menu.GetComponent<ReturnJourneyPausePortrait>();
                            if (menu.IsTransitioning || portrait.IsRevealing) return;
                            Check(portrait.HasChanged, "Pickup did not restore the blueprint portrait");
                            menu.Resume();
                            return;
                        }
                        if (PauseMenu.BlocksInput) return;
                        Check(stage.HasKey && key.CarryVisual != null, "Pickup did not establish key ownership");
                        if (Stages[cycle] == GameStage.DoorOpened) Check(stage.CompleteDoor(), "Key consumption failed");
                        step = 2;
                        break;
                    case 2:
                        var platform = Object.FindObjectOfType<ReturnJourneyPlatform>();
                        if (Object.FindObjectOfType<ReturnJourneyPresentation>().IsEntryPlaying
                            || !platform.HasCollapsed || platform.IsDropping) return;
                        Check(platform.View.localPosition == platform.FallenLocalPosition,
                            "Platform without optional particles was marked collapsed but never fell");
                        // Invalid and locked launches must leave the current run intact.
                        Check(!router.Enter(world, world.Modules[2]), "Locked level accepted");
                        world.PlayScenePath = "";
                        Check(!router.Enter(world, world.Modules[0]), "Invalid scene accepted");
                        world.PlayScenePath = PlayPath;
                        Check(stage.Stage == Stages[cycle], "Failed launch erased progress");
                        WorldPhaseState.Instance.SetActive(true);
                        previousRewind = RewindManager.Current;
                        // Ordinary reloads preserve this run's state, including carried keys.
                        Check(router.Load(PlayPath), "Ordinary reload failed");
                        step = 5;
                        break;
                    case 5:
                        Check(stage.Stage == Stages[cycle], "Ordinary reload lost stage");
                        Check(!stage.TryMarkCue("fresh-run-cue"), "Ordinary reload lost cue history");
                        Check((key.CarryVisual != null) == stage.HasKey, "Reload restored incorrect carry visual");
                        Check(door.IsOpen == (stage.Stage == GameStage.DoorOpened), "Reload restored incorrect door state");
                        Check(!ReferenceEquals(previousRewind, RewindManager.Current), "Reload retained the old rewind manager");
                        Check(Object.FindObjectOfType<ReturnJourneyPlatform>().HasCollapsed
                            && Object.FindObjectOfType<IndicatorLight>().IsOn, "Reload lost return platform/light state");
                        Check(Object.FindObjectOfType<ReturnJourneyPresentation>().Blend == 1f, "Reload lost return effects");
                        Check(PauseMenu.Current.GetComponent<ReturnJourneyPausePortrait>().HasChanged
                            == (stage.Stage >= GameStage.CarryingKey), "Reload restored incorrect pause portrait");
                        Check(!PauseMenu.IsOpen && !GameplayActionLocks.IsLocked(
                            GameplayAction.Input | GameplayAction.PlayerControls | GameplayAction.Rewind, scene), "Reload retained a pause/input lock");
                        var timer = Object.FindObjectOfType<BulletTimeController>();
                        timer.ToggleBulletTime();
                        Check(timer.BulletTimeActive && Time.timeScale == 0.5f, "Cannot exercise a slowed scene exit");
                        Check(router.Load(SelectPath), "Return to selection failed");
                        step = 3;
                        break;
                    case 4:
                        Check(WorldMapSceneRouter.ConsumeModule(world) == world.Modules[0].Id, "Selected first level lost");
                        Check(stage.Stage == GameStage.Outbound && !stage.HasKey && stage.CarriedKey == null,
                            "Fresh launch retained stage/key ownership");
                        Check(!key.IsCollected && key.CarryVisual == null && key.GetComponent<BoxCollider2D>().enabled,
                            "Fresh launch restored a carried key");
                        Check(!door.IsOpen && door.GetComponent<BoxCollider2D>().enabled, "Fresh launch retained open door");
                        Check(stage.TryMarkCue("fresh-run-cue"), "Fresh launch retained cue history");
                        Check(!Object.FindObjectOfType<ReturnJourneyPlatform>().HasCollapsed
                            && !Object.FindObjectOfType<IndicatorLight>().IsOn, "Fresh launch retained platform/light state");
                        Check(!Object.FindObjectOfType<ReturnJourneyPresentation>().HasActiveEffect, "Fresh launch retained return effects");
                        Check(Object.FindObjectOfType<LevelEndpoint>().GetComponentInChildren<Light>().color == Color.green,
                            "Fresh launch retained red exit lights");
                        Check(!PauseMenu.IsOpen && !PauseMenu.Current.GetComponent<ReturnJourneyPausePortrait>().HasChanged,
                            "Fresh launch retained pause/blueprint state");
                        Check(!WorldPhaseState.Instance.Active && !BulletTimeController.BulletTimeOn && Time.timeScale == 1f,
                            "Fresh launch retained mechanism phase or slow motion");
                        Check(AudioManager.Instance.CurrentMusic != MusicTrack.KeyEscape && !AudioManager.Instance.HasPendingEndingMusic,
                            "Fresh launch retained key music or a delayed ending cue");
                        Check(WorldMapProgressStore.Completed(world).Contains(world.Modules[0].Id)
                            && WorldMapProgressStore.IsUnlocked(world, world.Modules[1]), "Fresh launch erased saved map progress");
                        Pass(Stages[cycle] + ": reload preserves state; selection -> first level resets stage, key, door and cues; saved progress survives");
                        Pass("Platform, guide/exit lights, return effects, pause portrait, input locks, rewind, phase, bullet time and music do not leak into the new run");
                        if (++cycle == Stages.Length) { Finish(null); return; }
                        Check(stage.BeginReturn(), "Next cycle cannot begin return");
                        if (Stages[cycle] == GameStage.Returning) step = 2;
                        else { Check(key.TryCollect(player), "Next cycle pickup failed"); step = 1; }
                        break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Pass(string message) { File.AppendAllText(Results, "PASS: " + message + "\n"); }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            var world = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(WorldPath);
            if (world != null) WorldMapProgressStore.Clear(world);
            if (error != null)
            {
                File.AppendAllText(Results, "FAIL step " + step + ": " + error + "\n");
                Debug.LogException(error);
            }
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
