using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    [Serializable]
    public sealed class TerrainPaintEntry
    {
        public string ModuleId;
        public string PlacementId;
        public Material Material;
    }

    [CreateAssetMenu(menuName = "TapTap/Levels/Terrain Paint Profile")]
    public sealed class TerrainPaintProfile : ScriptableObject
    {
        public LevelDefinition Source;
        [Tooltip("只替换模型中使用此材质的槽位。")]
        public Material BaseMaterial;
        public List<Material> Variants = new List<Material>();
        [Tooltip("单关测试场景使用此模块的涂装；大地图使用各模块自己的涂装。")]
        public string PreviewModuleId;
        [SerializeField, HideInInspector] private List<TerrainPaintEntry> entries = new List<TerrainPaintEntry>();
        [NonSerialized] private Dictionary<string, TerrainPaintEntry> index;

        public IReadOnlyList<TerrainPaintEntry> Entries => entries;
        public string ModuleKey(string moduleId) => string.IsNullOrEmpty(moduleId) ? PreviewModuleId ?? "" : moduleId;
        private string Key(string moduleId, string placementId) => ModuleKey(moduleId) + ":" + placementId;

        private void EnsureIndex()
        {
            if (index != null) return;
            index = new Dictionary<string, TerrainPaintEntry>(StringComparer.Ordinal);
            foreach (TerrainPaintEntry entry in entries)
                if (entry != null && !string.IsNullOrEmpty(entry.PlacementId))
                    index[Key(entry.ModuleId, entry.PlacementId)] = entry;
        }

        public Material GetPaint(string moduleId, string placementId)
        {
            EnsureIndex();
            return !string.IsNullOrEmpty(placementId) && index.TryGetValue(Key(moduleId, placementId), out TerrainPaintEntry entry)
                ? entry.Material : null;
        }

        public bool SetPaint(string moduleId, string placementId, Material material)
        {
            if (string.IsNullOrEmpty(placementId)) return false;
            if (material == BaseMaterial) material = null;
            EnsureIndex();
            string key = Key(moduleId, placementId);
            index.TryGetValue(key, out TerrainPaintEntry entry);
            if ((entry != null ? entry.Material : null) == material) return false;
            if (material == null)
            {
                entries.Remove(entry);
                index.Remove(key);
            }
            else if (entry != null) entry.Material = material;
            else
            {
                entry = new TerrainPaintEntry { ModuleId = ModuleKey(moduleId), PlacementId = placementId, Material = material };
                entries.Add(entry);
                index.Add(key, entry);
            }
            return true;
        }

        public void CopyModule(string sourceModuleId, string destinationModuleId)
        {
            var source = entries.FindAll(entry => entry != null && ModuleKey(entry.ModuleId) == ModuleKey(sourceModuleId));
            foreach (TerrainPaintEntry entry in source) SetPaint(destinationModuleId, entry.PlacementId, entry.Material);
        }

        public List<TerrainPaintEntry> CapturePlacement(string placementId)
        {
            var result = new List<TerrainPaintEntry>();
            foreach (TerrainPaintEntry entry in entries)
                if (entry != null && entry.PlacementId == placementId)
                    result.Add(new TerrainPaintEntry { ModuleId = entry.ModuleId, PlacementId = entry.PlacementId, Material = entry.Material });
            return result;
        }

        public void InvalidateCache() => index = null;
        private void OnValidate() => InvalidateCache();
        private void OnEnable() => InvalidateCache();
    }
}
