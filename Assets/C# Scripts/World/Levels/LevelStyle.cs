using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    [Serializable]
    public sealed class LevelStyleModel
    {
        public string Label;
        public GameObject SourceModel;
        public GameObject Model;
    }

    [Serializable]
    public sealed class LevelStylePrefab
    {
        public string Label;
        public LevelBrush Brush;
        public GameObject Prefab;
    }

    [CreateAssetMenu(menuName = "TapTap/Levels/Style")]
    public sealed class LevelStyle : ScriptableObject
    {
        public string DisplayName;
        public Color PreviewColor = Color.gray;
        [Tooltip("使用这个笔刷的普通砖块由本 style 选择外观；不修改笔刷的碰撞和玩法配置。")]
        public LevelBrush TerrainBrush;
        public GameObject TerrainPrefab;
        [Tooltip("其它物体的预制体槽。四套预设引用同一批现有预制体；修改后重新保存区域 Prefab。")]
        public List<LevelStylePrefab> Prefabs = new List<LevelStylePrefab>();
        [Tooltip("按原模型映射到本 style 的模型。平台单格/左右/中段、传送带和活塞各段可以分别配置；空项沿用原模型。")]
        public List<LevelStyleModel> Models = new List<LevelStyleModel>();

        public GameObject TerrainModel => TerrainPrefab != null ? TerrainPrefab.GetComponent<TerrainTileVisual>()?.DefaultModel : null;
        public bool HasModelOverrides => Models != null && Models.Exists(m => m != null && m.SourceModel != null && m.Model != null && m.Model != m.SourceModel);

        public GameObject ResolveModel(GameObject original)
        {
            if (original != null && Models != null)
                foreach (var item in Models)
                    if (item != null && item.SourceModel == original && item.Model != null) return item.Model;
            return original;
        }

        public GameObject ResolvePrefab(LevelBrush brush)
        {
            if (brush == TerrainBrush && TerrainPrefab != null) return TerrainPrefab;
            if (Prefabs != null)
                foreach (var item in Prefabs)
                    if (item != null && item.Brush == brush && item.Prefab != null) return item.Prefab;
            return brush.Prefab;
        }
        public Color BrushColor(LevelBrush brush) => brush == TerrainBrush ? PreviewColor : brush.Color;
        public static LevelStyle For(Transform item) => item.GetComponentInParent<LevelRegion>(true)?.Source?.Style;
        public static GameObject ModelFor(Transform item, GameObject original) => For(item)?.ResolveModel(original) ?? original;
    }
}
