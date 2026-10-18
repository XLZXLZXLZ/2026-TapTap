using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public static class TerrainPaintAssets
    {
        public const string Folder = "Assets/Runtime/TerrainPainting";
        public static readonly string[] VariantLabels = { "微亮", "微暗", "轻微偏色" };

        [Serializable] private sealed class Export { public List<Binding> Bindings = new List<Binding>(); }
        [Serializable] private sealed class Binding
        {
            public string LevelPath, ProfilePath, ProfileGuid, BaseMaterialPath;
            public string[] Materials;
        }

        [MenuItem("TapTap/维护/准备缺失的地形涂装笔刷")]
        public static void PrepareAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
            var export = new Export();
            var maps = AssetDatabase.FindAssets("t:WorldMapDefinition", new[] { "Assets/Runtime/WorldMaps" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(AssetDatabase.GUIDToAssetPath(guid))).ToArray();
            foreach (string guid in AssetDatabase.FindAssets("t:LevelDefinition", new[] { "Assets/Runtime/Levels" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
                string moduleId = maps.SelectMany(map => map.OrderedModules()).FirstOrDefault(module => module.Level == level)?.Id;
                TerrainPaintProfile profile = Prepare(level, moduleId);
                export.Bindings.Add(new Binding
                {
                    LevelPath = path, ProfilePath = AssetDatabase.GetAssetPath(profile),
                    ProfileGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile)),
                    BaseMaterialPath = AssetDatabase.GetAssetPath(profile.BaseMaterial),
                    Materials = profile.Variants.Select(AssetDatabase.GetAssetPath).ToArray()
                });
            }
            TerrainPaintSceneIndex.RefreshLoadedPaint();
            if (Application.isBatchMode)
                File.WriteAllText(Path.Combine(Application.dataPath, "../terrain-paint-assets.json"), JsonUtility.ToJson(export, true));
            Debug.Log("地形涂装笔刷已就绪：" + export.Bindings.Count + " 个关卡，每关 3 种变体。位置：" + Folder);
        }

        public static void PrepareBatch()
        {
            if (!Application.isBatchMode || Directory.Exists(Path.Combine(Application.dataPath, "../.git")))
                throw new InvalidOperationException("Use an isolated batch project.");
            try { PrepareAll(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        public static TerrainPaintProfile Prepare(LevelDefinition level, string previewModuleId = null)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            Material source = FindBaseMaterial(level);
            if (source == null || (!source.HasProperty("_BaseColor") && !source.HasProperty("_Color")))
                throw new InvalidOperationException(level.name + " 的地形缺少可调整颜色的基础材质。");
            string levelPath = AssetDatabase.GetAssetPath(level);
            string name = LevelPrefabBuilder.SafeName(level.name);
            string destination = Folder + "/" + name;
            // Distinguish identically named assets in different folders.
            string profilePath = destination + "/Palette.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TerrainPaintProfile>(profilePath);
            if (existing != null && existing.Source != level)
            {
                destination += "_" + AssetDatabase.AssetPathToGUID(levelPath).Substring(0, 8);
                profilePath = destination + "/Palette.asset";
            }
            LevelPrefabBuilder.EnsureFolder(destination);
            TerrainPaintProfile profile = level.TerrainPaint != null ? level.TerrainPaint
                : AssetDatabase.LoadAssetAtPath<TerrainPaintProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<TerrainPaintProfile>();
                profile.Source = level;
                profile.BaseMaterial = source;
                profile.PreviewModuleId = previewModuleId ?? "";
                AssetDatabase.CreateAsset(profile, profilePath);
            }
            if (profile.Source != level) throw new InvalidOperationException(level.name + " 的涂装配置属于其它关卡。");
            if (profile.BaseMaterial == null) profile.BaseMaterial = source;
            for (int index = 0; index < 3; index++)
            {
                while (profile.Variants.Count <= index) profile.Variants.Add(null);
                if (profile.Variants[index] != null) continue;
                string materialPath = destination + "/" + name + "_" + (index + 1) + "_" + new[] { "Lighter", "Darker", "HueShift" }[index] + ".mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(profile.BaseMaterial) { name = name + " · " + VariantLabels[index] };
                    Color color = VariantColor(ReadColor(profile.BaseMaterial), index);
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                profile.Variants[index] = material;
            }
            if (level.TerrainPaint != profile)
            {
                Undo.RecordObject(level, "绑定地形涂装");
                level.TerrainPaint = profile;
                EditorUtility.SetDirty(level);
                AssetDatabase.SaveAssetIfDirty(level);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }

        public static Color ReadColor(Material material) => material == null ? Color.gray
            : material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor")
            : material.HasProperty("_Color") ? material.GetColor("_Color") : Color.gray;

        public static Color VariantColor(Color source, int index)
        {
            Color.RGBToHSV(source, out float h, out float s, out float v);
            if (index == 0) { s *= 0.95f; v = Mathf.Min(1f, v + 0.06f); }
            else if (index == 1) { s = Mathf.Min(1f, s * 1.02f); v *= 0.88f; }
            else { h = Mathf.Repeat(h + 0.014f, 1f); s *= 0.97f; v = Mathf.Min(1f, v * 1.02f + 0.005f); }
            Color result = Color.HSVToRGB(h, s, v); result.a = source.a;
            return result;
        }

        public static Material FindBaseMaterial(LevelDefinition level)
        {
            GameObject model = level.Style != null ? level.Style.TerrainModel : null;
            if (model == null && level.Palette != null)
                foreach (LevelBrush brush in level.Palette.Brushes)
                {
                    TerrainTileVisual tile = brush != null && brush.Prefab != null ? brush.Prefab.GetComponent<TerrainTileVisual>() : null;
                    if (tile != null) { model = tile.DefaultModel != null ? tile.DefaultModel : tile.LegacyVisual; break; }
                }
            if (model == null) return null;
            var weights = new Dictionary<Material, int>();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null) weights[material] = weights.TryGetValue(material, out int count) ? count + 1 : 1;
            return weights.OrderByDescending(pair => pair.Value).Select(pair => pair.Key).FirstOrDefault();
        }
    }
}
