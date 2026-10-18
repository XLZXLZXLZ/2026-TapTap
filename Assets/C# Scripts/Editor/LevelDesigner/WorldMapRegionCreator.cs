using System;
using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public static class WorldMapRegionCreator
    {
        public static RectInt Rectangle(Vector2Int a, Vector2Int b)
        {
            Vector2Int min = Vector2Int.Min(a, b), max = Vector2Int.Max(a, b);
            return new RectInt(min, max - min + Vector2Int.one);
        }

        public static string Validate(WorldMapDefinition map, RectInt area)
        {
            if (map == null) return "请选择大地图。";
            if (area.width < 1 || area.height < 1 || area.width > 2048 || area.height > 2048) return "新关卡长宽需为 1～2048 格。";
            return null;
        }

        public static WorldMapModule Create(WorldMapDefinition map, RectInt area, LevelPalette palette, string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            string problem = Validate(map, area);
            if (problem != null) throw new InvalidOperationException(problem);
            if (palette == null) throw new InvalidOperationException("请选择新关卡使用的物体配置。");
            if (palette.UnitSize <= 0f || float.IsNaN(palette.UnitSize) || float.IsInfinity(palette.UnitSize))
                throw new InvalidOperationException("物体配置的单位长度必须大于零。");
            string mapPath = AssetDatabase.GetAssetPath(map);
            path = (path ?? "").Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("关卡资源必须保存为 Assets 下的 .asset 文件。");
            LevelPrefabBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.Size = area.size;
            level.Palette = palette;
            AssetDatabase.CreateAsset(level, path);
            AssetDatabase.SaveAssetIfDirty(level);
            level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
            if (!string.IsNullOrEmpty(mapPath)) map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(mapPath);
            Undo.RegisterCompleteObjectUndo(map, "框选创建子关卡");
            var module = new WorldMapModule { Level = level, Position = area.min, Sequence = map.NextSequence() };
            map.Modules.Add(module);
            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssetIfDirty(map);
            LevelLayoutChanges.Notify(level);
            return module;
        }
    }
}
