using System;
using System.IO;
using System.Linq;
using TapTap;
using TapTap.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit menu only. All authored outputs stay inside this inspiration pack.
public static class TempInspirationSceneBuilder
{
    private const string Pack = "Assets/Temp/MechanismIdeas_20261012";

    [MenuItem("TapTap/TEMP Inspiration/Generate Selected Test Scenes")]
    private static void GenerateSelected()
    {
        Generate(Selection.objects.OfType<LevelDefinition>().ToArray());
    }

    [MenuItem("TapTap/TEMP Inspiration/Generate All Test Scenes")]
    private static void GenerateAll()
    {
        var layouts = AssetDatabase.FindAssets("t:LevelDefinition", new[] { Pack + "/Layouts" })
            .Select(guid => AssetDatabase.LoadAssetAtPath<LevelDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
            .OrderBy(item => item.name, StringComparer.Ordinal).ToArray();
        Generate(layouts);
    }

    private static void Generate(LevelDefinition[] layouts)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请退出 Play 模式后再生成 TEMP 测试场景。");
        if (layouts.Length == 0)
            throw new InvalidOperationException("请在 Project 中选择本灵感包的布局资源。");
        foreach (var layout in layouts)
        {
            string path = AssetDatabase.GetAssetPath(layout);
            if (!path.StartsWith(Pack + "/Layouts/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("此菜单只处理 TEMP 灵感包中的布局：" + path);
            var errors = LevelPrefabBuilder.Validate(layout);
            if (errors.Count != 0) throw new InvalidOperationException(layout.name + "\n" + string.Join("\n", errors));
        }
        // Read the already authored runtime; do not call helpers that update shared assets.
        var runtimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LevelTestSceneBuilder.RuntimePrefabPath);
        var resources = runtimePrefab != null ? runtimePrefab.GetComponent<LevelSceneRuntime>() : null;
        if (resources == null || resources.PlayerPrefab == null || resources.CameraRig == null)
            throw new InvalidOperationException("现有 GameplayRuntime 缺少玩家或相机配置。");
        var prototype = resources.PlayerPrefab.GetComponent<PlayerController>();
        if (prototype == null || prototype.Body == null || prototype.Head == null)
            throw new InvalidOperationException("现有玩家 Prefab 缺少 PlayerController 或头身引用。");
        foreach (var layout in layouts)
            if (layout.Palette.Config == null) throw new InvalidOperationException(layout.name + " 缺少角色配置。");
        LevelPrefabBuilder.EnsureFolder(Pack + "/Prefabs");
        LevelPrefabBuilder.EnsureFolder(Pack + "/Scenes");
        foreach (var layout in layouts) Build(layout, runtimePrefab, resources.PlayerPrefab);
        Debug.Log("TEMP inspiration: generated " + layouts.Length + " test scenes in " + Pack + "/Scenes");
    }

    private static void Build(LevelDefinition layout, GameObject runtimePrefab, GameObject playerPrefab)
    {
        string name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(layout));
        string prefabPath = Pack + "/Prefabs/" + name + ".prefab";
        string scenePath = Pack + "/Scenes/" + name + "_Test.unity";
        var loaded = SceneManager.GetSceneByPath(scenePath);
        if (loaded.IsValid() && loaded.isLoaded)
            throw new InvalidOperationException("请先关闭已有的 TEMP 测试场景再重新生成：" + scenePath);
        var prefab = LevelPrefabBuilder.Save(layout, prefabPath);
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var levelObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var region = levelObject.GetComponent<LevelRegion>();
            var playerObject = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            var player = playerObject.GetComponent<PlayerController>();
            var config = layout.Palette.Config;
            playerObject.transform.localScale *= config.UnitSize;
            BindConfig(player, config);
            BindConfig(player.GetComponent<RespawnService>(), config);
            BindConfig(player.GetComponent<PlayerView>(), config);
            Vector2 spawn = region.EntrySpawnPosition;
            playerObject.transform.position += (Vector3)(spawn - (Vector2)player.Body.transform.position);
            player.Head.transform.position = spawn + Vector2.up * config.ToWorld(config.JoinedOffset);
            player.GetComponent<RespawnService>().SetCheckpoint(spawn);
            var runtimeObject = (GameObject)PrefabUtility.InstantiatePrefab(runtimePrefab, scene);
            var runtime = runtimeObject.GetComponent<LevelSceneRuntime>();
            runtime.Configure(player, region, config, spawn);
            LevelTestSceneBuilder.GenerateAnnotations(region, player);
            runtime.CameraRig.CenterOnRegion(region);
            var camera = runtime.GetComponentInChildren<Camera>();
            if (camera != null)
            {
                Bounds bounds = region.WorldBounds;
                camera.orthographicSize = Mathf.Max(9f, bounds.size.y * 0.5f + 1f,
                    bounds.size.x / (2f * Mathf.Max(0.1f, camera.aspect)) + 1f);
            }
            Physics2D.SyncTransforms();
            RecordOverrides(levelObject);
            RecordOverrides(playerObject);
            RecordOverrides(runtimeObject);
            if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("无法保存 TEMP 场景：" + scenePath);
            AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceSynchronousImport);
            layout.TestSceneGuid = AssetDatabase.AssetPathToGUID(scenePath);
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssetIfDirty(layout);
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void BindConfig(Component component, PlayerConfig config)
    {
        if (component == null) return;
        var serialized = new SerializedObject(component);
        var property = serialized.FindProperty("config");
        if (property != null)
        {
            property.objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void RecordOverrides(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<Component>(true))
            if (component != null && PrefabUtility.IsPartOfPrefabInstance(component))
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
    }
}
