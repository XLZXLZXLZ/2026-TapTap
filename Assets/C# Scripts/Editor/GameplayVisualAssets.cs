using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class GameplayVisualAssets
    {
        public const string VisualConfigPath = "Assets/Runtime/Config/PrototypeVisuals.asset";
        public const string PhasePrefabPath = "Assets/Prefabs/Terrain/Grid/GridPhaseBlock.prefab";
        public const string InversePhasePrefabPath = "Assets/Prefabs/Terrain/Grid/GridInversePhaseBlock.prefab";
        public const string SwitchPrefabPath = "Assets/Prefabs/Interactables/Grid/GridMechanismSwitch.prefab";
        public const string ShowcaseLayoutPath = "Assets/Runtime/Levels/Level_Mechanisms.asset";

        public static PlayerVisualConfig EnsureVisualSettings()
        {
            LevelPrefabBuilder.EnsureFolder("Assets/Art/Sprite/Prototype");
            LevelPrefabBuilder.EnsureFolder("Assets/Art/Materials/Prototype");
            LevelPrefabBuilder.EnsureFolder("Assets/Runtime/Config");
            const string spritePath = "Assets/Art/Sprite/Prototype/Dot.png";
            if (!File.Exists(spritePath))
            {
                var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        float radius = Vector2.Distance(new Vector2((x + 0.5f) / 64f, (y + 0.5f) / 64f), Vector2.one * 0.5f);
                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((0.48f - radius) * 64f)));
                    }
                texture.Apply();
                File.WriteAllBytes(spritePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(spritePath, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(spritePath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = 64f;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            Sprite dot = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            Material lines = EnsureMaterial("Lines", null);
            Material particles = EnsureMaterial("Particles", dot.texture);
            var settings = AssetDatabase.LoadAssetAtPath<PlayerVisualConfig>(VisualConfigPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PlayerVisualConfig>();
                settings.DotSprite = dot; settings.EffectsMaterial = lines; settings.ParticleMaterial = particles;
                AssetDatabase.CreateAsset(settings, VisualConfigPath);
            }
            return settings;
        }

        private static Material EnsureMaterial(string name, Texture texture)
        {
            string path = "Assets/Art/Materials/Prototype/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Sprites/Default")) { name = name, mainTexture = texture };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        public static void ConfigurePlayerVisuals(GameObject root)
        {
            PlayerVisualConfig settings = EnsureVisualSettings();
            root.GetComponent<PlayerView>()?.ConfigureVisuals(settings);
            PlayerEffectsPrefabs.BindPlayer(root, settings);
        }

        public static void EnsureMechanismPrefabs()
        {
            LevelPrefabBuilder.EnsureFolder("Assets/Prefabs/Terrain/Grid");
            LevelPrefabBuilder.EnsureFolder("Assets/Prefabs/Interactables/Grid");
            Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Sprite/Prototype/Square.png");
            Sprite rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Sprite/Prototype/Rounded.png");
            if (square == null || rounded == null) throw new System.InvalidOperationException("缺少基础 Square/Rounded 精灵资源。");
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PhasePrefabPath) == null)
                {
                    var root = new GameObject("GridPhaseBlock");
                    SceneManager.MoveGameObjectToScene(root, preview);
                    root.AddComponent<BoxCollider2D>().size = Vector2.one;
                    root.AddComponent<WorldSurface>().Configure(SurfaceKind.Solid, 0f);
                    SpriteRenderer fill = Sprite(root.transform, "Phase fill", square, Vector2.zero, Vector2.one, new Color(0.4f, 0.76f, 0.94f, 0.12f));
                    root.AddComponent<PhaseBlock>().ConfigureVisual(fill);
                    PrefabUtility.SaveAsPrefabAsset(root, PhasePrefabPath);
                    Object.DestroyImmediate(root);
                }
                if (AssetDatabase.LoadAssetAtPath<GameObject>(InversePhasePrefabPath) == null)
                {
                    var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PhasePrefabPath), preview);
                    root.name = "GridInversePhaseBlock";
                    root.GetComponent<PhaseBlock>().SolidWhenActive = false;
                    root.GetComponentInChildren<SpriteRenderer>().color = new Color(0.4f, 0.76f, 0.94f, 0.85f);
                    LevelPrefabBuilder.RecordInstanceOverrides(root);
                    PrefabUtility.SaveAsPrefabAsset(root, InversePhasePrefabPath);
                    Object.DestroyImmediate(root);
                }
                if (AssetDatabase.LoadAssetAtPath<GameObject>(SwitchPrefabPath) == null)
                {
                    var root = new GameObject("GridMechanismSwitch");
                    SceneManager.MoveGameObjectToScene(root, preview);
                    var sensor = root.AddComponent<BoxCollider2D>();
                    sensor.isTrigger = true; sensor.size = new Vector2(0.82f, 0.22f); sensor.offset = new Vector2(0f, 0.11f);
                    Sprite(root.transform, "Base", rounded, new Vector2(0f, 0.04f), new Vector2(0.82f, 0.08f), new Color(0.24f, 0.31f, 0.42f));
                    SpriteRenderer cap = Sprite(root.transform, "Button", rounded, new Vector2(0f, 0.13f), new Vector2(0.65f, 0.13f), new Color(0.42f, 0.8f, 0.97f));
                    SpriteRenderer lamp = Sprite(root.transform, "State lamp", EnsureVisualSettings().DotSprite,
                        new Vector2(0f, 0.32f), Vector2.one * 0.16f, new Color(0.7f, 0.95f, 1f));
                    root.AddComponent<MechanismSwitch>().ConfigureVisual(lamp, cap.transform);
                    PrefabUtility.SaveAsPrefabAsset(root, SwitchPrefabPath);
                    Object.DestroyImmediate(root);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static SpriteRenderer Sprite(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 scale, Color color)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = position;
            child.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.color = color; renderer.sortingOrder = 2;
            return renderer;
        }

        [MenuItem("TapTap/Prepare Gameplay Visuals and Mechanisms")]
        public static void PrepareAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            PlayerConfig config = AssetDatabase.LoadAssetAtPath<PlayerConfig>("Assets/Runtime/Config/PrototypePlayer.asset");
            if (config != null && Mathf.Approximately(config.JoinedOffset, 0.9f))
            { config.JoinedOffset = 0.75f; EditorUtility.SetDirty(config); }
            foreach (string path in new[] { "Assets/Prefabs/Characters/Player.prefab", "Assets/Prefabs/Characters/LooseHead.prefab" })
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (MovableEntity entity in root.GetComponentsInChildren<MovableEntity>(true))
                    {
                        float height = entity.Part == EntityPart.Head ? 0.6f : 0.9f;
                        var box = entity.GetComponent<BoxCollider2D>();
                        box.size = new Vector2(box.size.x, height);
                        if (entity.Visual != null)
                        {
                            Vector3 scale = entity.Visual.localScale; scale.y = height;
                            entity.Visual.localScale = scale;
                        }
                    }
                    PlayerController player = root.GetComponent<PlayerController>();
                    if (player != null)
                    {
                        player.Body.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                        player.Head.transform.localPosition = new Vector3(0f, 0.45f + player.Config.JoinedOffset, 0f);
                        ConfigurePlayerVisuals(root);
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (string path in new[] { "Assets/Prefabs/Interactables/Checkpoint.prefab", "Assets/Prefabs/Interactables/Grid/GridCheckpoint.prefab" })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var flag = root.GetComponent<CheckpointFlag>();
                    if (Mathf.Approximately(flag.SpawnOffset.y, 0.4f))
                    { flag.SpawnOffset.y = 0.45f; LevelPrefabBuilder.RecordInstanceOverrides(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            LevelPalette palette = LevelEditorAssets.EnsureDefaults();
            LevelTestSceneBuilder.EnsureRuntimePrefab();
            foreach (string guid in AssetDatabase.FindAssets("t:LevelDefinition", new[] { "Assets/Runtime/Levels" }))
            {
                var layout = AssetDatabase.LoadAssetAtPath<LevelDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                bool changed = false;
                foreach (LevelPlacement item in layout.Placements)
                    if (item.Settings is CheckpointPlacementSettings settings && Mathf.Approximately(settings.SpawnOffset.y, 0.4f))
                    { settings.SpawnOffset.y = 0.45f; changed = true; }
                if (!changed) continue;
                EditorUtility.SetDirty(layout);
                if (layout.OutputPrefab != null) LevelPrefabBuilder.Save(layout, AssetDatabase.GetAssetPath(layout.OutputPrefab));
            }
            AssetDatabase.SaveAssets();
            CreateShowcase(palette);
            Debug.Log("Gameplay visuals and mechanisms prepared successfully.");
        }

        private static void CreateShowcase(LevelPalette palette)
        {
            var layout = AssetDatabase.LoadAssetAtPath<LevelDefinition>(ShowcaseLayoutPath);
            if (layout != null) return;
            layout = ScriptableObject.CreateInstance<LevelDefinition>();
            layout.Palette = palette;
            LevelBrush Find(string name) => AssetDatabase.LoadAssetAtPath<LevelBrush>("Assets/Runtime/LevelDesigner/Brushes/" + name + ".asset");
            void Put(int x, int y, string name)
            {
                LevelBrush brush = Find(name);
                layout.Put(new Vector2Int(x, y), brush, LevelBrushHandlers.Get(brush.HandlerId).CreateSettings(brush));
            }
            for (int x = 0; x < 32; x++) Put(x, 0, "Terrain");
            Put(2, 1, "Checkpoint"); layout.EntryPlacementId = layout.Find(new Vector2Int(2, 1), "markers").Id;
            Put(5, 1, "MechanismSwitch"); Put(24, 1, "MechanismSwitch");
            for (int x = 8; x <= 12; x++) Put(x, 3, "PhaseBlock");
            for (int y = 1; y <= 2; y++) Put(12, y, "PhaseBlock");
            for (int x = 16; x <= 20; x++) Put(x, 4, "InversePhaseBlock");
            for (int x = 21; x <= 24; x++) Put(x, 3, "Platform");
            Put(28, 1, "Spikes"); Put(29, 1, "Spikes");
            AssetDatabase.CreateAsset(layout, ShowcaseLayoutPath);
            LevelTestSceneBuilder.Generate(layout, false);
            AssetDatabase.SaveAssets();
        }
    }
}
