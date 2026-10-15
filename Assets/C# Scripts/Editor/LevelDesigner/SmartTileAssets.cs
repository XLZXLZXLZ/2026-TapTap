using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class SmartTileAssets
    {
        public const string PistonBrushPath = "Assets/Runtime/LevelDesigner/Brushes/Piston.asset";
        private const string Models = "Assets/Prefabs/Terrain/Grid/SmartModels";
        private const string PistonPrefab = "Assets/Prefabs/Terrain/Grid/GridPiston.prefab";

        public static void EnsureInPalette(LevelPalette palette)
        {
            if (palette == null) return;
            LevelPrefabBuilder.EnsureFolder(Models);
            UpgradePlatform("Assets/Runtime/LevelDesigner/Brushes/Platform.asset", false);
            UpgradePlatform("Assets/Runtime/LevelDesigner/Brushes/SquarePlatform.asset", true);
            ConveyorTileAssets.Ensure();
            LevelBrush brush = AssetDatabase.LoadAssetAtPath<LevelBrush>(PistonBrushPath);
            if (brush == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PistonPrefab);
                if (prefab == null)
                {
                    GameObject baseModel = Model("PistonBase", 0.1f, 0.88f, Color.gray, true, true, 0f);
                    GameObject body = Model("PistonBody", 1f, 0.76f, new Color(0.32f, 0.66f, 0.83f), false, false, 0f);
                    GameObject tip = Model("PistonTip", 1f, 0.9f, new Color(0.95f, 0.67f, 0.26f), false, true, 0f);
                    Scene preview = EditorSceneManager.NewPreviewScene();
                    try
                    {
                        var root = new GameObject("GridPiston");
                        SceneManager.MoveGameObjectToScene(root, preview);
                        root.AddComponent<PhaseBlock>();
                        PhasePiston piston = root.AddComponent<PhasePiston>();
                        piston.BaseModel = baseModel; piston.BodyModel = body; piston.TipModel = tip;
                        piston.Configure(0, 3);
                        prefab = PrefabUtility.SaveAsPrefabAsset(root, PistonPrefab);
                    }
                    finally { EditorSceneManager.ClosePreviewScene(preview); }
                }
                brush = ScriptableObject.CreateInstance<LevelBrush>();
                brush.DisplayName = "开关活塞"; brush.HandlerId = "phase-piston"; brush.LayerId = "pistons";
                brush.Prefab = prefab; brush.Color = new Color(0.32f, 0.66f, 0.83f);
                LevelPrefabBuilder.EnsureFolder("Assets/Runtime/LevelDesigner/Brushes");
                AssetDatabase.CreateAsset(brush, PistonBrushPath);
            }
            if (palette.Brushes == null) palette.Brushes = new List<LevelBrush>();
            if (!palette.Brushes.Contains(brush))
            {
                palette.Brushes.Add(brush);
                EditorUtility.SetDirty(palette);
            }
            AssetDatabase.SaveAssetIfDirty(palette);
        }

        private static void UpgradePlatform(string path, bool square)
        {
            LevelBrush brush = AssetDatabase.LoadAssetAtPath<LevelBrush>(path);
            if (brush == null || brush.Prefab == null) return;
            if (brush.Prefab.GetComponent<PlatformTileVisual>() == null)
            {
                float height = square ? 1f : 0.25f, y = square ? 0f : 0.375f;
                string prefix = square ? "SquarePlatform" : "Platform";
                Color color = square ? new Color(0.3f, 0.38f, 0.43f) : new Color(0.22f, 0.69f, 0.6f);
                GameObject single = Model(prefix + "Single", 1f, height, color, true, true, y);
                GameObject left = Model(prefix + "Left", 1f, height, color, true, false, y);
                GameObject middle = Model(prefix + "Middle", 1f, height, color, false, false, y);
                GameObject right = Model(prefix + "Right", 1f, height, color, false, true, y);
                string prefabPath = AssetDatabase.GetAssetPath(brush.Prefab);
                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var visual = root.AddComponent<PlatformTileVisual>();
                    visual.ConnectionGroup = square ? "square-platform" : "thin-platform";
                    visual.SingleModel = single; visual.LeftModel = left; visual.MiddleModel = middle; visual.RightModel = right;
                    var legacy = new List<GameObject>();
                    foreach (Transform child in root.transform)
                        if (child.GetComponentInChildren<Renderer>(true) != null) legacy.Add(child.gameObject);
                    visual.LegacyVisuals = legacy.ToArray();
                    visual.ApplyPart(PlatformPart.Single);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            if (brush.HandlerId == "default")
            {
                brush.HandlerId = "smart-platform";
                EditorUtility.SetDirty(brush);
                AssetDatabase.SaveAssetIfDirty(brush);
            }
        }

        private static GameObject Model(string name, float width, float height, Color color, bool left, bool right, float y)
        {
            string path = Models + "/" + name + ".prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject(name);
                SceneManager.MoveGameObjectToScene(root, preview);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Sprite/Prototype/Square.png");
                Sprite(root.transform, "Body", sprite, new Vector2(0f, y), new Vector2(width, height), color);
                Color edge = Color.Lerp(color, Color.white, 0.55f);
                if (left) Sprite(root.transform, "Left cap", sprite, new Vector2(-width * 0.5f + 0.035f, y), new Vector2(0.07f, height), edge);
                if (right) Sprite(root.transform, "Right cap", sprite, new Vector2(width * 0.5f - 0.035f, y), new Vector2(0.07f, height), edge);
                Sprite(root.transform, "Top edge", sprite, new Vector2(0f, y + height * 0.5f - 0.025f), new Vector2(width, 0.05f), edge);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static void Sprite(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 size, Color color)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = position;
            child.transform.localScale = new Vector3(size.x, size.y, 1f);
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.color = color; renderer.sortingOrder = 3;
        }
    }
}
