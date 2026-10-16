using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class ButtonModelAssets
    {
        public const string VisualPath = "Assets/Prefabs/Interactables/Models/Button3D.prefab";
        public const string SwitchPath = "Assets/Prefabs/Interactables/Grid/GridMechanismSwitch.prefab";
        public const float Height = 0.22f;
        public const float Travel = 0.06f;

        [MenuItem("TapTap/美术/配置 Button 按钮模型")]
        public static void InstallModels()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出运行模式。");
            BuildVisual();
            GameObject root = PrefabUtility.LoadPrefabContents(SwitchPath);
            try
            {
                // Retain the old sprite objects so existing references remain valid.
                foreach (string legacy in new[] { "Base", "Button" })
                {
                    Transform child = root.transform.Find(legacy);
                    if (child != null) child.gameObject.SetActive(false);
                }
                Transform previous = root.transform.Find("Button3D");
                if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath), root.scene);
                model.transform.SetParent(root.transform, false);
                SpriteRenderer lamp = root.transform.Find("State lamp")?.GetComponent<SpriteRenderer>();
                if (lamp != null)
                {
                    Vector3 position = lamp.transform.localPosition;
                    position.z = -0.015f;
                    lamp.transform.localPosition = position;
                }
                root.GetComponent<MechanismSwitch>().ConfigureModelVisual(lamp, model.transform.Find("Visual/Cap"), Travel);
                LevelPrefabBuilder.RecordInstanceOverrides(model);
                PrefabUtility.SaveAsPrefabAsset(root, SwitchPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("Button 按钮已配置：宽深 1 格、Z 中心 0.5，顶盖下压 0.06 格并回弹；二维触发区域保持原配置。");
        }

        private static void BuildVisual()
        {
            LevelPrefabBuilder.EnsureFolder("Assets/Prefabs/Interactables/Models");
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("Button3D");
                SceneManager.MoveGameObjectToScene(root, scene);
                Transform visual = Child(root.transform, "Visual");
                visual.localPosition = Vector3.forward * 0.5f;
                Transform baseFit = Child(visual, "Base");
                Transform cap = Child(visual, "Cap");
                Transform topFit = Child(cap, "Model Fit");
                Import("Button_Bottle", baseFit, scene);
                Import("Button_Top", topFit, scene);

                // Fit the two exported parts together so their authored alignment is preserved.
                // The low profile matches the existing 2D sensor's height; only visuals gain depth.
                Bounds bounds = PlayerModelAssets.LocalBounds(visual);
                Vector3 scale = new Vector3(1f / bounds.size.x, Height / bounds.size.y, 1f / bounds.size.z);
                Vector3 offset = -Vector3.Scale(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), scale);
                foreach (Transform fit in new[] { baseFit, topFit })
                {
                    fit.localScale = scale;
                    fit.localPosition = offset;
                }
                PrefabUtility.SaveAsPrefabAsset(root, VisualPath);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static Transform Child(Transform parent, string name)
        {
            var result = new GameObject(name).transform;
            result.SetParent(parent, false);
            return result;
        }

        private static void Import(string name, Transform parent, Scene scene)
        {
            string path = "Assets/Art/3D Resources/" + name + ".fbx";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null) throw new InvalidOperationException("缺少按钮素材：" + path);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            model.transform.SetParent(parent, false);
            LevelPrefabBuilder.RecordInstanceOverrides(model);
        }
    }
}
