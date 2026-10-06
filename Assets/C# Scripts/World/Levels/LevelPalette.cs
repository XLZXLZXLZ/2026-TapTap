using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    [CreateAssetMenu(menuName = "TapTap/Levels/Palette")]
    public sealed class LevelPalette : ScriptableObject
    {
        public PlayerConfig Config;
        public List<LevelBrush> Brushes = new List<LevelBrush>();

        public float UnitSize => Config != null ? Mathf.Max(0.01f, Config.UnitSize) : 1f;
    }
}
