using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    [CreateAssetMenu(menuName = "TapTap/Levels/World Map")]
    public sealed class WorldMapDefinition : ScriptableObject
    {
        public List<WorldMapModule> Modules = new List<WorldMapModule>();
        [HideInInspector] public string PlaySceneGuid;
        [HideInInspector] public string PlayScenePath;
        [HideInInspector] public string SelectionSceneGuid;
        [HideInInspector] public string SelectionScenePath;
        [HideInInspector] public string ProgressId;

        // Zero is the legacy value: retain list order until a designer assigns numbers.
        public int SequenceNumber(WorldMapModule module) => module.Sequence > 0 ? module.Sequence : Modules.IndexOf(module) + 1;
        public List<WorldMapModule> OrderedModules()
        {
            var ordered = Modules.FindAll(module => module?.Level != null);
            ordered.Sort((a, b) =>
            {
                int order = SequenceNumber(a).CompareTo(SequenceNumber(b));
                return order != 0 ? order : Modules.IndexOf(a).CompareTo(Modules.IndexOf(b));
            });
            return ordered;
        }
        public int NextSequence()
        {
            int last = 0;
            foreach (WorldMapModule module in Modules) if (module != null) last = Mathf.Max(last, SequenceNumber(module));
            return last + 1;
        }

        public RectInt PreviewBounds
        {
            get
            {
                Vector2Int min = Vector2Int.zero, max = Vector2Int.zero;
                bool any = false;
                if (Modules != null)
                    foreach (WorldMapModule module in Modules)
                    {
                        if (module?.Level == null) continue;
                        RectInt bounds = module.Bounds;
                        if (!any) { min = bounds.min; max = bounds.max; any = true; }
                        else { min = Vector2Int.Min(min, bounds.min); max = Vector2Int.Max(max, bounds.max); }
                    }
                return any ? new RectInt(min - Vector2Int.one * 2, max - min + Vector2Int.one * 4)
                    : new RectInt(Vector2Int.zero, new Vector2Int(32, 18));
            }
        }
        public WorldMapModule FindModule(string id) => string.IsNullOrEmpty(id) ? null : Modules?.Find(module => module != null && module.Id == id);

    }

    [Serializable]
    public sealed class WorldMapModule
    {
        public string Id = Guid.NewGuid().ToString("N");
        public LevelDefinition Level;
        public Vector2Int Position;
        public string Label = "";
        [Min(0), Tooltip("关卡推进序号；0 使用原有列表顺序。正数允许不连续，但不能重复。")]
        public int Sequence;

        public RectInt Bounds => Level != null ? new RectInt(Position + Level.Footprint.Bounds.position, Level.Footprint.Bounds.size)
            : new RectInt(Position, Vector2Int.zero);
        public bool Contains(Vector2Int worldCell) => Level != null && Level.Footprint.Contains(worldCell - Position);
        public string DisplayName => !string.IsNullOrWhiteSpace(Label) ? Label : Level != null ? Level.name : "缺少关卡";
    }
}
