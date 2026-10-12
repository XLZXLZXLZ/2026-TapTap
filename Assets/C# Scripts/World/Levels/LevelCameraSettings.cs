using System;
using UnityEngine;

namespace TapTap
{
    [Serializable]
    public sealed class LevelCameraSettings
    {
        [Tooltip("按地图长宽较大值的一半设置 Size，上限 12 格；超过上限才启用跟随。")]
        public bool AutoSize = true;
        [Tooltip("关闭 Auto Size 时使用的镜头半高（格）。")]
        [Min(0.1f)] public float ViewSize = 9f;
        public bool FollowHorizontal = true;
        public bool FollowVertical = true;
        [Tooltip("镜头相对身体表现位置的偏移（格）。")]
        public Vector2 Offset = new Vector2(0f, 1.5f);
        [Tooltip("缓冲区半宽、半高占整个画面的比例。默认中央 20% 宽、30% 高。")]
        public Vector2 DeadZone = new Vector2(0.1f, 0.15f);
    }
}
