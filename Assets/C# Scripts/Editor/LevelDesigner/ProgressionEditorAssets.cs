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

        public static void EnsureInPalette(LevelPalette palette)
        {
            if (palette == null) return;
            if (palette.Brushes != null && palette.Brushes.RemoveAll(item => item != null && item.HandlerId == "indicator-light") > 0)
            {
                EditorUtility.SetDirty(palette);
                AssetDatabase.SaveAssetIfDirty(palette);
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(KeyPath) != null) return;
            EnsureMaterial();
            EnsurePrefab<ProgressKey>(KeyPath);
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
