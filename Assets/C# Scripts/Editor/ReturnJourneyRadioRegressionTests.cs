using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    [InitializeOnLoad]
    public static class ReturnJourneyRadioRegressionTests
    {
        private const string Pending = "TapTap.ReturnJourneyRadioFocusTest";
        private static string Output => Path.GetFullPath("Logs/ReturnJourney/radio-focus-results.txt");
        private static int step, frame;
        private static double since, deadline, simulationStart;
        private static RadioReceiver radio;
        private static ReturnJourneyRadioFocus focus;
        private static PlayerController player;
        private static DemoCamera rig;
        private static LevelCameraShot initial;
        private static Vector3 pausedCamera;
        private static float pausedAudio;
        private static Transform otherLock;

        static ReturnJourneyRadioRegressionTests()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
                step = 0;
                frame = -1;
                deadline = EditorApplication.timeSinceStartup + 60;
                EditorApplication.update += Tick;
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            File.WriteAllText(Output, "");
            ReturnJourneyAssets.Build();
            EditorSceneManager.OpenScene(ReturnJourneyAssets.PreviewPath, OpenSceneMode.Single);
            foreach (ReturnJourneyTrigger trigger in Object.FindObjectsOfType<ReturnJourneyTrigger>()) trigger.enabled = false;
            var receiver = ((GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(ReturnJourneyAssets.RadioFocusPath))).GetComponent<RadioReceiver>();
            receiver.TriggerAutomatically = false;
            receiver.transform.position = new Vector3(-8f, 0f, 0f);
            Object.FindObjectOfType<PlayerController>().transform.position = new Vector3(-8f, 0.6f, 0f);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/ReturnJourneyRadioValidation.unity");
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        private static VoiceSequence Sequence(float seconds, bool multiple = false)
        {
            var sequence = ScriptableObject.CreateInstance<VoiceSequence>();
            var clip = AudioClip.Create("Radio focus validation", Mathf.RoundToInt(seconds * 24000), 1, 24000, false);
            var segment = new VoiceSegment { Clip = clip, Chinese = "返程广播测试", GapAfter = 0.2f };
            sequence.Segments = multiple ? new[] { segment, new VoiceSegment { Clip = clip, Chinese = "最后一段测试" } }
                : new[] { segment };
            return sequence;
        }

        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || Time.frameCount < 5 || frame == Time.frameCount) return;
                frame = Time.frameCount;
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Radio focus step " + step);
                double elapsed = EditorApplication.timeSinceStartup - since;
                if (step == 0)
                {
                    player = Object.FindObjectOfType<PlayerController>();
                    radio = Object.FindObjectOfType<RadioReceiver>();
                    focus = radio.GetComponent<ReturnJourneyRadioFocus>();
                    rig = Object.FindObjectOfType<DemoCamera>();
                    if (player == null || rig == null || RewindManager.Current == null) return;
                    player.GetComponent<PlayerInput>().UseKeyboard = false;
                    RewindManager.Current.UseKeyboard = false;
                    radio.Player = player;
                    radio.FirstEncounter = Sequence(1f);
                    radio.ReturnJourney = Sequence(1.2f, true);
                    focus.ApproachDuration = 0.25f;
                    focus.RestoreDuration = 0.2f;
                    Check(!focus.TryActivate(), "Focus trigger accepted outbound stage");
                    Check(radio.TryPlay(), "Normal outbound broadcast failed");
                    Next();
                }
                else if (step == 1 && elapsed > 0.2)
                {
                    Check(!focus.IsActive && !WorldMapProgression.InputBlocked && !rig.HasExternalControl,
                        "Outbound broadcast took control");
                    radio.Playback.Stop();
                    Check(GameStageProgression.ForScene(radio.gameObject.scene).BeginReturn(), "Could not start return stage");
                    initial = LevelCameraShot.Read(rig.ViewCamera);
                    simulationStart = RewindManager.Current.SimulationTime;
                    Check(focus.TryActivate() && focus.IsActive && rig.HasExternalControl, "Return activation did not acquire camera");
                    Check(!WorldMapProgression.InputBlocked
                        && GameplayActionLocks.IsLocked(GameplayAction.PlayerControls, player.gameObject.scene)
                        && !RewindManager.Current.BeginRewind(), "Focus should block controls and Z without freezing simulation");
                    player.GetComponent<PlayerInput>().SetExternalInput(0.5f, false);
                    Next();
                }
                else if (step == 2 && elapsed > 0.5)
                {
                    Check(radio.Playback.IsPlaying && !radio.Playback.IsPaused && radio.Playback.SegmentElapsed > 0.2f,
                        "Broadcast stopped during focus");
                    Check(player.GetComponent<PlayerInput>().Horizontal == 0f
                        && RewindManager.Current.SimulationTime > simulationStart && Mathf.Approximately(Time.timeScale, 1f),
                        "Controls were not blocked, or simulation / time stopped during focus");
                    player.GetComponent<PlayerInput>().SetExternalInput(0f, false);
                    Check(Mathf.Approximately(focus.EffectiveCloseupHeight, 3f), "Focus prefab did not use the wider 3-grid view");
                    Vector3 target = focus.FocusTarget.position;
                    Vector3 viewport = rig.ViewCamera.WorldToViewportPoint(target);
                    Check(Vector2.Distance((Vector2)viewport, Vector2.one * 0.5f) < 0.02f && viewport.z > 0f,
                        "Close-up does not center on the radio");
                    float height = viewport.z * 2f * Mathf.Tan(rig.ViewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    Check(Mathf.Abs(height - focus.EffectiveCloseupHeight * player.Config.UnitSize) < 0.1f,
                        "Camera did not reach a close view");
                    Capture("radio-closeup");
                    pausedAudio = radio.Playback.SegmentElapsed;
                    pausedCamera = rig.ViewCamera.transform.position;
                    Check(PauseMenu.Current.Open(), "Pause menu unavailable during focus");
                    Next();
                }
                else if (step == 3 && elapsed > 0.35)
                {
                    Check(radio.Playback.IsPaused && Mathf.Abs(radio.Playback.SegmentElapsed - pausedAudio) < 0.08f
                        && rig.ViewCamera.transform.position == pausedCamera && focus.IsActive, "Pause failed to freeze focus and broadcast");
                    PauseMenu.Current.Resume();
                    Next();
                }
                else if (step == 4 && !PauseMenu.IsOpen)
                {
                    otherLock = new GameObject("Other input lock").transform;
                    GameplayActionLocks.Acquire(otherLock, GameplayAction.Input);
                    pausedAudio = radio.Playback.SegmentElapsed;
                    Next();
                }
                else if (step == 5 && elapsed > 0.25)
                {
                    Check(radio.Playback.IsPaused && Mathf.Abs(radio.Playback.SegmentElapsed - pausedAudio) < 0.08f,
                        "Focus incorrectly ignored another system's input lock");
                    GameplayActionLocks.Release(otherLock);
                    Object.Destroy(otherLock.gameObject);
                    Next();
                }
                else if (step == 6 && radio.ReturnJourneyCompleted && !focus.IsActive)
                {
                    VerifyReleased();
                    Capture("radio-restored");
                    Check(radio.Playback.InputLockOwner == null && !radio.TryPlay(), "Completion leaked ownership or replayed");
                    Check(RewindManager.Current.BeginRewind(), "Z did not return after focus");
                    RewindManager.Current.EndRewind();
                    Pass("Outbound broadcast unchanged; return close-up uses a wider 3-grid view, blocks player controls and Z while simulation continues at normal speed, supports pause and other locks, then restores gameplay camera");
                    radio.ResetPlaybackHistory();
                    Check(focus.TryActivate(), "Could not restart interrupted-case fixture");
                    Next();
                }
                else if (step == 7 && elapsed > 0.15)
                {
                    radio.Playback.Stop();
                    Next();
                }
                else if (step == 8 && !focus.IsActive)
                {
                    VerifyReleased();
                    Check(!radio.ReturnJourneyCompleted && focus.TryActivate(), "Interrupted broadcast could not retry");
                    Next();
                }
                else if (step == 9 && elapsed > 0.15)
                {
                    focus.enabled = false;
                    VerifyReleased();
                    Check(!radio.Playback.IsPlaying && !radio.ReturnJourneyCompleted && radio.Playback.InputLockOwner == null,
                        "Disabling focus left audio or a lock behind");
                    focus.enabled = true;
                    VoiceSequence valid = radio.ReturnJourney;
                    radio.ReturnJourney = ScriptableObject.CreateInstance<VoiceSequence>();
                    Check(!focus.TryActivate() && !focus.IsActive, "Unconfigured radio locked the player");
                    radio.ReturnJourney = valid;
                    otherLock = new GameObject("Other camera owner").transform;
                    Check(rig.TryAcquireControl(otherLock) && focus.TryActivate() && !focus.IsActive,
                        "Focus stole another camera owner's control");
                    Check(rig.HasExternalControl && !WorldMapProgression.InputBlocked, "Busy camera fallback leaked movement lock");
                    radio.Playback.Stop();
                    rig.ReleaseControl(otherLock);
                    Object.Destroy(otherLock.gameObject);
                    Pass("Interruption retries safely; disabling restores immediately; invalid audio and occupied camera never strand player controls");
                    Finish(null);
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private static void VerifyReleased()
        {
            Check(!focus.IsActive && !rig.HasExternalControl && !WorldMapProgression.InputBlocked
                && !GameplayActionLocks.IsLocked(GameplayAction.Rewind, radio.gameObject.scene), "Focus leaked camera or action ownership");
            var view = rig.ViewCamera;
            Check(Vector3.Distance(view.transform.position, initial.Position) < 0.02f
                && Quaternion.Angle(view.transform.rotation, initial.Rotation) < 0.01f
                && Mathf.Abs(view.fieldOfView - initial.FieldOfView) < 0.02f
                && view.usePhysicalProperties == initial.Physical, "Focus did not restore the original camera");
        }
        private static void Capture(string label)
        {
            Camera camera = rig.ViewCamera;
            var target = new RenderTexture(1280, 720, 24);
            target.Create();
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Output), label + ".png"), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(image);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
        private static void Next() { step++; since = EditorApplication.timeSinceStartup; }
        private static void Check(bool condition, string text) { if (!condition) throw new Exception(text); }
        private static void Pass(string text) { File.AppendAllText(Output, "PASS: " + text + "\n"); Debug.Log("RADIO FOCUS PASS: " + text); }
        private static void Finish(Exception error)
        {
            if (error != null) { File.AppendAllText(Output, "FAIL step " + step + ": " + error + "\n"); Debug.LogException(error); }
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
