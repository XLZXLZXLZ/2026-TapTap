using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Synthetic clips only. Run in a disposable project; never edits production audio or scenes.
    [InitializeOnLoad]
    public static class RadioRegressionTests
    {
        private const string Pending = "TapTap.RadioRegression";
        private static string Output => Path.GetFullPath("Logs/Radio");
        private static RadioReceiver radio;
        private static PlayerController player;
        private static VoiceSubtitlePresenter subtitles;
        private static int step, frame;
        private static double since, deadline;
        private static float pausedAt;
        static RadioRegressionTests()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
                step = 0; frame = -1; deadline = EditorApplication.timeSinceStartup + 80;
                EditorApplication.update += Tick;
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use a disposable project.");
            try
            {
                Directory.CreateDirectory(Output);
                File.WriteAllText(Path.Combine(Output, "results.txt"), "");
                RadioAssets.BuildBatch();
                Check(!AssetDatabase.LoadAssetAtPath<VoiceSequence>(RadioAssets.FirstPath).IsConfigured, "Template should be empty");
                string before = File.ReadAllText(RadioAssets.RadioPath);
                RadioAssets.Build();
                Check(before == File.ReadAllText(RadioAssets.RadioPath), "Builder overwrote existing prefab");
                EditorSceneManager.OpenScene(RadioAssets.DemoPath, OpenSceneMode.Single);
                Object.FindObjectOfType<PlayerInput>().UseKeyboard = false;
                Directory.CreateDirectory("Assets/RadioValidation");
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/RadioValidation/Runtime.unity");
                SessionState.SetBool(Pending, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(error); }
        }

        private static VoiceSequence Sequence(float duration, bool second = false)
        {
            var sequence = ScriptableObject.CreateInstance<VoiceSequence>();
            var clip = AudioClip.Create("Radio validation only", Mathf.RoundToInt(duration * 24000), 1, 24000, false);
            var first = new VoiceSegment { Clip = clip, Chinese = "本台消息，玩具工厂将暂时关闭。", English = "The toy factory will close temporarily.", GapAfter = 0.15f };
            sequence.Segments = second ? new[] { first, new VoiceSegment { Clip = clip,
                Chinese = "部分自动系统仍将维持工作。", English = "Some automated systems will remain active." } } : new[] { first };
            return sequence;
        }

        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
                frame = Time.frameCount;
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Step " + step);
                double elapsed = EditorApplication.timeSinceStartup - since;
                if (step == 0)
                {
                    player = Object.FindObjectOfType<PlayerController>();
                    radio = Object.FindObjectOfType<RadioReceiver>();
                    if (player == null || player.Body?.Motor == null || RewindManager.Current == null) return;
                    RewindManager.Current.UseKeyboard = false;
                    radio.Player = player;
                    radio.FirstEncounter = Sequence(3f, true);
                    radio.ReturnJourney = Sequence(0.65f);
                    Check(SubtitleLanguageSettings.Language == SubtitleLanguage.Chinese, "Default language");
                    Check(RadioReceiver.Attenuation(2, 3, 10) == 1 && RadioReceiver.Attenuation(10, 3, 10) == 0
                        && Mathf.Abs(RadioReceiver.Attenuation(6.5f, 3, 10) - 0.5f) < 0.001f, "Attenuation curve");
                    player.TeleportJoined(new Vector2(0, 0.7f));
                    Next();
                }
                else if (step == 1 && radio.Playback.SegmentElapsed > 0.4f)
                {
                    Check(radio.Playback.IsPlaying, "Body entering square trigger did not start");
                    subtitles = Object.FindObjectOfType<VoiceSubtitlePresenter>();
                    Check(subtitles != null && subtitles.DisplayedText == radio.FirstEncounter.Segments[0].Chinese && subtitles.Group.alpha > 0.9f,
                        "Chinese subtitle/fade: text=" + subtitles?.DisplayedText + "; alpha=" + subtitles?.Group.alpha
                        + "; paused=" + radio.Playback.IsPaused + "; gain=" + radio.Playback.DistanceGain + "; segment=" + radio.Playback.SegmentIndex);
                    Check(subtitles.Label.font.name.Contains("text(SMALL)") && subtitles.Label.color == Color.white
                        && !subtitles.Group.blocksRaycasts && subtitles.Panel.GetComponent<Image>().color.a > 0.4f, "Subtitle styling");
                    Capture("chinese");
                    SubtitleLanguageSettings.SetLanguage(SubtitleLanguage.English);
                    Next();
                }
                else if (step == 2 && elapsed > 0.45)
                {
                    Check(subtitles.DisplayedText == radio.FirstEncounter.Segments[0].English && subtitles.Group.alpha > 0.9f, "Runtime English switch");
                    Capture("english");
                    pausedAt = radio.Playback.SegmentElapsed;
                    Time.timeScale = 0;
                    Next();
                }
                else if (step == 3 && elapsed > 0.35)
                {
                    Check(radio.Playback.IsPaused && Mathf.Abs(radio.Playback.SegmentElapsed - pausedAt) < 0.08f, "Pause advanced segment");
                    Time.timeScale = 1;
                    Check(RewindManager.Current.BeginRewind(), "Could not begin rewind");
                    Next();
                }
                else if (step == 4 && elapsed > 0.15)
                {
                    Check(RewindManager.Rewinding && radio.Playback.IsPaused && !radio.IsReturning, "Z should pause, not select return sequence");
                    RewindManager.Current.EndRewind();
                    player.TeleportJoined(new Vector2(6.5f, 0.7f));
                    Next();
                }
                else if (step == 5 && elapsed > 0.12)
                {
                    Check(radio.Playback.IsPlaying && radio.Playback.DistanceGain > 0.45f && radio.Playback.DistanceGain < 0.51f, "Leaving trigger/range gain");
                    Check(Mathf.Abs(radio.Playback.Source.volume - radio.Playback.DistanceGain *
                        (MenuAudio.Current != null ? MenuAudio.Current.EffectsVolume : PlayerPrefs.GetFloat(MenuAudio.EffectsPreference, 0.7f))) < 0.01f, "Audio gain not applied");
                    player.TeleportJoined(new Vector2(12, 0.7f));
                    Next();
                }
                else if (step == 6 && elapsed > 0.15)
                {
                    Check(!radio.Playback.IsPlaying && !radio.FirstEncounterCompleted, "Interrupted broadcast consumed completion");
                    SubtitleLanguageSettings.SetLanguage(SubtitleLanguage.Chinese);
                    player.TeleportJoined(new Vector2(0, 0.7f));
                    Next();
                }
                else if (step == 7 && elapsed > 0.35)
                {
                    Check(radio.Playback.IsPlaying && radio.Playback.SegmentIndex == 0, "Reentry did not retry");
                    Next();
                }
                else if (step == 8 && radio.Playback.SegmentIndex == 1 && !radio.Playback.IsInGap && radio.Playback.SegmentElapsed > 0.35f)
                {
                    Check(subtitles.DisplayedText == radio.FirstEncounter.Segments[1].Chinese, "Second segment subtitle");
                    Pass("Trigger, bilingual subtitles/fades, pause/Z, distance gain, interruption/reentry and sequential segments");
                    Next();
                }
                else if (step == 9 && radio.FirstEncounterCompleted)
                {
                    Check(!radio.TryPlay(), "Completed outbound replayed");
                    Check(GameStageProgression.ForScene(radio.gameObject.scene).BeginReturn(), "Stage switch");
                    Next();
                }
                else if (step == 10 && elapsed > 0.2)
                {
                    Check(radio.IsReturning && radio.Playback.Sequence == radio.ReturnJourney, "Return did not start inside trigger");
                    Next();
                }
                else if (step == 11 && radio.ReturnJourneyCompleted)
                {
                    Check(!radio.TryPlay() && radio.FirstEncounterCompleted, "Stages should complete once independently");
                    radio.enabled = false; radio.enabled = true;
                    Check(radio.ReturnJourneyCompleted && !radio.TryPlay(), "Enable lost completion");
                    var other = Object.Instantiate(radio.gameObject).GetComponent<RadioReceiver>();
                    other.TriggerAutomatically = false;
                    Check(other.IsReturning && other.TryPlay(), "Late radio stage restoration");
                    radio.ResetPlaybackHistory(); radio.TriggerAutomatically = false;
                    Check(radio.TryPlay() && !other.Playback.IsPlaying && subtitles.Owner == radio.Playback, "Broadcast priority/ownership");
                    radio.Playback.Stop();
                    radio.ReturnJourney = ScriptableObject.CreateInstance<VoiceSequence>();
                    Check(!radio.TryPlay() && !radio.ReturnJourneyCompleted, "Empty sequence consumed playback");
                    Object.Destroy(other.gameObject);
                    Pass("Independent one-shot stages, same-trigger return, late restoration, subtitle ownership and incomplete config rejection");
                    Next();
                }
                else if (step == 12 && elapsed > 0.45)
                {
                    Check(subtitles.Group.alpha == 0 && subtitles.DisplayedText == "", "End subtitle did not fade out");
                    Pass("Clean fade-out; asset builder preserves configured assets; no production voice clips used");
                    Finish(null);
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Capture(string name)
        {
            Camera camera = Object.FindObjectOfType<Camera>();
            var target = new RenderTexture(1280, 720, 24);
            target.Create(); camera.targetTexture = target;
            Canvas canvas = subtitles.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases(); camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(Output, name + ".png"), texture.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
            target.Release(); Object.DestroyImmediate(texture); Object.DestroyImmediate(target);
        }
        private static void Next() { step++; since = EditorApplication.timeSinceStartup; }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Pass(string message) => File.AppendAllText(Path.Combine(Output, "results.txt"), "PASS: " + message + "\n");
        private static void Finish(Exception error)
        {
            Time.timeScale = 1;
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            if (error != null)
            {
                Directory.CreateDirectory(Output);
                File.AppendAllText(Path.Combine(Output, "results.txt"), "FAIL step " + step + ": " + error + "\n");
                Debug.LogException(error);
            }
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
