using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class ProgressionEditorAssets
    {
        public const string LightPath = "Assets/Prefabs/Interactables/Grid/GridIndicatorLight.prefab";
        public const string KeyPath = "Assets/Prefabs/Interactables/ProgressKey.prefab";
        private const string BrushPath = "Assets/Runtime/LevelDesigner/Brushes/IndicatorLight.asset";

        [MenuItem("TapTap/创建灯光与钥匙配置", false, 130)]
        public static void CreateAssets()
        {
            EnsureInPalette(AssetDatabase.LoadAssetAtPath<LevelPalette>(LevelEditorAssets.DefaultPalettePath));
            EnsurePrefab<ProgressKey>(KeyPath);
        }

        public static void EnsureInPalette(LevelPalette palette)
        {
            if (palette == null) return;
            bool hasLight = palette.Brushes != null && palette.Brushes.Exists(item => item != null && item.HandlerId == "indicator-light");
            if (hasLight && AssetDatabase.LoadAssetAtPath<GameObject>(KeyPath) != null) return;
            EnsureMaterial();
            EnsurePrefab<ProgressKey>(KeyPath);
            if (hasLight) return;
            GameObject prefab = EnsurePrefab<IndicatorLight>(LightPath);
            LevelPrefabBuilder.EnsureFolder("Assets/Runtime/LevelDesigner/Brushes");
            LevelBrush brush = AssetDatabase.LoadAssetAtPath<LevelBrush>(BrushPath);
            if (brush == null)
            {
                brush = ScriptableObject.CreateInstance<LevelBrush>();
                brush.DisplayName = "指示灯光"; brush.HandlerId = "indicator-light"; brush.LayerId = "lights";
                brush.Anchor = new Vector2(0.5f, 0.5f); brush.PreviewRect = new Rect(0.1f, 0.25f, 0.8f, 0.5f);
                brush.Color = new Color(1f, 0.88f, 0.58f); brush.Prefab = prefab;
                AssetDatabase.CreateAsset(brush, BrushPath);
            }
            if (palette.Brushes == null) palette.Brushes = new System.Collections.Generic.List<LevelBrush>();
            palette.Brushes.Add(brush); EditorUtility.SetDirty(palette); AssetDatabase.SaveAssetIfDirty(palette);
        }

        private static void EnsureMaterial()
        {
            const string path = "Assets/Resources/Materials/IndicatorVisual.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            LevelPrefabBuilder.EnsureFolder("Assets/Resources/Materials");
            AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")), path);
        }

        private static GameObject EnsurePrefab<T>(string path) where T : Component
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            EnsureMaterial();
            LevelPrefabBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject(typeof(T).Name);
                SceneManager.MoveGameObjectToScene(root, preview);
                root.AddComponent<T>();
                root.GetComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("Materials/IndicatorVisual");
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
    }
}
