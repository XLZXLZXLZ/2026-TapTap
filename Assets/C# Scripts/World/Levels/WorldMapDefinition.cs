using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace TapTap
{
    public enum WorldMapTemplateKind { Heart, SplitCharacter }

    [CreateAssetMenu(menuName = "TapTap/Levels/World Map")]
    public sealed class WorldMapDefinition : ScriptableObject
    {
        public WorldMapTemplateKind Template;
        [FormerlySerializedAs("HeartSize")]
        public Vector2Int TemplateSize = new Vector2Int(320, 288);
        [Range(0.005f, 0.08f)] public float CharacterGapRatio = 0.022f;
        [Range(0.01f, 0.12f)] public float CharacterCornerRatio = 0.055f;
        public int Padding = 8;
        public List<WorldMapModule> Modules = new List<WorldMapModule>();
        public List<WorldMapPassage> Passages = new List<WorldMapPassage>();
        public GameObject OutputPrefab;
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

        public Vector2Int Size => TemplateSize + Vector2Int.one * (Padding * 2);
        public string TemplateName => Template == WorldMapTemplateKind.SplitCharacter ? "分体角色" : "爱心";
        public static Vector2Int DefaultSize(WorldMapTemplateKind kind) => kind == WorldMapTemplateKind.SplitCharacter
            ? new Vector2Int(320, 448) : new Vector2Int(320, 288);
        public WorldMapModule FindModule(string id) => string.IsNullOrEmpty(id) ? null : Modules?.Find(module => module != null && module.Id == id);

        private void OnValidate()
        {
            TemplateSize = new Vector2Int(Mathf.Clamp(TemplateSize.x, 8, 2048), Mathf.Clamp(TemplateSize.y, 8, 2048));
            CharacterGapRatio = Mathf.Clamp(CharacterGapRatio, 0.005f, 0.08f);
            CharacterCornerRatio = Mathf.Clamp(CharacterCornerRatio, 0.01f, 0.12f);
            Padding = Mathf.Clamp(Padding, 0, 64);
        }
    }

    [Serializable]
    public sealed class WorldMapPassage
    {
        public string Id = Guid.NewGuid().ToString("N");
        public Vector2Int Position;
        // Retain earlier bindings in saved maps; arrows no longer require linked modules.
        [HideInInspector] public string FromModuleId = "";
        [HideInInspector] public string ToModuleId = "";
        [Range(0f, 360f)] public float Angle;
        public bool Bidirectional;
        public string Note = "";
        public Vector2 ArrowDirection => new Vector2(Mathf.Cos(Angle * Mathf.Deg2Rad), Mathf.Sin(Angle * Mathf.Deg2Rad));
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
