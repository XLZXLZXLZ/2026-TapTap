using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Run in an isolated batch project. Temporary silent clips exercise distinct BGM slots without new music assets.
    [InitializeOnLoad]
    public static class BackgroundMusicRegressionTests
    {
        private const string Pending = "TapTap.BackgroundMusicRegression";
        private const string Results = "Logs/BGM/results.txt";
        private const string Selection = "Assets/Scenes/WorldMaps/World_Character_Select.unity";
        private const string Gameplay = "Assets/Scenes/WorldMaps/World_Character_Play.unity";
        private const string Ending = "Assets/Scenes/MainMenu/Ending.unity";
        private static AudioManager audio;
        private static BackgroundMusicLibrary originalLibrary, testLibrary;
        private static WorldMapDefinition world;
        private static IEnumerator flow;
        private static Func<bool> waiting;
        private static double readyAt, deadline;
        private static int frame;
        private static bool hadMusic, hadProgress;
        private static float oldMusic;
        private static string progressKey, oldProgress;

        static BackgroundMusicRegressionTests()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
                AudioListener.volume = 0f;
                world = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
                hadMusic = SessionState.GetBool(Pending + ".HadMusic", false);
                oldMusic = SessionState.GetFloat(Pending + ".OldMusic", 0.45f);
                progressKey = SessionState.GetString(Pending + ".ProgressKey", "");
                hadProgress = SessionState.GetBool(Pending + ".HadProgress", false);
                oldProgress = SessionState.GetString(Pending + ".OldProgress", "");
                frame = -1;
                readyAt = 0;
                waiting = null;
                deadline = EditorApplication.timeSinceStartup + 480;
                flow = SessionState.GetBool(Pending + ".RoutesOnly", false) ? ValidateTransitionRoutes() : Validate();
                SessionState.SetBool(Pending + ".RoutesOnly", false);
                EditorApplication.update += Tick;
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            PlayerSettings.companyName = "CodexValidation";
            PlayerSettings.productName = "TapTap BGM Regression";
            Directory.CreateDirectory("Logs/BGM");
            File.WriteAllText(Results, "Unity " + Application.unityVersion + "\n");
            hadMusic = PlayerPrefs.HasKey(AudioManager.MusicPreference);
            oldMusic = PlayerPrefs.GetFloat(AudioManager.MusicPreference);
            world = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
            progressKey = "TapTap.WorldProgress." + (string.IsNullOrEmpty(world.ProgressId) ? world.name : world.ProgressId);
            hadProgress = PlayerPrefs.HasKey(progressKey);
            oldProgress = PlayerPrefs.GetString(progressKey);
            SessionState.SetBool(Pending + ".HadMusic", hadMusic);
            SessionState.SetFloat(Pending + ".OldMusic", oldMusic);
            SessionState.SetString(Pending + ".ProgressKey", progressKey);
            SessionState.SetBool(Pending + ".HadProgress", hadProgress);
            SessionState.SetString(Pending + ".OldProgress", oldProgress);
            foreach (var module in world.OrderedModules()) WorldMapProgressStore.Unlock(world, module);
            EditorSceneManager.OpenScene(MainMenuAssets.ScenePath);
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        public static void RunTransitionRoutesBatch()
        {
            SessionState.SetBool(Pending + ".RoutesOnly", true);
            RunBatch();
        }

        private static IEnumerator ValidateTransitionRoutes()
        {
            yield return 4.7f;
            audio = AudioManager.Instance;
            originalLibrary = audio.MusicLibrary;
            testLibrary = Object.Instantiate(originalLibrary);
            testLibrary.BGM_5 = Clip(5);
            audio.MusicLibrary = testLibrary;
            Expect(MusicTrack.Menu, testLibrary.BGM_0);
            bool silentOnLoad = true;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> observe = (scene, mode) =>
            {
                if (mode != LoadSceneMode.Single) return;
                foreach (AudioSource source in audio.GetComponentsInChildren<AudioSource>())
                    if (source.name.StartsWith("Music ") && source.volume > 0f) silentOnLoad = false;
            };
            SceneManager.sceneLoaded += observe;
            var escape = new GameObject("BGM escape handoff").AddComponent<FinalEscapeEndingTransition>();
            escape.HoldDuration = escape.FadeDuration = 0.01f;
            escape.Play();
            yield return Loaded(Ending);
            var ending = Object.FindObjectOfType<EndingSequenceController>();
            ending.Radio.enabled = false;
            ending.ReturnToMainMenuAfterBroadcast = false;
            ending.ReturnControl();
            yield return (Func<bool>)(() => ending.State == EndingState.Exploration);
            Check(audio.IsMusicPaused && !audio.MusicSource.isPlaying && silentOnLoad,
                "Escape/router handoff loaded Ending before music was silent.");
            ending.Radio.enabled = true;
            Check(ending.Radio.TryActivate(ending.Player), "Ending radio did not start after escape handoff.");
            yield return 3.5f;
            Expect(MusicTrack.Ending, testLibrary.BGM_5);
            Pass("Escape black cover hands music ownership to the router; Ending loads in silence and its radio can fade music back in.");
            ending.AfterVoiceHold = 0f;
            ending.FadeToWhiteDuration = ending.WhiteHoldDuration = ending.RevealMainMenuDuration = 0.01f;
            ending.ReturnToMainMenu();
            yield return Loaded(MainMenuAssets.ScenePath);
            yield return 3.5f;
            Expect(MusicTrack.Menu, testLibrary.BGM_0);
            Check(silentOnLoad, "White fade loaded the main menu before music was silent.");
            SceneManager.sceneLoaded -= observe;
            Pass("Ending white cover waits for fade-out and silence, then releases menu music for fade-in after loading.");
        }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("BGM validation timed out.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
                frame = Time.frameCount;
                if (EditorApplication.timeSinceStartup < readyAt || waiting != null && !waiting()) return;
                waiting = null;
                if (!flow.MoveNext()) { Finish(null); return; }
                if (flow.Current is float seconds) readyAt = EditorApplication.timeSinceStartup + seconds;
                else if (flow.Current is Func<bool> condition) waiting = condition;
            }
            catch (Exception error) { Finish(error); }
        }

        private static IEnumerator Validate()
        {
            yield return 4.7f;
            audio = AudioManager.Instance;
            originalLibrary = audio.MusicLibrary;
            Check(originalLibrary != null && originalLibrary.BGM_0 == audio.Library.BackgroundMusic, "Menu track migration failed.");
            Check(originalLibrary.FadeInDuration == 2f && originalLibrary.FadeOutDuration == 2f
                && originalLibrary.EndingMusicDelay == 1f, "Default transition timings changed.");
            Expect(MusicTrack.Menu, originalLibrary.BGM_0);
            foreach (WorldMapModule module in world.OrderedModules())
            {
                int number = world.SequenceNumber(module);
                MusicTrack? expected = number <= 5 ? MusicTrack.Region1 : number <= 12 ? MusicTrack.Region2
                    : number <= 19 ? MusicTrack.Region3 : (MusicTrack?)null;
                Check(originalLibrary.TrackFor(module.Level.Style, number) == expected, "Region mapping wrong at " + number);
            }
            Check(originalLibrary.TrackFor(world.OrderedModules()[0].Level.Style, 100) == MusicTrack.Region1,
                "Region music still depends on hardcoded level ranges.");
            Pass("Real configuration resolves existing style assets; level 20 overrides its yellow style; menu uses QuietWorkshop.");

            testLibrary = Object.Instantiate(originalLibrary);
            testLibrary.BGM_1 = Clip(1); testLibrary.BGM_2 = Clip(2); testLibrary.BGM_3 = Clip(3);
            testLibrary.BGM_4 = Clip(4); testLibrary.BGM_5 = Clip(5);
            audio.MusicLibrary = testLibrary;
            audio.SetMusicVolume(0.6f);
            AudioSource menu = audio.MusicSource;
            menu.time = 5f;
            audio.ConfigureMenu(audio.Library.Find(SoundId.Click).Clips[0], null);
            Check(menu == audio.MusicSource && menu.time > 4.8f, "Repeated menu configuration restarted music.");
            Time.timeScale = 0f; AudioListener.pause = true;
            int before = menu.timeSamples;
            yield return 0.3f;
            Check(menu.isPlaying && menu.timeSamples != before, "BGM stopped during gameplay/listener pause.");
            Time.timeScale = 1f; AudioListener.pause = false;
            menu.time = menu.clip.length - 0.1f;
            yield return 0.4f;
            Check(menu.time < 3f, "Menu track did not loop.");
            Load(Selection);
            yield return Loaded(Selection);
            yield return 2.3f;
            Expect(MusicTrack.Menu, testLibrary.BGM_0);
            Check(audio.MusicSource == menu, "Main menu to selection replaced the music source.");
            Pass("Menu/selection share one track and playhead; looping and pause behavior remain intact.");

            Check(WorldMapSceneRouter.Instance.Enter(world, world.OrderedModules()[0]), "Cannot enter first level.");
            yield return Loaded(Gameplay);
            yield return 0.05f;
            AudioSource first = audio.MusicSource;
            Check(audio.CurrentMusic == MusicTrack.Region1 && first != menu, "Level entry did not select region 1.");
            yield return 4.7f;
            Expect(MusicTrack.Region1, testLibrary.BGM_1);
            Check(!menu.isPlaying && first.ignoreListenerPause && first.loop, "Outgoing source was not paused after fade.");
            // Sample a controlled transition after loading: heavy asset loading may finish the entry fade before observation.
            Time.timeScale = 0f;
            testLibrary.FadeInDuration = testLibrary.FadeOutDuration = 0.5f;
            testLibrary.SilenceDuration = 0.5f;
            audio.ApplyLevelMusic(SceneManager.GetActiveScene(), world.OrderedModules()[5].Level, 6);
            yield return 0.15f;
            Check(first.volume > 0f && first.volume < audio.MusicVolume
                && audio.MusicSource == first, "Outgoing music did not fade before the new track.");
            audio.SetMusicVolume(0.3f);
            Check(first.volume <= 0.3f, "Volume setting ignored fade gains.");
            yield return 0.45f;
            Check(first.volume == 0f && !first.isPlaying && audio.IsMusicPaused, "Music did not leave a silent gap between tracks.");
            yield return 1.2f;
            Expect(MusicTrack.Region2, testLibrary.BGM_2);
            audio.ApplyLevelMusic(SceneManager.GetActiveScene(), world.OrderedModules()[0].Level, 1);
            yield return 1.8f;
            Expect(MusicTrack.Region1, testLibrary.BGM_1);
            testLibrary.FadeInDuration = testLibrary.FadeOutDuration = 2f;
            testLibrary.SilenceDuration = 0.35f;
            Time.timeScale = 1f;
            Pass("Sequential fade-out, silent gap and fade-in work during pause; volume settings preserve fade gains.");
            WorldMapProgression progression = WorldMapProgression.Current;
            progression.UseKeyboard = false;
            progression.FadeDuration = 0.01f; progression.AssemblyDuration = 0.01f; progression.AssemblyHold = 0f;
            PlayerController player = Object.FindObjectOfType<PlayerController>();
            player.GetComponent<PlayerInput>().SetExternalInput(0f, false);
            first.time = 5f;
            MenuAudio.Ensure(testLibrary.BGM_0, null);
            Check(audio.MusicSource == first && first.time > 4.8f, "Pause menu initialization selected menu music.");
            audio.StopGameplay();
            Check(first.isPlaying, "Stopping SFX stopped music.");
            LevelEndpoint endpoint = progression.CurrentRegion.GetComponentInChildren<LevelEndpoint>();
            endpoint.EntryWalkSpeed = 1000f;
            Check(progression.CompleteCurrent(endpoint), "Cannot complete level 1.");
            yield return TransitionFinished();
            Check(progression.CurrentNumber == 2 && audio.MusicSource == first && first.time > 4.8f,
                "Same-region completion restarted music.");
            Check(progression.GoToSequence(5), "Cannot go to level 5.");
            yield return TransitionFinished();
            Check(audio.MusicSource == first && first.time > 4.8f, "Same-region level transition restarted music.");
            endpoint = progression.CurrentRegion.GetComponentInChildren<LevelEndpoint>();
            endpoint.EntryWalkSpeed = 1000f;
            Check(progression.CompleteCurrent(endpoint), "Cannot complete level 5.");
            yield return TransitionFinished();
            yield return 4.7f;
            Check(progression.CurrentNumber == 6, "Completion did not advance to level 6.");
            Expect(MusicTrack.Region2, testLibrary.BGM_2);
            Pass("Actual level completion crosses red/blue boundary; same-region jumps and pause-menu setup preserve playback.");

            AudioSource second = audio.MusicSource;
            second.time = 5f;
            Check(progression.RestartCurrent(), "Cannot restart level 6.");
            yield return TransitionFinished();
            Check(audio.MusicSource == second && second.time > 4.8f, "Restart reset the region playhead.");
            Check(progression.GoToSequence(12), "Cannot go to level 12.");
            yield return TransitionFinished();
            Check(audio.MusicSource == second && second.time > 4.8f, "Same blue-region transition restarted music.");
            Check(progression.GoToSequence(progression.GetComponent<WorldMapRoot>().Source.SequenceNumber(progression.Modules[progression.CurrentIndex + 1])), "Cannot cross into yellow region.");
            yield return TransitionFinished();
            yield return 4.7f;
            Expect(MusicTrack.Region3, testLibrary.BGM_3);
            Check(progression.GoToSequence(19), "Cannot go to level 19.");
            yield return TransitionFinished();
            AudioSource third = audio.MusicSource;
            third.time = 5f;
            Check(progression.GoToSequence(progression.GetComponent<WorldMapRoot>().Source.SequenceNumber(progression.Modules[progression.CurrentIndex + 1])), "Cannot enter level 20.");
            yield return TransitionFinished();
            yield return 4.7f;
            Check(progression.CurrentNumber == 20 && audio.IsMusicPaused && !third.isPlaying, "Level 20 did not fade to silence.");
            Check(third.time > 4.8f, "Pause lost the music playhead.");
            Pass("Blue/yellow boundary switches tracks, restart preserves playhead, and level 20 fades out and pauses.");

            GameStageProgression stage = GameStageProgression.ForScene(player.gameObject.scene);
            Check(stage.BeginReturn(), "Cannot begin return journey.");
            ProgressKey key = Object.FindObjectOfType<ProgressKey>();
            Check(key != null && key.TryCollect(player), "Cannot pick up key.");
            Check(audio.CurrentMusic == MusicTrack.KeyEscape, "Key music waited for the pickup cinematic to finish.");
            yield return (Func<bool>)(() => !GameStageProgression.InputBlocked);
            if (PauseMenu.IsOpen) PauseMenu.Current.Resume();
            yield return 4.7f;
            Expect(MusicTrack.KeyEscape, testLibrary.BGM_4);
            AudioSource escape = audio.MusicSource;
            WorldMapProgression.Snapshot snapshot = new WorldMapProgression.Snapshot { CurrentIndex = 0 };
            progression.RestoreState(in snapshot);
            Check(audio.CurrentMusic == MusicTrack.KeyEscape && audio.MusicSource == escape, "Return across regions replaced key music.");
            Check(stage.CompleteDoor(), "Cannot open door.");
            Check(audio.CurrentMusic == MusicTrack.KeyEscape, "Door opening replaced escape music.");
            Load(Gameplay);
            yield return Loaded(Gameplay);
            yield return 4.7f;
            Expect(MusicTrack.KeyEscape, testLibrary.BGM_4);
            Check(WorldMapProgression.Current.ReturnToSelection(), "Cannot return to selection.");
            yield return Loaded(Selection);
            yield return 4.7f;
            Expect(MusicTrack.Menu, testLibrary.BGM_0);
            Check(WorldMapSceneRouter.Instance.Enter(world, world.OrderedModules()[5]), "Cannot launch a fresh run in blue region.");
            yield return Loaded(Gameplay);
            yield return 4.7f;
            Expect(MusicTrack.Region2, testLibrary.BGM_2);
            Check(GameStageProgression.StageIn(SceneManager.GetActiveScene()) == GameStage.Outbound, "New run retained key stage.");
            Pass("Immediate pickup cue survives region restoration, door opening and reload; return to selection resets menu music and a fresh run resets priority.");

            // An absent slot must fade the previous music out, without falling back to an unrelated track.
            AudioClip saved = testLibrary.BGM_3;
            testLibrary.BGM_3 = null;
            audio.ApplyLevelMusic(SceneManager.GetActiveScene(), world.OrderedModules()[12].Level, 13);
            yield return 4.7f;
            Check(audio.CurrentMusic == MusicTrack.Region3 && audio.IsMusicPaused && !audio.MusicSource.isPlaying, "Missing BGM retained old music.");
            testLibrary.BGM_3 = saved;
            // Rapidly reverse a fade and then request a third clip; there must still be only two music voices.
            audio.ApplyLevelMusic(SceneManager.GetActiveScene(), world.OrderedModules()[0].Level, 1);
            yield return 0.1f;
            audio.ApplyLevelMusic(SceneManager.GetActiveScene(), world.OrderedModules()[5].Level, 6);
            yield return 0.1f;
            audio.ApplyLevelMusic(SceneManager.GetActiveScene(), world.OrderedModules()[12].Level, 13);
            yield return 4.7f;
            Expect(MusicTrack.Region3, testLibrary.BGM_3);
            Check(audio.transform.Find("Music 1") != null && audio.transform.Find("Music 2") != null
                && audio.transform.Find("Music 3") == null, "Music switching leaked sources.");
            Pass("Missing clips produce silence; interrupted fades settle on the latest request using two sources.");

            var escapeTransition = new GameObject("BGM escape handoff").AddComponent<FinalEscapeEndingTransition>();
            escapeTransition.HoldDuration = 0.01f;
            escapeTransition.FadeDuration = 0.01f;
            escapeTransition.Play();
            yield return Loaded(Ending);
            EndingSequenceController ending = Object.FindObjectOfType<EndingSequenceController>();
            ending.Radio.enabled = false;
            ending.Player.UseKeyboard = false;
            ending.ReturnToMainMenuAfterBroadcast = false;
            ending.ReturnControl();
            yield return (Func<bool>)(() => ending.State == EndingState.Exploration);
            yield return 4.7f;
            Check(audio.IsMusicPaused && !audio.MusicSource.isPlaying, "Ending scene did not fade out music.");
            ending.Radio.enabled = true;
            Check(ending.Radio.TryActivate(ending.Player), "Ending radio interaction failed.");
            Check(audio.HasPendingEndingMusic && audio.IsMusicPaused, "Ending BGM started without its delay.");
            yield return 0.25f;
            Check(audio.CurrentMusic == null, "Ending BGM ignored the default 1-second delay.");
            ending.ReturnControl();
            yield return 4.7f;
            Check(!audio.HasPendingEndingMusic && audio.CurrentMusic == null, "Interrupted interaction leaked its delayed cue.");
            Check(ending.Radio.TryActivate(ending.Player), "Cannot retry ending radio.");
            yield return 0.25f;
            Check(audio.CurrentMusic == null, "Retry started BGM too early.");
            yield return 3.5f;
            Expect(MusicTrack.Ending, testLibrary.BGM_5);
            Pass("Ending is silent on entry; successful radio interaction waits 1 second, supports interruption/retry, then fades in BGM_5.");
            ending.ReturnControl();
            yield return 1f;
            // Return to the menu during a pending cue, before it is eligible to play.
            Load(Ending);
            yield return Loaded(Ending);
            ending = Object.FindObjectOfType<EndingSequenceController>();
            ending.Radio.enabled = false;
            ending.ReturnToMainMenuAfterBroadcast = false;
            ending.ReturnControl();
            yield return (Func<bool>)(() => ending.State == EndingState.Exploration);
            ending.Radio.enabled = true;
            Check(ending.Radio.TryActivate(ending.Player), "Cannot begin final delayed cue.");
            ending.AfterVoiceHold = 0f;
            ending.FadeToWhiteDuration = 0.01f;
            ending.WhiteHoldDuration = 0.01f;
            ending.RevealMainMenuDuration = 0.01f;
            ending.ReturnToMainMenu();
            yield return Loaded(MainMenuAssets.ScenePath);
            yield return 3.5f;
            Expect(MusicTrack.Menu, testLibrary.BGM_0);
            Check(!audio.HasPendingEndingMusic && Object.FindObjectsOfType<AudioManager>().Length == 1,
                "Scene change leaked delayed playback or duplicated the singleton.");
            Pass("Escape/router handoff and ending white fade preserve the silent gap, cancel delayed playback and release music for the new scene.");
        }

        private static AudioClip Clip(int index) => AudioClip.Create("BGM_Test_" + index, 44100 * 16, 1, 44100, false);
        private static Func<bool> Loaded(string path) => () => !WorldMapSceneRouter.IsLoading && SceneManager.GetActiveScene().path == path;
        private static Func<bool> TransitionFinished() => () => !WorldMapProgression.Current.IsTransitioning;
        private static void Load(string path) => Check(WorldMapSceneRouter.Instance.Load(path), "Cannot load " + path);
        private static void Expect(MusicTrack track, AudioClip clip)
        {
            Check(AudioManager.Instance == audio && audio.CurrentMusic == track && !audio.IsMusicPaused, "Wrong music state: expected " + track);
            Check(audio.MusicSource.clip == clip && audio.MusicSource.isPlaying && audio.MusicSource.loop, "Wrong playback for " + track);
            Check(Mathf.Abs(audio.MusicSource.volume - audio.MusicVolume) < 0.001f, "Fade did not reach configured music volume.");
        }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Pass(string message) => File.AppendAllText(Results, "PASS: " + message + "\n");
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            if (audio != null)
            {
                audio.MusicLibrary = originalLibrary;
                audio.SetMusicVolume(hadMusic ? oldMusic : 0.45f);
            }
            if (!hadMusic) PlayerPrefs.DeleteKey(AudioManager.MusicPreference);
            if (hadProgress) PlayerPrefs.SetString(progressKey, oldProgress); else PlayerPrefs.DeleteKey(progressKey);
            PlayerPrefs.Save();
            if (error != null) { File.AppendAllText(Results, "FAIL: " + error + "\n"); Debug.LogException(error); }
            else Debug.Log("Background music flow regression passed.");
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
