using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class UrpTrialSetup
{
    private const string Root = "Assets/Rendering";

    // Explicit command only: opening the project never converts or overwrites assets.
    [MenuItem("Tools/TapTap/URP Trial/Apply Pipeline and Convert Materials")]
    public static void Apply()
    {
        // URP 12's batch importer otherwise starts at -1 and indexes its upgrader array at -1.
        var settingsType = typeof(StandardUpgrader).Assembly.GetType("UnityEditor.Rendering.Universal.UniversalProjectSettings", true);
        var version = settingsType.GetProperty("materialVersionForUpgrade", BindingFlags.Public | BindingFlags.Static);
        if ((int)version.GetValue(null) < 0)
        {
            version.SetValue(null, 0);
            settingsType.GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        }
        EnsureFolder(Root + "/Settings");
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root + "/Settings/TapTapRenderer.asset");
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            renderer.name = "TapTapRenderer";
            AssetDatabase.CreateAsset(renderer, Root + "/Settings/TapTapRenderer.asset");
            var feature = ScriptableObject.CreateInstance<TapTapScreenEffectsFeature>();
            feature.name = "TapTap Screen Effects";
            AssetDatabase.AddObjectToAsset(feature, renderer);
            renderer.rendererFeatures.Add(feature);
            renderer.SetDirty();
            EditorUtility.SetDirty(renderer);
        }
        ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
        var rendererSerialized = new SerializedObject(renderer);
        var featureMap = rendererSerialized.FindProperty("m_RendererFeatureMap");
        featureMap.arraySize = renderer.rendererFeatures.Count;
        for (int i = 0; i < renderer.rendererFeatures.Count; i++)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out string guid, out long id);
            featureMap.GetArrayElementAtIndex(i).longValue = id;
        }
        rendererSerialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(renderer);
        var low = Profile("TapTapLow", renderer, false, false, 15f, 1, 512);
        var medium = Profile("TapTapMedium", renderer, true, false, 20f, 1, 1024);
        var high = Profile("TapTapHigh", renderer, true, true, 150f, 2, 2048);
        GraphicsSettings.renderPipelineAsset = high;
        int currentQuality = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = i < 2 ? low : i == 2 ? medium : high;
        }
        QualitySettings.SetQualityLevel(currentQuality, false);

        int converted = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(asset is Material material) || material.shader == null) continue;
                string shader = material.shader.name;
                if (shader == "Standard" || shader == "Standard (Specular setup)")
                    MaterialUpgrader.Upgrade(material, new StandardUpgrader(shader), MaterialUpgrader.UpgradeFlags.None);
                else if (shader == "Unlit/Color" || shader == "Unlit/Texture" || shader == "Unlit/Transparent")
                {
                    var upgrader = new MaterialUpgrader();
                    upgrader.RenameShader(shader, "Universal Render Pipeline/Unlit");
                    upgrader.RenameColor("_Color", "_BaseColor");
                    upgrader.RenameTexture("_MainTex", "_BaseMap");
                    MaterialUpgrader.Upgrade(material, upgrader, MaterialUpgrader.UpgradeFlags.None);
                    if (shader == "Unlit/Transparent")
                    {
                        material.SetFloat("_Surface", 1f);
                        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                        material.SetFloat("_ZWrite", 0f);
                        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        material.renderQueue = (int)RenderQueue.Transparent;
                    }
                }
                else if (shader == "Sprites/Default")
                    material.shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                else continue;
                EditorUtility.SetDirty(material);
                converted++;
                Debug.Log("URP material converted: " + path + " (" + shader + ")");
            }
        }
        AssetDatabase.SaveAssets();
        // Finish URP's versioned material upgrades, including smoothness and shader keywords.
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset == null || asset.GetType().Name != "AssetVersion") continue;
                var serializedVersion = new SerializedObject(asset);
                var field = serializedVersion.FindProperty("version");
                if (field.intValue >= 0) continue;
                field.intValue = 0;
                serializedVersion.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
            }
            if (path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        var postprocessor = typeof(StandardUpgrader).Assembly.GetType("UnityEditor.Rendering.Universal.MaterialPostprocessor", true);
        postprocessor.GetMethod("SaveAssetsToDisk", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        AssetDatabase.SaveAssets();
        Debug.Log("URP_TRIAL_SETUP_OK: " + converted + " materials; all quality levels assigned.");
    }

    public static void ApplyBatch()
    {
        try { Apply(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static UniversalRenderPipelineAsset Profile(string name, UniversalRendererData renderer,
        bool shadows, bool soft, float distance, int msaa, int resolution)
    {
        string path = Root + "/Settings/" + name + ".asset";
        var profile = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
        if (profile != null) return profile;
        profile = UniversalRenderPipelineAsset.Create(renderer);
        profile.name = name;
        profile.supportsHDR = false;
        profile.msaaSampleCount = msaa;
        profile.shadowDistance = distance;
        profile.maxAdditionalLightsCount = 4;
        profile.useSRPBatcher = true;
        profile.supportsCameraDepthTexture = false;
        profile.supportsCameraOpaqueTexture = false;
        var serialized = new SerializedObject(profile);
        serialized.FindProperty("m_MainLightShadowsSupported").boolValue = shadows;
        serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue = shadows;
        serialized.FindProperty("m_SoftShadowsSupported").boolValue = soft;
        serialized.FindProperty("m_MainLightShadowmapResolution").intValue = resolution;
        serialized.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = resolution;
        serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
        serialized.FindProperty("m_ShadowCascadeCount").intValue = shadows && soft ? 4 : 1;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.CreateAsset(profile, path);
        return profile;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }
}
