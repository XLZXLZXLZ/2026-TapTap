using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Runs only in a disposable batch project: validates real imported assets and audio-clock playback.
    [InitializeOnLoad]
    public static class RadioTimelineRegressionTests
    {
        private const string Pending = "TapTap.RadioTimelineRegression";
        private static readonly string Output = Path.GetFullPath("Logs/Radio/TimelineResults.txt");
        private static VoicePlayer player;
        private static VoiceSubtitlePresenter presenter;
        private static VoiceSequence synthetic, m07;
        private static int phase, frame, lastPass;
        private static float pausedAt;
        private static double since, deadline;
        private static bool completed;

        static RadioTimelineRegressionTests()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
                phase = 0; frame = -1; lastPass = -1; completed = false;
                deadline = EditorApplication.timeSinceStartup + 100;
                EditorApplication.update += Tick;
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use a disposable batch project.");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Output));
                File.WriteAllText(Output, "");
                ValidateAssets();
                ValidateBoundaries();
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camera = new GameObject("Timeline Test Camera", typeof(Camera), typeof(AudioListener));
                camera.tag = "MainCamera";
                Directory.CreateDirectory("Assets/RadioValidation");
                EditorSceneManager.SaveScene(scene, "Assets/RadioValidation/Timeline.unity");
                SessionState.SetBool(Pending, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void ValidateAssets()
        {
            int configured = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:VoiceSequence", new[] { RadioAssets.SequenceFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith("M") && !name.StartsWith("H")) continue;
                VoiceSequence sequence = AssetDatabase.LoadAssetAtPath<VoiceSequence>(path);
                Check(sequence != null && sequence.IsConfigured, "Invalid imported sequence: " + name);
                Check(sequence.Segments.Length == (name == "M07_ReturnJourney" ? 5 : 1), "Segment count: " + name);
                foreach (VoiceSegment segment in sequence.Segments)
                {
                    Check(segment.Subtitles != null && segment.Subtitles.Length > 0, "Missing timeline: " + name);
                    foreach (VoiceSubtitleCue cue in segment.Subtitles)
                        Check(!string.IsNullOrWhiteSpace(cue.Chinese) && !string.IsNullOrWhiteSpace(cue.English), "Missing translation: " + name);
                }
                configured++;
            }
            Check(configured == 18, "Expected 18 configured sequences, got " + configured);
            m07 = AssetDatabase.LoadAssetAtPath<VoiceSequence>(RadioAssets.SequenceFolder + "/M07_ReturnJourney.asset");
            Check(m07.Segments[3].Clip == m07.Segments[4].Clip, "M07 final clip must be shared");
            Check(m07.Segments[3].Subtitles[0].Chinese == "拿起钥匙。跟着光走。"
                && m07.Segments[4].Subtitles[0].Chinese == "拿起钥匙。跟着光走。", "M07 final translations");
            Pass("18 real imported sequences, valid bilingual timelines, M07 five passes sharing final clip");
        }

        private static void ValidateBoundaries()
        {
            var clip = AudioClip.Create("Timeline validation", 48000, 1, 24000, false);
            var segment = new VoiceSegment { Clip = clip, Chinese = "旧配置", English = "Legacy" };
            Check(segment.IsConfigured && segment.SubtitleAt(0.5f, SubtitleLanguage.Chinese) == "旧配置", "Legacy compatibility");
            segment.Subtitles = new[] {
                new VoiceSubtitleCue { Start = 0.1f, End = 0.5f, Chinese = "甲", English = "A" },
                new VoiceSubtitleCue { Start = 0.8f, End = 1.2f, English = "Fallback" }
            };
            Check(segment.IsConfigured, "Valid timeline rejected");
            Check(segment.SubtitleAt(0.09f, SubtitleLanguage.Chinese) == ""
                && segment.SubtitleAt(0.1f, SubtitleLanguage.Chinese) == "甲"
                && segment.SubtitleAt(0.5f, SubtitleLanguage.Chinese) == ""
                && segment.SubtitleAt(0.8f, SubtitleLanguage.Chinese) == "Fallback"
                && segment.SubtitleAt(1.2f, SubtitleLanguage.Chinese) == "", "Cue boundaries/gaps/fallback");
            segment.Subtitles[1].Start = 0.4f;
            Check(!segment.IsConfigured, "Overlap accepted");
            segment.Subtitles[1].Start = -0.1f;
            Check(!segment.IsConfigured, "Negative/unsorted start accepted");
            segment.Subtitles[1].Start = 0.8f;
            segment.Subtitles[1].End = 2.1f;
            Check(!segment.IsConfigured, "Cue beyond clip accepted");
            segment.Subtitles[1].End = float.NaN;
            Check(!segment.IsConfigured, "Nonfinite cue accepted");
            Object.DestroyImmediate(clip);
            Pass("Half-open cue boundaries, silent gaps, language fallback, legacy data, malformed timeline rejection");
        }

        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
                frame = Time.frameCount;
                Check(EditorApplication.timeSinceStartup < deadline, "Timeline test timed out at phase " + phase);
                double wall = EditorApplication.timeSinceStartup - since;
                if (phase == 0)
                {
                    player = new GameObject("Timeline Voice Player").AddComponent<VoicePlayer>();
                    player.SetDistanceGain(1f);
                    synthetic = ScriptableObject.CreateInstance<VoiceSequence>();
                    var clip = AudioClip.Create("Timeline clock", 24000 * 4, 1, 24000, false);
                    synthetic.Segments = new[] { new VoiceSegment { Clip = clip, Subtitles = new[] {
                        new VoiceSubtitleCue { Start = 0.1f, End = 0.9f, Chinese = "第一条字幕", English = "First cue" },
                        new VoiceSubtitleCue { Start = 1.4f, End = 2.2f, Chinese = "第二条字幕", English = "Second cue" },
                        new VoiceSubtitleCue { Start = 2.2f, End = 3.5f, Chinese = "第三条字幕", English = "Third cue" }
                    } } };
                    Check(player.Play(synthetic), "Timeline play failed");
                    presenter = Object.FindObjectOfType<VoiceSubtitlePresenter>();
                    Check(presenter != null, "No subtitle presenter");
                    Next();
                }
                else if (phase == 1 && player.SegmentElapsed > 0.35f)
                {
                    Check(player.CurrentSubtitle == "第一条字幕" && presenter.DisplayedText == "第一条字幕", "First Chinese cue");
                    pausedAt = player.SegmentElapsed;
                    Time.timeScale = 0;
                    Next();
                }
                else if (phase == 2 && wall > 0.35)
                {
                    Check(player.IsPaused && Mathf.Abs(player.SegmentElapsed - pausedAt) < 0.08f, "Paused timeline advanced");
                    SubtitleLanguageSettings.SetLanguage(SubtitleLanguage.English);
                    Time.timeScale = 0.1f;
                    Next();
                }
                else if (phase == 3 && wall > 0.2)
                {
                    Check(player.CurrentSubtitle == "First cue" && presenter.DisplayedText == "First cue", "Resume/language switch");
                    Check(player.SegmentElapsed - pausedAt > 0.12f, "Slow motion slowed voice");
                    Next();
                }
                else if (phase == 4 && player.SegmentElapsed > 1.15f)
                {
                    Check(!player.HasSubtitle && presenter.Group.alpha < 0.1f, "Subtitle not cleared in cue gap");
                    Next();
                }
                else if (phase == 5 && player.SegmentElapsed > 1.75f)
                {
                    Check(player.CurrentSubtitle == "Second cue" && presenter.DisplayedText == "Second cue", "Second timed cue");
                    Check(player.SegmentIndex == 0 && player.Source.clip == synthetic.Segments[0].Clip, "Cue change restarted audio");
                    Check(Mathf.Abs(player.SegmentElapsed - player.Source.timeSamples / (float)player.Source.clip.frequency) < 0.1f, "Sample clock drift");
                    SubtitleLanguageSettings.SetLanguage(SubtitleLanguage.Chinese);
                    Next();
                }
                else if (phase == 6 && player.SegmentElapsed > 2.35f)
                {
                    Check(player.CurrentSubtitle == "第三条字幕" && presenter.DisplayedText == "第三条字幕", "Adjacent cue / immediate replacement");
                    player.SetDistanceGain(0);
                    Next();
                }
                else if (phase == 7 && wall > 0.25)
                {
                    Check(!player.HasSubtitle && player.IsPlaying, "Distance should hide subtitles without stopping voice");
                    player.Stop();
                    Check(!player.IsPlaying && player.CurrentSubtitle == "", "Interruption left stale timeline");
                    Time.timeScale = 1;
                    Pass("Actual audio sample clock, pause/resume, slow motion, timed gaps, adjacent cues, runtime language, mute/interruption");
                    m07 = AssetDatabase.LoadAssetAtPath<VoiceSequence>(RadioAssets.SequenceFolder + "/M07_ReturnJourney.asset");
                    player.Ended += (_, __, reason) => { if (reason == VoiceStopReason.Completed) completed = true; };
                    player.SetDistanceGain(1);
                    Check(player.Play(m07), "M07 play failed");
                    Next();
                }
                else if (phase == 8)
                {
                    if (player.IsPlaying && !player.IsInGap && player.SegmentElapsed > 0.65f && player.SegmentIndex != lastPass)
                    {
                        int index = player.SegmentIndex;
                        Check(index == lastPass + 1, "M07 skipped a pass");
                        Check(player.CurrentSubtitle == m07.Segments[index].Subtitles[0].Chinese, "M07 caption " + index);
                        Check(presenter.DisplayedText == player.CurrentSubtitle, "M07 visible Chinese caption " + index);
                        lastPass = index;
                        Pass("M07 pass " + (index + 1) + ": " + player.CurrentSubtitle);
                    }
                    if (completed)
                    {
                        Check(lastPass == 4 && !player.IsPlaying, "M07 did not complete all five passes");
                        Next();
                    }
                }
                else if (phase == 9 && wall > 0.4)
                {
                    Check(presenter.DisplayedText == "" && presenter.Group.alpha == 0, "Completed timeline left subtitle visible");
                    Pass("Five real M07 passes completed with clean subtitle release");
                    Finish(null);
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Next() { phase++; since = EditorApplication.timeSinceStartup; }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Pass(string message) => File.AppendAllText(Output, "PASS: " + message + "\n");
        private static void Finish(Exception error)
        {
            Time.timeScale = 1;
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            if (error != null) { File.AppendAllText(Output, "FAIL: " + error + "\n"); Debug.LogException(error); }
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
