using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Run in a separate project copy: -batchmode -executeMethod TapTap.Editor.AudioRegressionTests.RunBatch
    [InitializeOnLoad]
    public static class AudioRegressionTests
    {
        private const string Pending = "TapTap.AudioRegression.Pending";
        private static readonly List<string> results = new List<string>();
        private static AudioManager audio;
        private static PlayerController player;
        private static PauseMenu pause;
        private static AsyncOperation loading;
        private static int stage, lastFrame;
        private static double next, deadline;
        private static float oldMusic, oldEffects;
        private static bool hadMusic, hadEffects;
        private static string DirectoryPath => Path.Combine(Application.dataPath, "../Logs/Audio");

        static AudioRegressionTests()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
                stage = 0; lastFrame = -1;
                deadline = EditorApplication.timeSinceStartup + 60;
                next = 0;
                EditorApplication.update += Tick;
            };
        }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate batch project.");
            Directory.CreateDirectory(DirectoryPath);
            Scene empty = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Temp"));
            EditorSceneManager.SaveScene(empty, "Assets/Temp/AudioRegressionEmpty.unity");
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }
        private static void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); }
        private static void Pass(string message) { results.Add("PASS: " + message); Debug.Log("AUDIO PASS: " + message); }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Audio regression timed out");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || Time.frameCount == lastFrame
                    || EditorApplication.timeSinceStartup < next) return;
                lastFrame = Time.frameCount;
                if (loading != null && !loading.isDone) return;
                if (stage == 0)
                {
                    audio = AudioManager.Instance;
                    hadMusic = PlayerPrefs.HasKey(AudioManager.MusicPreference);
                    hadEffects = PlayerPrefs.HasKey(AudioManager.EffectsPreference);
                    oldMusic = audio.MusicVolume; oldEffects = audio.EffectsVolume;
                    Check(audio.Library != null, "Catalog not auto-loaded");
                    foreach (SoundId id in Enum.GetValues(typeof(SoundId)))
                    {
                        SoundDefinition sound = audio.Library.Find(id);
                        Check(sound != null && sound.Clips.Length > 0, "Missing cue: " + id);
                        foreach (AudioClip clip in sound.Clips)
                            Check(clip != null && clip.length > .01f, "Missing imported clip: " + id);
                    }
                    Pass("All " + Enum.GetValues(typeof(SoundId)).Length + " resource catalog entries resolve imported clips");
                    new GameObject("Duplicate Audio Manager").AddComponent<AudioManager>();
                    var floor = new GameObject("Audio Test Floor");
                    floor.transform.position = Vector3.down * .5f;
                    floor.AddComponent<BoxCollider2D>().size = new Vector2(30f, 1f);
                    floor.AddComponent<WorldSurface>();
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Player.prefab");
                    Check(prefab != null, "Player prefab missing");
                    player = Object.Instantiate(prefab).GetComponent<PlayerController>();
                    player.GetComponent<PlayerInput>().SetExternalInput(0f, false);
                    player.TeleportJoined(new Vector2(0f, .5f));
                    RewindManager.Current.UseKeyboard = false;
                }
                else if (stage == 1)
                {
                    Check(Object.FindObjectsOfType<AudioManager>().Length == 1, "Duplicate manager survived");
                    Check(player.GetComponents<PlayerAudioFeedback>().Length == 1, "Player auto-binding failed");
                    MenuAudio menu = MenuAudio.Ensure(AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/QuietWorkshop.wav"),
                        AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SoftClick.wav"));
                    menu.SetMusicVolume(.23f); menu.SetEffectsVolume(.41f);
                    Check(Mathf.Abs(audio.MusicSource.volume-.23f)<.001f && Mathf.Abs(audio.EffectsSource.volume-.41f)<.001f,
                        "Menu facade lost volume routing");
                    Check(menu.MusicSource == audio.MusicSource && audio.MusicSource.loop, "Music source duplicated");
                    Pass("Singleton, automatic player bridge, existing menu/BGM/volume compatibility");
                    var voiceObject = new GameObject("Existing Voice Integration");
                    VoicePlayer voice = voiceObject.AddComponent<VoicePlayer>();
                    VoiceSequence sequence = ScriptableObject.CreateInstance<VoiceSequence>();
                    sequence.Volume = .8f;
                    sequence.Segments = new[] { new VoiceSegment { Clip = audio.Library.Find(SoundId.MagnetJoin).Clips[0] } };
                    Check(voice.Play(sequence), "Existing voice player failed");
                    voice.SetDistanceGain(.5f);
                    Check(Mathf.Abs(voice.Source.volume-.8f*.5f*.41f)<.001f, "Radio voice no longer follows effects volume");
                    Object.DestroyImmediate(voiceObject);
                    Object.DestroyImmediate(sequence);
                    Pass("Existing VoicePlayer keeps distance and unified effects-volume gain");
                    int before = audio.PlayedCount;
                    player.GetComponent<PlayerInput>().SetExternalInput(0f, true);
                    RewindManager.Current.SimulateStep(Time.fixedDeltaTime);
                    Check(audio.PlayedCount > before && audio.LastPlayed == SoundId.MagnetRelease, "Space press did not emit release");
                    Pass("Real Space input and player simulation trigger magnetic release");
                }
                else if (stage == 2)
                {
                    Check(audio.ActiveLoopCount == 1, "Magnetic hold should own one loop");
                    int before = audio.PlayedCount;
                    player.GetComponent<PlayerInput>().SetExternalInput(0f, false);
                    for (int i=0; i<100; i++) RewindManager.Current.SimulateStep(Time.fixedDeltaTime);
                    Check(player.Phase == PlayerPhase.Joined && audio.PlayedCount > before, "Retraction did not join audibly");
                    Pass("Magnet loop deduplication and real retract/join feedback");
                    bool historical = WorldPhaseState.Instance.Active;
                    before = audio.PlayedCount;
                    WorldPhaseState.Instance.Toggle();
                    Check(audio.PlayedCount > before, "Normal phase toggle silent");
                    before = audio.PlayedCount;
                    WorldPhaseState.Instance.RestoreState(in historical);
                    Check(audio.PlayedCount == before, "Historical phase restore replayed sound");
                    Pass("Normal mechanism changes sound; snapshot restoration stays silent");
                    var springObject = new GameObject("Audible Spring");
                    WorldSurface springSurface = springObject.AddComponent<WorldSurface>();
                    springObject.AddComponent<SpringPad>();
                    Check(SpringPad.TryBounce(springSurface, 24f, out float launchSpeed) && launchSpeed > 0f
                        && audio.LastPlayed == SoundId.Bounce, "Spring event not routed");
                    Object.DestroyImmediate(springObject);
                    var pistonObject = new GameObject("Audible Piston");
                    PhasePiston piston = pistonObject.AddComponent<PhasePiston>();
                    piston.SetExtended(true, false);
                    Check(audio.LastPlayed == SoundId.PistonMove, "Piston event not routed");
                    before = audio.PlayedCount;
                    piston.SetExtended(false, true);
                    Check(audio.PlayedCount == before, "Immediate piston restore emitted sound");
                    Object.DestroyImmediate(pistonObject);
                    Pass("Actual spring/piston operations sound, immediate piston restoration stays silent");
                    for (int i=0; i<3; i++)
                    {
                        var belt = new GameObject("Conveyor Tile " + i);
                        belt.transform.position = new Vector3(2+i, 0f, 0f);
                        belt.AddComponent<BoxCollider2D>();
                        belt.AddComponent<WorldSurface>().ConveyorSpeed = 2f;
                    }
                }
                else if (stage == 3)
                {
                    Check(audio.ActiveLoopCount == 1, "Adjacent conveyor tiles should share one loop");
                    Pass("Three nearby conveyor tiles aggregate into one loop");
                    Check(RewindManager.Current.BeginRewind(), "Rewind did not start");
                    Check(audio.LastPlayed == SoundId.RewindStart, "Rewind start missing");
                    int before = audio.PlayedCount;
                    Check(!audio.Play(SoundId.Death), "Gameplay cue leaked during rewind");
                    RewindManager.Current.RewindBy(.1f);
                    Check(audio.PlayedCount == before, "Restore emitted historical effects");
                    Pass("Rewind start, gameplay suppression and silent historical restore");
                }
                else if (stage == 4)
                {
                    Check(audio.ActiveLoopCount == 1, "Rewind should retain only its own loop");
                    RewindManager.Current.EndRewind();
                    Check(audio.LastPlayed == SoundId.RewindEnd, "Rewind exit missing");
                    player.GetComponent<PlayerInput>().SetExternalInput(0f, false);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PauseMenu.prefab");
                    pause = Object.Instantiate(prefab).GetComponent<PauseMenu>();
                    Check(pause.Open(), "Pause menu failed");
                    Check(!audio.Play(SoundId.Bounce), "Gameplay cue leaked while paused");
                    Check(audio.Play(SoundId.Click), "UI cue blocked while paused");
                    Pass("Rewind exit, real pause menu gating and UI audio while paused");
                }
                else if (stage == 5)
                {
                    pause.Resume();
                    Object.DestroyImmediate(pause.gameObject);
                    Time.timeScale = 1f;
                    int before = audio.PlayedCount;
                    RespawnService respawn = player.GetComponent<RespawnService>();
                    respawn.BeginGroupReturn(player.Body, player.Head, player.Config.ToWorld(player.Config.JoinedOffset));
                    Check(audio.PlayedCount == before+1 && audio.LastPlayed == SoundId.Death, "Grouped death missing or doubled");
                    respawn.Simulate(5f);
                    Check(audio.PlayedCount == before+2 && audio.LastPlayed == SoundId.Respawn, "Grouped rebuild missing or doubled");
                    Pass("Real grouped return produces exactly one death and one rebuild cue");
                    Object.DestroyImmediate(player.gameObject);
                    audio.StopGameplay();
                    audio.SetLoop(SoundId.MagnetHum, audio, true);
                    Check(audio.ActiveLoopCount == 1, "Loop setup failed");
                    loading = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Temp/AudioRegressionEmpty.unity",
                        new LoadSceneParameters(LoadSceneMode.Single));
                    Pass("Pause resumes without replacing persistent audio service");
                }
                else
                {
                    Check(audio.ActiveLoopCount == 0, "Loop leaked after scene change/owner destruction");
                    Check(audio.MusicSource.clip != null && audio.MusicSource.loop, "Persistent music lost");
                    Pass("Loop cleanup and persistent music across scene lifetime");
                    Finish(null);
                    return;
                }
                stage++;
                next = EditorApplication.timeSinceStartup + (stage == 3 ? .65 : .22);
            }
            catch (Exception exception) { Finish(exception); }
        }
        private static void Finish(Exception exception)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            if (exception != null) { results.Add("FAIL: " + exception); Debug.LogException(exception); }
            if (audio != null) { audio.SetMusicVolume(oldMusic); audio.SetEffectsVolume(oldEffects); }
            if (!hadMusic) PlayerPrefs.DeleteKey(AudioManager.MusicPreference);
            if (!hadEffects) PlayerPrefs.DeleteKey(AudioManager.EffectsPreference);
            PlayerPrefs.Save();
            File.WriteAllLines(Path.Combine(DirectoryPath, "regression-results.txt"), results);
            EditorApplication.Exit(exception == null ? 0 : 1);
        }
    }
}
