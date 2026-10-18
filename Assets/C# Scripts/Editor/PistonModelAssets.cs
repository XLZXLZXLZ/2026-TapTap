using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TapTap.Editor
{
    public static class PistonModelAssets
    {
        public const string PistonPath = "Assets/Prefabs/Terrain/Grid/GridPiston.prefab";
        public const string ModelFolder = "Assets/Prefabs/Terrain/Grid/SmartModels";
        public const float CoverThickness = 0.12f;

        [MenuItem("TapTap/维护/素材装配/配置 Telescopic 活塞模型")]
        public static void InstallModels()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出运行模式。");
            // Fit wrappers preserve the imported FBX transforms and shared source materials.
            GameObject baseModel = Fit("Telescopic_Base", "TelescopicBase", new Vector3(0.14f, 1f, 1f), -0.07f);
            GameObject bodyModel = Fit("Telescopic_Cube", "TelescopicBody", new Vector3(1f, 0.82f, 0.82f), 0f);
            GameObject coverModel = Fit("Telescopic_Cover", "TelescopicCover", Vector3.one, 0.5f);
            GameObject root = PrefabUtility.LoadPrefabContents(PistonPath);
            try
            {
                PhasePiston piston = root.GetComponent<PhasePiston>();
                piston.BaseModel = baseModel;
                piston.BodyModel = bodyModel;
                piston.TipModel = coverModel;
                piston.UseThinCover = true;
                piston.CoverThickness = CoverThickness;
                piston.RebuildVisual();
                PrefabUtility.SaveAsPrefabAsset(root, PistonPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            // Existing levels contain generated visual children: changing the shared slots alone
            // does not replace those saved children. Rebuild only the piston visuals in place.
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Levels" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset.GetComponentInChildren<PhasePiston>(true) == null) continue;
                root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (PhasePiston piston in root.GetComponentsInChildren<PhasePiston>(true))
                    {
                        // Model choices inherit from GridPiston; lengths, directions and polarity
                        // remain exactly as authored in each level.
                        piston.RebuildVisual();
                        LevelPrefabBuilder.RecordInstanceOverrides(piston.gameObject);
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Telescopic 活塞模型配置完成：固定基座、分段主体、固定厚度薄盖；已刷新现有关卡的活塞外观。");
        }

        private static GameObject Fit(string sourceName, string name, Vector3 size, float centerX)
        {
            string sourcePath = "Assets/Art/3D Resources/" + sourceName + ".fbx";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) throw new InvalidOperationException("缺少模型：" + sourcePath);
            LevelPrefabBuilder.EnsureFolder(ModelFolder);
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject(name);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var fit = new GameObject("Model Fit").transform;
                fit.SetParent(root.transform, false);
                var orient = new GameObject("Model Orientation").transform;
                orient.SetParent(fit, false);
                orient.localRotation = Quaternion.Euler(0f, 0f, -90f);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                model.transform.SetParent(orient, false);
                Bounds bounds = PlayerModelAssets.LocalBounds(fit);
                Vector3 scale = new Vector3(size.x / bounds.size.x, size.y / bounds.size.y, size.z / bounds.size.z);
                fit.localScale = scale;
                fit.localPosition = new Vector3(centerX, 0f, 0f) - Vector3.Scale(bounds.center, scale);
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = RenderMaterial(materials[i], sourceName, i);
                    renderer.sharedMaterials = materials;
                }
                LevelPrefabBuilder.RecordInstanceOverrides(model);
                return PrefabUtility.SaveAsPrefabAsset(root, ModelFolder + "/" + name + ".prefab");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static Material RenderMaterial(Material source, string part, int index)
        {
            if (source != null && source.shader != null && source.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal))
                return source;
            string folder = "Assets/Art/Materials/Grid/Telescopic";
            string name = source != null ? source.name.Replace('/', '_').Replace('\\', '_') : "Default_" + index;
            string path = folder + "/" + part + "_" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            LevelPrefabBuilder.EnsureFolder(folder);
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = part + " " + name };
            material.SetColor("_BaseColor", source != null && source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor")
                : source != null && source.HasProperty("_Color") ? source.color : Color.white);
            material.SetTexture("_BaseMap", source != null && source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap")
                : source != null && source.HasProperty("_MainTex") ? source.mainTexture : null);
            material.SetFloat("_Smoothness", 0.25f);
            if (source != null && source.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
                if (source.IsKeywordEnabled("_EMISSION")) material.EnableKeyword("_EMISSION");
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
