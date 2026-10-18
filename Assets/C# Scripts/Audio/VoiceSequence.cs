using System;
using UnityEngine;

namespace TapTap
{
    [Serializable]
    public sealed class VoiceSubtitleCue
    {
        [Min(0f), Tooltip("相对本段音频开头的秒数；起点包含、终点不包含。")]
        public float Start;
        [Min(0f)] public float End;
        [TextArea(2, 5)] public string Chinese;
        [TextArea(2, 5)] public string English;

        public string Subtitle(SubtitleLanguage language) => VoiceSegment.Localized(Chinese, English, language);
    }

    [Serializable]
    public sealed class VoiceSegment
    {
        [Tooltip("一段完整播报。换字幕不需要剪切音频；不同播放遍次仍可用多个 Segment。")]
        public AudioClip Clip;
        [Tooltip("按开始时间排列，不重叠。留空时兼容旧的整段字幕。")]
        public VoiceSubtitleCue[] Subtitles = new VoiceSubtitleCue[0];
        [Header("旧配置兼容：仅在 Subtitles 为空时使用")]
        [TextArea(2, 5)] public string Chinese;
        [TextArea(2, 5)] public string English;
        [Min(0f), Tooltip("本段结束到下一段开始的间隔，使用真实时间秒。")]
        public float GapAfter = 0.2f;

        public string Subtitle(SubtitleLanguage language)
        {
            return Localized(Chinese, English, language);
        }

        internal static string Localized(string chinese, string english, SubtitleLanguage language)
        {
            string text = language == SubtitleLanguage.Chinese ? chinese : english;
            return !string.IsNullOrWhiteSpace(text) ? text
                : language == SubtitleLanguage.Chinese ? english ?? "" : chinese ?? "";
        }

        public string SubtitleAt(float seconds, SubtitleLanguage language)
        {
            if (Subtitles == null || Subtitles.Length == 0) return Subtitle(language);
            foreach (VoiceSubtitleCue cue in Subtitles)
            {
                if (cue == null) continue;
                if (seconds < cue.Start) break;
                if (seconds >= cue.Start && seconds < cue.End) return cue.Subtitle(language);
            }
            return "";
        }

        public bool IsConfigured
        {
            get
            {
                if (Clip == null || Clip.length <= 0f) return false;
                if (Subtitles == null) return true;
                float previousEnd = 0f;
                foreach (VoiceSubtitleCue cue in Subtitles)
                {
                    if (cue == null || float.IsNaN(cue.Start) || float.IsInfinity(cue.Start)
                        || float.IsNaN(cue.End) || float.IsInfinity(cue.End)
                        || cue.Start < previousEnd || cue.End <= cue.Start || cue.End > Clip.length) return false;
                    previousEnd = cue.End;
                }
                return true;
            }
        }
    }

    [CreateAssetMenu(fileName = "VoiceSequence", menuName = "TapTap/Audio/Voice Sequence")]
    public sealed class VoiceSequence : ScriptableObject
    {
        [Range(0f, 1f)] public float Volume = 1f;
        public VoiceSegment[] Segments = new VoiceSegment[0];

        // An unfinished configuration must not consume a radio's one playback per stage.
        public bool IsConfigured
        {
            get
            {
                if (Segments == null || Segments.Length == 0) return false;
                foreach (VoiceSegment segment in Segments)
                    if (segment == null || !segment.IsConfigured) return false;
                return true;
            }
        }
    }
}
