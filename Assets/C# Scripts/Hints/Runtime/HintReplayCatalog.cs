using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap.Hints
{
    [CreateAssetMenu(menuName = "TapTap/Hints/Replay Catalog")]
    public sealed class HintReplayCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public LevelDefinition Level;
            [Tooltip("通常留空。同一个关卡布局在大地图中重复使用时，可填写 WorldMapModule.Id 单独覆盖。")]
            public string ModuleId;
            public HintReplayClip Clip;
        }
        public List<Entry> Entries = new List<Entry>();

        public HintReplayClip Resolve(LevelDefinition level, string moduleId)
        {
            if (level == null || Entries == null) return null;
            if (!string.IsNullOrEmpty(moduleId))
                foreach (Entry entry in Entries)
                    if (entry != null && entry.Level == level && entry.ModuleId == moduleId) return entry.Clip;
            foreach (Entry entry in Entries)
                if (entry != null && entry.Level == level && string.IsNullOrEmpty(entry.ModuleId)) return entry.Clip;
            return null;
        }
    }
}
