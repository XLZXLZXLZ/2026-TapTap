using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class LevelEditorAssets
    {
        public const string DefaultPalettePath = "Assets/Runtime/Config/LevelPalette.asset";
        public const string ExampleLayoutPath = "Assets/Runtime/Levels/Level_Example.asset";
        public const string ExamplePrefabPath = "Assets/Prefabs/Levels/Level_Example.prefab";
        private const string BrushFolder = "Assets/Runtime/LevelDesigner/Brushes";
        private const string TerrainFolder = "Assets/Prefabs/Terrain/Grid";
        private const string MarkerFolder = "Assets/Prefabs/Interactables/Grid";

        public static LevelBrush EnsureSpringBrush()
        {
            LevelPrefabBuilder.EnsureFolder(BrushFolder);
            return EnsureBrush("Spring", "弹簧", "spring", "geometry", GameplayVisualAssets.EnsureSpringPrefab(),
                new Vector2(0.5f, 0.5f), new Rect(0.04f, 0f, 0.92f, 0.7f), new Color(1f, 0.75f, 0.18f));
        }

        public static void EnsureSpringInPalette(LevelPalette palette)
        {
            if (palette == null || palette.Brushes != null && palette.Brushes.Exists(item => item != null && item.HandlerId == "spring")) return;
            LevelBrush brush = EnsureSpringBrush();
            if (palette.Brushes == null) palette.Brushes = new System.Collections.Generic.List<LevelBrush>();
            palette.Brushes.Add(brush);
            EditorUtility.SetDirty(palette);
            AssetDatabase.SaveAssetIfDirty(palette);
        }

        public static LevelBrush EnsureAnnotationBrush()
        {
            LevelPrefabBuilder.EnsureFolder(BrushFolder);
            LevelPrefabBuilder.EnsureFolder(MarkerFolder);
            string path = MarkerFolder + "/GridAnnotation.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                string materialPath = MarkerFolder + "/AnnotationRed.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(Shader.Find("Sprites/Default"));
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                Scene preview = EditorSceneManager.NewPreviewScene();
                try
                {
                    var root = new GameObject("GridAnnotation");
                    SceneManager.MoveGameObjectToScene(root, preview);
                    LevelAnnotation annotation = root.AddComponent<LevelAnnotation>();
                    annotation.TextFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/ResourceHanRoundedCN-Bold.ttf");
                    BoxCollider2D collider = root.GetComponent<BoxCollider2D>();
                    collider.isTrigger = true;
                    collider.size = Vector2.one * 0.8f;
                    var circle = new Vector3[49];
                    for (int i = 0; i < circle.Length; i++)
                    {
                        float angle = i * Mathf.PI * 2f / (circle.Length - 1);
                        circle[i] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.32f;
                    }
                    AnnotationLine(root.transform, "Red circle", circle, 0.055f, material);
                    AnnotationLine(root.transform, "Exclamation stem", new[] { new Vector3(0f, 0.19f), new Vector3(0f, -0.05f) }, 0.07f, material);
                    AnnotationLine(root.transform, "Exclamation dot", new[] { new Vector3(0f, -0.17f), new Vector3(0f, -0.18f) }, 0.08f, material);
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { EditorSceneManager.ClosePreviewScene(preview); }
            }
            return EnsureBrush("Annotation", "测试批注 [N]", "annotation", "annotations", prefab,
                new Vector2(0.5f, 0.5f), new Rect(0.1f, 0.1f, 0.8f, 0.8f), Color.red);
        }

        private static void AnnotationLine(Transform parent, string name, Vector3[] points, float width, Material material)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = material;
            line.startColor = line.endColor = new Color(1f, 0.12f, 0.12f);
            line.startWidth = line.endWidth = width;
            line.numCapVertices = 4;
            line.sortingOrder = 100;
            line.positionCount = points.Length;
            line.SetPositions(points);
        }

        public static void EnsureAnnotationInPalette(LevelPalette palette)
        {
            if (palette == null) return;
            if (palette.Brushes != null && palette.Brushes.Exists(item => item != null && item.HandlerId == "annotation")) return;
            LevelBrush brush = EnsureAnnotationBrush();
            if (palette.Brushes == null) palette.Brushes = new System.Collections.Generic.List<LevelBrush>();
            palette.Brushes.Add(brush);
            EditorUtility.SetDirty(palette);
            AssetDatabase.SaveAssetIfDirty(palette);
        }

        public static LevelBrush EnsureEndpointBrush()
        {
            LevelPrefabBuilder.EnsureFolder(BrushFolder);
            return EnsureBrush("Endpoint", "终点占位", "endpoint", "markers", GameplayVisualAssets.EnsureEndpointPrefab(),
                new Vector2(0.5f, 0f), new Rect(0.1f, 0f, 0.8f, 0.92f), new Color(0.45f, 0.95f, 0.65f));
        }

        [MenuItem("TapTap/Level Designer/Create Endpoint Brush")]
        public static void CreateEndpointAssets()
        {
            LevelBrush brush = EnsureEndpointBrush();
            LevelPalette palette = AssetDatabase.LoadAssetAtPath<LevelPalette>(DefaultPalettePath);
            if (palette != null)
            {
                if (palette.Brushes == null || !palette.Brushes.Contains(brush))
                {
                    Undo.RecordObject(palette, "添加终点笔刷");
                    if (palette.Brushes == null) palette.Brushes = new System.Collections.Generic.List<LevelBrush>();
                    palette.Brushes.Add(brush);
                    EditorUtility.SetDirty(palette);
                    AssetDatabase.SaveAssetIfDirty(palette);
                }
            }
            AssetDatabase.SaveAssets();
            if (!Application.isBatchMode) { Selection.activeObject = brush; EditorGUIUtility.PingObject(brush); }
        }

        public static LevelPalette EnsureDefaults()
        {
            GameplayVisualAssets.EnsureMechanismPrefabs();
            LevelPrefabBuilder.EnsureFolder(BrushFolder);
            LevelPrefabBuilder.EnsureFolder(TerrainFolder);
            LevelPrefabBuilder.EnsureFolder(MarkerFolder);
            LevelPrefabBuilder.EnsureFolder("Assets/Runtime/Config");

            GameObject terrain = EnsureGridPrefab("Terrain", "Assets/Prefabs/Terrain/SolidBlock.prefab", TerrainFolder);
            GameObject platform = EnsureGridPrefab("Platform", "Assets/Prefabs/Terrain/OneWayPlatform.prefab", TerrainFolder);
            GameObject conveyor = EnsureGridPrefab("Conveyor", "Assets/Prefabs/Terrain/Conveyor.prefab", TerrainFolder);
            GameObject spikes = EnsureGridPrefab("Spikes", "Assets/Prefabs/Terrain/Spikes.prefab", TerrainFolder);
            GameObject checkpoint = EnsureGridPrefab("Checkpoint", "Assets/Prefabs/Interactables/Checkpoint.prefab", MarkerFolder);

            LevelBrush[] brushes =
            {
                EnsureBrush("Terrain", "地形", "default", "geometry", terrain,
                    new Vector2(0.5f, 0.5f), new Rect(0f, 0f, 1f, 1f), new Color(0.28f, 0.35f, 0.44f)),
                EnsureBrush("Platform", "平台", "default", "geometry", platform,
                    new Vector2(0.5f, 0.5f), new Rect(0f, 0.75f, 1f, 0.25f), new Color(0.22f, 0.89f, 0.78f)),
                EnsureBrush("Conveyor", "传送带", "conveyor", "geometry", conveyor,
                    new Vector2(0.5f, 0.5f), new Rect(0f, 0f, 1f, 1f), new Color(0.46f, 0.38f, 0.70f)),
                EnsureBrush("Spikes", "尖刺", "default", "geometry", spikes,
                    new Vector2(0.5f, 0.5f), new Rect(0f, 0f, 1f, 1f), new Color(1f, 0.43f, 0.36f)),
                EnsureBrush("Checkpoint", "复活点", "checkpoint", "markers", checkpoint,
                    new Vector2(0.5f, 0f), new Rect(0.25f, 0f, 0.5f, 1f), new Color(1f, 0.94f, 0.79f)),
                EnsureBrush("PhaseBlock", "虚实方块（亮时实）", "phase-block", "geometry",
                    AssetDatabase.LoadAssetAtPath<GameObject>(GameplayVisualAssets.PhasePrefabPath),
                    new Vector2(0.5f, 0.5f), new Rect(0f, 0f, 1f, 1f), new Color(0.4f, 0.76f, 0.94f)),
                EnsureBrush("InversePhaseBlock", "虚实方块（亮时虚）", "phase-block", "geometry",
                    AssetDatabase.LoadAssetAtPath<GameObject>(GameplayVisualAssets.InversePhasePrefabPath),
                    new Vector2(0.5f, 0.5f), new Rect(0f, 0f, 1f, 1f), new Color(0.4f, 0.76f, 0.94f)),
                EnsureBrush("MechanismSwitch", "机关按钮", "default", "markers",
                    AssetDatabase.LoadAssetAtPath<GameObject>(GameplayVisualAssets.SwitchPrefabPath),
                    new Vector2(0.5f, 0f), new Rect(0.1f, 0f, 0.8f, 0.4f), new Color(0.42f, 0.8f, 0.97f)),
                EnsureEndpointBrush(),
                EnsureAnnotationBrush(),
                EnsureSpringBrush()
            };
            LevelPalette palette = AssetDatabase.LoadAssetAtPath<LevelPalette>(DefaultPalettePath);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<LevelPalette>();
                palette.Config = AssetDatabase.LoadAssetAtPath<PlayerConfig>("Assets/Runtime/Config/PrototypePlayer.asset");
                palette.Brushes.AddRange(brushes);
                AssetDatabase.CreateAsset(palette, DefaultPalettePath);
                AssetDatabase.SaveAssetIfDirty(palette);
            }
            else if (palette.Brushes == null || !palette.Brushes.Exists(brush => brush != null))
            {
                Undo.RecordObject(palette, "恢复默认笔刷");
                palette.Brushes = new System.Collections.Generic.List<LevelBrush>(brushes);
                EditorUtility.SetDirty(palette);
                AssetDatabase.SaveAssetIfDirty(palette);
            }
            else
            {
                for (int i = 5; i < brushes.Length; i++)
                    if (!palette.Brushes.Contains(brushes[i])) { palette.Brushes.Add(brushes[i]); EditorUtility.SetDirty(palette); }
                AssetDatabase.SaveAssetIfDirty(palette);
            }
            EnsureAnnotationInPalette(palette);
            EnsureSpringInPalette(palette);
            return palette;
        }

        [MenuItem("TapTap/Level Designer/Create Example Region")]
        public static void CreateStarterAssets()
        {
            LevelPalette palette = EnsureDefaults();
            LevelDefinition layout = AssetDatabase.LoadAssetAtPath<LevelDefinition>(ExampleLayoutPath);
            if (layout == null)
            {
                LevelPrefabBuilder.EnsureFolder("Assets/Runtime/Levels");
                layout = ScriptableObject.CreateInstance<LevelDefinition>();
                layout.Palette = palette;
                layout.Size = new Vector2Int(32, 18);
                LevelBrush terrain = LoadBrush("Terrain");
                LevelBrush platform = LoadBrush("Platform");
                LevelBrush conveyor = LoadBrush("Conveyor");
                LevelBrush spikes = LoadBrush("Spikes");
                LevelBrush checkpoint = LoadBrush("Checkpoint");
                for (int x = 0; x < layout.Size.x; x++)
                    Put(layout, new Vector2Int(x, 0), terrain);
                Put(layout, new Vector2Int(2, 1), checkpoint);
                layout.EntryPlacementId = layout.Find(new Vector2Int(2, 1), "markers").Id;
                for (int x = 6; x <= 10; x++)
                    Put(layout, new Vector2Int(x, 3), platform);
                for (int x = 14; x <= 17; x++)
                    Put(layout, new Vector2Int(x, 2), conveyor);
                for (int x = 22; x <= 24; x++)
                    Put(layout, new Vector2Int(x, 1), spikes);
                for (int x = 21; x <= 25; x++)
                    Put(layout, new Vector2Int(x, 4), platform);
                AssetDatabase.CreateAsset(layout, ExampleLayoutPath);
                AssetDatabase.SaveAssetIfDirty(layout);
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ExamplePrefabPath) == null)
                LevelPrefabBuilder.Save(layout, ExamplePrefabPath);
            if (!Application.isBatchMode)
            {
                Selection.activeObject = layout;
                EditorGUIUtility.PingObject(layout);
            }
            Debug.Log("Level Designer example saved: " + ExampleLayoutPath + " / " + ExamplePrefabPath);
        }

        private static void Put(LevelDefinition layout, Vector2Int cell, LevelBrush brush)
        {
            layout.Put(cell, brush, LevelBrushHandlers.Get(brush.HandlerId).CreateSettings(brush));
        }

        private static LevelBrush LoadBrush(string name) => AssetDatabase.LoadAssetAtPath<LevelBrush>(BrushFolder + "/" + name + ".asset");

        private static LevelBrush EnsureBrush(string name, string displayName, string handlerId, string layerId,
            GameObject prefab, Vector2 anchor, Rect previewRect, Color color)
        {
            string path = BrushFolder + "/" + name + ".asset";
            LevelBrush brush = AssetDatabase.LoadAssetAtPath<LevelBrush>(path);
            if (brush != null)
                return brush;
            brush = ScriptableObject.CreateInstance<LevelBrush>();
            brush.DisplayName = displayName;
            brush.HandlerId = handlerId;
            brush.LayerId = layerId;
            brush.Prefab = prefab;
            brush.Anchor = anchor;
            brush.PreviewRect = previewRect;
            brush.Color = color;
            AssetDatabase.CreateAsset(brush, path);
            AssetDatabase.SaveAssetIfDirty(brush);
            return brush;
        }

        private static GameObject EnsureGridPrefab(string name, string sourcePath, string folder)
        {
            string path = folder + "/Grid" + name + ".prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
                throw new InvalidOperationException("缺少基础资源：" + sourcePath);
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
                root.name = "Grid" + name;
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                BoxCollider2D collider = root.GetComponent<BoxCollider2D>();
                if (name == "Platform")
                {
                    collider.size = new Vector2(1f, 0.25f);
                    collider.offset = new Vector2(0f, 0.375f);
                    Transform surface = root.transform.Find("Surface");
                    if (surface != null)
                    {
                        surface.localPosition = new Vector3(0f, 0.375f, 0f);
                        surface.localScale = new Vector3(1f, 0.25f, 1f);
                    }
                }
                else if (name == "Spikes")
                {
                    collider.size = Vector2.one;
                    collider.offset = Vector2.zero;
                    collider.isTrigger = true;
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (!child.name.StartsWith("Spike ", StringComparison.Ordinal))
                            continue;
                        Vector3 scale = child.localScale;
                        scale.y = 1f;
                        child.localScale = scale;
                    }
                }
                else if (name != "Checkpoint")
                {
                    collider.size = Vector2.one;
                    collider.offset = Vector2.zero;
                }
                LevelPrefabBuilder.RecordInstanceOverrides(root);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (!success || prefab == null)
                    throw new InvalidOperationException("无法保存格子资源：" + path);
                return prefab;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }
    }
}
