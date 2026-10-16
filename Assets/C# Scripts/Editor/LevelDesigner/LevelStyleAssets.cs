using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class LevelStyleAssets
    {
        public const string StyleFolder = "Assets/Runtime/LevelStyles";
        public const string OtherStylePath = StyleFolder + "/Region4_Other.asset";
        public const string BrickFolder = "Assets/Prefabs/Terrain/Grid/Styles";
        private const string ModelFolder = BrickFolder + "/Models";
        private const string MaterialFolder = "Assets/Art/Materials/Grid/Styles";
        private static readonly string[] Names = { "Region1_Red", "Region2_Blue", "Region3_Yellow", "Region4_Other" };
        private static readonly string[] Labels = { "一区 · 1–5 · 红", "二区 · 6–12 · 蓝", "三区 · 13–20 · 黄", "其它 · 21及论外 · 灰" };
        private static readonly Color[] Colors =
        {
            new Color(0.80f, 0.31f, 0.32f), new Color(0.30f, 0.54f, 0.83f),
            new Color(0.90f, 0.75f, 0.28f), new Color(0.55f, 0.59f, 0.66f)
        };

        [MenuItem("TapTap/关卡风格/按四区域预设风格")]
        public static void InstallPresets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
            LevelPrefabBuilder.EnsureFolder(StyleFolder);
            LevelPrefabBuilder.EnsureFolder(ModelFolder);
            LevelPrefabBuilder.EnsureFolder(MaterialFolder);
            PrepareCommonTerrain();
            var terrain = AssetDatabase.LoadAssetAtPath<LevelBrush>("Assets/Runtime/LevelDesigner/Brushes/Terrain.asset");
            var styles = new LevelStyle[4];
            for (int i = 0; i < 4; i++)
            {
                string path = StyleFolder + "/" + Names[i] + ".asset";
                styles[i] = AssetDatabase.LoadAssetAtPath<LevelStyle>(path);
                if (styles[i] != null) continue;
                GameObject model = CreateBrickModel(Names[i], Colors[i]);
                GameObject prefab = CreateBrickPrefab(Names[i], model, terrain);
                var style = ScriptableObject.CreateInstance<LevelStyle>();
                style.DisplayName = Labels[i]; style.PreviewColor = Colors[i];
                style.TerrainBrush = terrain; style.TerrainPrefab = prefab;
                AddModelSlots(style);
                AssetDatabase.CreateAsset(style, path);
                styles[i] = style;
            }

            var assignments = new Dictionary<LevelDefinition, LevelStyle>();
            WorldMapDefinition map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
            if (map == null) throw new InvalidOperationException("找不到 World_Character 大地图。");
            foreach (WorldMapModule module in map.OrderedModules())
            {
                int sequence = map.SequenceNumber(module);
                assignments[module.Level] = styles[sequence >= 1 && sequence <= 5 ? 0 : sequence <= 12 && sequence >= 6 ? 1
                    : sequence >= 13 && sequence <= 20 ? 2 : 3];
            }
            foreach (string guid in AssetDatabase.FindAssets("t:LevelDefinition", new[] { "Assets/Runtime/Levels" }))
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                LevelStyle style = assignments.TryGetValue(level, out var assigned) ? assigned : styles[3];
                if (level.Style == style) continue;
                Undo.RecordObject(level, "预设区域风格"); level.Style = style;
                EditorUtility.SetDirty(level); AssetDatabase.SaveAssetIfDirty(level);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Levels" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset.GetComponent<LevelRegion>() == null || asset.GetComponent<LevelStyleView>() != null) continue;
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    root.AddComponent<LevelStyleView>();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
            Debug.Log("已按大地图序号预设四区域风格；其它关卡资源使用第四种风格。现有场景沿原 Prefab 引用应用外观。");
        }

        private static void PrepareCommonTerrain()
        {
            const string path = "Assets/Prefabs/Terrain/SolidBlock.prefab";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset.GetComponent<TerrainTileVisual>() != null) return;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var visual = root.AddComponent<TerrainTileVisual>();
                Transform legacy = root.transform.Find("Visual");
                if (legacy == null) throw new InvalidOperationException("普通砖块缺少 Visual 子物体。");
                visual.LegacyVisual = legacy.gameObject;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static GameObject CreateBrickModel(string name, Color color)
        {
            string path = ModelFolder + "/GridBrickModel_" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var original = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Terrain/Grid/Block_1_Visual.prefab");
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(original, preview);
                root.name = "GridBrickModel_" + name;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    // The region preview replaces only the main-color material. Accent,
                    // trim and all other slots keep their existing shared materials.
                    for (int i = 0; i < Mathf.Min(1, materials.Length); i++)
                    {
                        string materialPath = MaterialFolder + "/" + name + "_" + i + ".mat";
                        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                        if (material == null)
                        {
                            material = new Material(materials[i]); material.name = name + "_" + i;
                            Color tint = color;
                            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
                            if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
                            AssetDatabase.CreateAsset(material, materialPath);
                        }
                        materials[i] = material;
                    }
                    renderer.sharedMaterials = materials;
                }
                LevelPrefabBuilder.RecordInstanceOverrides(root);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static GameObject CreateBrickPrefab(string name, GameObject model, LevelBrush terrain)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(terrain.Prefab, preview);
                root.name = "GridBrick_" + name;
                TerrainTileVisual visual = root.GetComponent<TerrainTileVisual>();
                visual.DefaultModel = model; visual.ApplyModel(model);
                LevelPrefabBuilder.RecordInstanceOverrides(root);
                return PrefabUtility.SaveAsPrefabAsset(root, BrickFolder + "/GridBrick_" + name + ".prefab");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static void AddModelSlots(LevelStyle style)
        {
            var palette = AssetDatabase.LoadAssetAtPath<LevelPalette>(LevelEditorAssets.DefaultPalettePath);
            foreach (LevelBrush brush in palette.Brushes)
                if (brush != null && brush != style.TerrainBrush && brush.Prefab != null)
                    style.Prefabs.Add(new LevelStylePrefab { Label = brush.DisplayName, Brush = brush, Prefab = brush.Prefab });
            foreach (string file in new[] { "Platform", "SquarePlatform" })
            {
                var brush = AssetDatabase.LoadAssetAtPath<LevelBrush>("Assets/Runtime/LevelDesigner/Brushes/" + file + ".asset");
                PlatformTileVisual p = brush.Prefab.GetComponent<PlatformTileVisual>();
                Add(style, file + " / 单格", p.SingleModel); Add(style, file + " / 左端", p.LeftModel);
                Add(style, file + " / 中段", p.MiddleModel); Add(style, file + " / 右端", p.RightModel);
            }
            var conveyor = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Terrain/Grid/GridConveyor.prefab").GetComponent<ConveyorTileVisual>();
            Add(style, "传送带 / 左端", conveyor.LeftModel); Add(style, "传送带 / 中段", conveyor.MiddleModel); Add(style, "传送带 / 右端", conveyor.RightModel);
            var piston = AssetDatabase.LoadAssetAtPath<GameObject>(PistonModelAssets.PistonPath).GetComponent<PhasePiston>();
            Add(style, "活塞 / 基座", piston.BaseModel); Add(style, "活塞 / 主体", piston.BodyModel); Add(style, "活塞 / 薄盖", piston.TipModel);
        }
        private static void Add(LevelStyle style, string label, GameObject model)
        {
            if (model != null) style.Models.Add(new LevelStyleModel { Label = label, SourceModel = model, Model = model });
        }
    }
}
