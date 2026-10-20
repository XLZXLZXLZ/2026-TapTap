using System;
using UnityEngine;

namespace TapTap
{
    public enum MusicTrack { Menu, Region1, Region2, Region3, KeyEscape, Ending }

    [Serializable]
    public sealed class RegionMusic
    {
        public LevelStyle Style;
        public MusicTrack Track;
    }

    [CreateAssetMenu(menuName = "TapTap/Audio/Background Music Library")]
    public sealed class BackgroundMusicLibrary : ScriptableObject
    {
        public const string ResourcePath = "Audio/BackgroundMusicLibrary";

        [Header("循环音乐")]
        [Tooltip("主菜单与选关界面；为空时使用旧音效库的 BackgroundMusic。")]
        public AudioClip BGM_0;
        public AudioClip BGM_1;
        public AudioClip BGM_2;
        public AudioClip BGM_3;
        [Tooltip("拾取钥匙后持续播放，直至进入 Ending。")]
        public AudioClip BGM_4;
        public AudioClip BGM_5;

        [Header("关卡分区（引用现有 LevelStyle）")]
        public RegionMusic[] Regions = Array.Empty<RegionMusic>();
        [Min(1), Tooltip("此关在拾取钥匙前保持安静，优先于分区音乐。")]
        public int SilentLevelNumber = 20;

        [Header("切换时间（秒）")]
        [Min(0f)] public float FadeInDuration = 1f;
        [Min(0f)] public float FadeOutDuration = 1f;
        [Min(0f), Tooltip("结局广播成功开始后，等待此时间再淡入 BGM_5。")]
        public float EndingMusicDelay = 1f;

        public AudioClip ClipFor(MusicTrack track)
        {
            switch (track)
            {
                case MusicTrack.Menu: return BGM_0;
                case MusicTrack.Region1: return BGM_1;
                case MusicTrack.Region2: return BGM_2;
                case MusicTrack.Region3: return BGM_3;
                case MusicTrack.KeyEscape: return BGM_4;
                case MusicTrack.Ending: return BGM_5;
                default: return null;
            }
        }

        public MusicTrack? TrackFor(LevelStyle style, int sequenceNumber)
        {
            if (sequenceNumber == SilentLevelNumber || style == null) return null;
            if (Regions != null)
                foreach (RegionMusic region in Regions)
                    if (region != null && region.Style == style) return region.Track;
            return null;
        }
    }
}
