using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ScreenFilters;

namespace TapTap.Editor
{
    public static class LevelTestSceneBuilder
    {
        public const string RuntimePrefabPath = "Assets/Prefabs/Runtime/GameplayRuntime.prefab";
        public const string TestSceneFolder = "Assets/Scenes/Test";
        private const string DefaultPlayerPath = "Assets/Prefabs/Characters/Player.prefab";
        private const string DefaultConfigPath = "Assets/Runtime/Config/PrototypePlayer.asset";

        public static GameObject EnsureRuntimePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePrefabPath);
            if (existing != null)
            {
                if (existing.GetComponent<LevelSceneRuntime>() == null)
                    throw new InvalidOperationException("Runtime Prefab 缺少 LevelSceneRuntime 配置。");
                var existingOutline = existing.GetComponentInChildren<PhaseBlockOutline>(true);
                if (existing.GetComponentInChildren<WorldPhaseState>(true) == null || existingOutline == null
                    || existingOutline.GetComponent<MeshFilter>() == null
                    || existing.GetComponentInChildren<RewindManager>(true) == null
                    || existing.GetComponentInChildren<RewindScreenEffect>(true) == null
                    || existing.GetComponentInChildren<ScreenFilterCamera>(true) == null)
                {
                    GameObject root = PrefabUtility.LoadPrefabContents(RuntimePrefabPath);
                    try
                    {
                        Transform managers = root.transform.Find("Managers");
                        if (managers == null) { managers = new GameObject("Managers").transform; managers.SetParent(root.transform, false); }
                        if (managers.GetComponent<WorldPhaseState>() == null) managers.gameObject.AddComponent<WorldPhaseState>();
                        if (managers.GetComponent<RewindManager>() == null) managers.gameObject.AddComponent<RewindManager>();
                        Camera viewCamera = root.GetComponentInChildren<Camera>(true);
                        if (viewCamera != null && viewCamera.GetComponent<RewindScreenEffect>() == null)
                            viewCamera.gameObject.AddComponent<RewindScreenEffect>();
                        if (viewCamera != null) BindScreenFilters(viewCamera);
                        PlayerEffectsPrefabs.BindRuntime(root, GameplayVisualAssets.EnsureVisualSettings());
                        PrefabUtility.SaveAsPrefabAsset(root, RuntimePrefabPath);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                    existing = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePrefabPath);
                }
                return existing;
            }
            GameObject character = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPlayerPath);
            PlayerConfig config = AssetDatabase.LoadAssetAtPath<PlayerConfig>(DefaultConfigPath);
            if (character == null || config == null)
                throw new InvalidOperationException("缺少玩家 Prefab 或玩家配置，请先准备基础资源。");
            LevelPrefabBuilder.EnsureFolder("Assets/Prefabs/Runtime");
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("GameplayRuntime");
                SceneManager.MoveGameObjectToScene(root, preview);
                var effects = new GameObject("Managers");
                effects.transform.SetParent(root.transform, false);
                effects.AddComponent<EffectManager>();
                effects.AddComponent<WorldPhaseState>();
                effects.AddComponent<RewindManager>();
                var follow = new GameObject("Camera Rig");
                follow.transform.SetParent(root.transform, false);
                follow.transform.localPosition = new Vector3(3f, 3.5f, -10f);
                var shake = new GameObject("Camera Shake");
                shake.transform.SetParent(follow.transform, false);
                var cameraObject = new GameObject("Main Camera");
                cameraObject.transform.SetParent(shake.transform, false);
                cameraObject.tag = "MainCamera";
                Camera camera = cameraObject.AddComponent<Camera>();
                SceneCameraDefaults.EnablePostProcessing(camera);
                cameraObject.AddComponent<RewindScreenEffect>();
                BindScreenFilters(camera);
                camera.orthographic = false;
                camera.fieldOfView = 38f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.045f, 0.075f, 0.12f);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100f;
                cameraObject.AddComponent<AudioListener>();
                DemoCamera rig = follow.AddComponent<DemoCamera>();
                rig.Configure(null, camera, shake.transform);
                root.AddComponent<LevelSceneRuntime>().ConfigureResources(character, config, rig);
                PlayerEffectsPrefabs.BindRuntime(root, GameplayVisualAssets.EnsureVisualSettings());
                GameObject result = PrefabUtility.SaveAsPrefabAsset(root, RuntimePrefabPath, out bool success);
                if (!success || result == null) throw new IOException("无法保存 Runtime Prefab。");
                return result;
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        public static GameObject LoadRuntimePrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePrefabPath);
            LevelSceneRuntime runtime = prefab != null ? prefab.GetComponent<LevelSceneRuntime>() : null;
            PlayerController player = runtime != null && runtime.PlayerPrefab != null
                ? runtime.PlayerPrefab.GetComponent<PlayerController>() : null;
            if (runtime == null || player == null || player.Body == null || player.Head == null
                || player.GetComponent<RespawnService>() == null || player.GetComponent<PlayerInput>() == null
                || runtime.CameraRig == null || runtime.PlayerConfig == null)
                throw new InvalidOperationException("GameplayRuntime 或主角配置不完整，请使用 TapTap → 维护 → 升级关卡编辑资源。");
            return prefab;
        }

        private static void BindScreenFilters(Camera camera)
        {
            var binding = camera.GetComponent<ScreenFilterCamera>();
            if (binding == null) binding = camera.gameObject.AddComponent<ScreenFilterCamera>();
            if (binding.Profile == null) binding.SetProfile(AssetDatabase.LoadAssetAtPath<ScreenFilterProfile>(
                "Assets/Rendering/ScreenFilters/Profiles/DefaultScreenFilters.asset"));
        }

        public static string Generate(LevelDefinition layout, bool openScene = true)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            LevelPrefabBuilder.EnsureFolder("Assets/Prefabs/Levels");
            string path = layout.OutputPrefab != null ? AssetDatabase.GetAssetPath(layout.OutputPrefab) :
                AssetDatabase.GenerateUniqueAssetPath("Assets/Prefabs/Levels/" + FileName(layout.name) + ".prefab");
            GameObject level = LevelPrefabBuilder.Save(layout, path);
            return Generate(level, layout.Palette != null ? layout.Palette.Config : null, openScene);
        }

        public static string Generate(GameObject levelPrefab, PlayerConfig config = null, bool openScene = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode，再创建测试场景。");
            if (levelPrefab == null || !PrefabUtility.IsPartOfPrefabAsset(levelPrefab)
                || levelPrefab.GetComponent<LevelRegion>() == null)
                throw new InvalidOperationException("请选择带有 LevelRegion 的区域 Prefab。");
            GameObject runtimePrefab = LoadRuntimePrefab();
            LevelSceneRuntime shared = runtimePrefab.GetComponent<LevelSceneRuntime>();
            if (shared.PlayerPrefab == null || shared.PlayerPrefab.GetComponent<PlayerController>() == null)
                throw new InvalidOperationException("Runtime 配置需要一个带有 PlayerController 的玩家 Prefab。");
            PlayerController character = shared.PlayerPrefab.GetComponent<PlayerController>();
            if (character.Body == null || character.Head == null || character.GetComponent<RespawnService>() == null
                || character.GetComponent<PlayerInput>() == null || shared.CameraRig == null)
                throw new InvalidOperationException("Runtime 或玩家 Prefab 缺少相机、输入、身体、头部或复活配置。");
            config = config != null ? config : levelPrefab.GetComponent<LevelRegion>().Source?.Palette?.Config;
            config = config != null ? config : shared.PlayerConfig;
            if (config == null || config.UnitSize <= 0f || float.IsNaN(config.UnitSize) || float.IsInfinity(config.UnitSize))
                throw new InvalidOperationException("Runtime 配置缺少有效的玩家配置。");
            LevelPrefabBuilder.EnsureFolder(TestSceneFolder);
            LevelDefinition source = levelPrefab.GetComponent<LevelRegion>().Source;
            string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : null;
            string bound = source != null ? AssetDatabase.GUIDToAssetPath(source.TestSceneGuid) : null;
            string scenePath = !string.IsNullOrEmpty(bound) && AssetDatabase.LoadAssetAtPath<SceneAsset>(bound) != null ? bound
                : TestSceneFolder + "/" + FileName(levelPrefab.name) + "_Test.unity";
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
            Scene previous = SceneManager.GetActiveScene();
            bool untitled = false;
            for (int index = 0; index < SceneManager.sceneCount; index++)
                untitled |= string.IsNullOrEmpty(SceneManager.GetSceneAt(index).path);
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool alreadyOpen = scene.IsValid() && scene.isLoaded;
            bool existed = File.Exists(scenePath);
            if (!alreadyOpen)
                scene = existed ? EditorSceneManager.OpenScene(scenePath, untitled ? OpenSceneMode.Single : OpenSceneMode.Additive)
                    : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, untitled ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                if (FindOne<WorldMapRoot>(scene) != null)
                    throw new InvalidOperationException("绑定的导航场景是大地图，请为此关卡绑定单关测试场景后再更新。");
                LevelRegion region = FindOne<LevelRegion>(scene);
                if (region != null && (region.Source != source ||
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(region.gameObject) != AssetDatabase.GetAssetPath(levelPrefab)))
                    throw new InvalidOperationException("目标测试场景的关卡已解包或引用了其它 Prefab，已停止更新。");
                SceneManager.SetActiveScene(scene);
                if (!existed) GameplayLightingDefaults.Apply(scene);
                if (region == null)
                    region = ((GameObject)PrefabUtility.InstantiatePrefab(levelPrefab, scene)).GetComponent<LevelRegion>();
                PlayerController player = FindOne<PlayerController>(scene);
                bool newPlayer = player == null;
                if (newPlayer)
                {
                    player = ((GameObject)PrefabUtility.InstantiatePrefab(shared.PlayerPrefab, scene)).GetComponent<PlayerController>();
                    player.transform.localScale *= config.UnitSize;
                }
                SetConfig(player, config);
                SetConfig(player.GetComponent<RespawnService>(), config);
                SetConfig(player.GetComponent<PlayerView>(), config);
                Physics2D.SyncTransforms();
                Vector2 spawn = FindSpawn(region, player, config);
                if (newPlayer)
                {
                    player.transform.position += (Vector3)(spawn - (Vector2)player.Body.transform.position);
                    player.Head.transform.position = spawn + Vector2.up * config.ToWorld(config.JoinedOffset);
                    player.GetComponent<RespawnService>().SetCheckpoint(spawn);
                }
                LevelSceneRuntime runtime = FindOne<LevelSceneRuntime>(scene);
                bool newRuntime = runtime == null;
                if (newRuntime) runtime = ((GameObject)PrefabUtility.InstantiatePrefab(runtimePrefab, scene)).GetComponent<LevelSceneRuntime>();
                runtime.Configure(player, region, config, spawn);
                if (newRuntime) SceneCameraDefaults.Apply(runtime.gameObject);
                runtime.CameraRig?.SetRegion(region);
                Transform annotations = region.transform.Find("Test Annotations");
                if (annotations != null) UnityEngine.Object.DestroyImmediate(annotations.gameObject);
                LevelPrefabBuilder.RecordInstanceOverrides(region.gameObject);
                LevelPrefabBuilder.RecordInstanceOverrides(player.gameObject);
                LevelPrefabBuilder.RecordInstanceOverrides(runtime.gameObject);
                if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("无法保存测试场景。");
                AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceSynchronousImport);
                if (!string.IsNullOrEmpty(sourcePath)) source = AssetDatabase.LoadAssetAtPath<LevelDefinition>(sourcePath);
                if (source != null)
                {
                    LevelSceneNavigation.Assign(source, AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath));
                    AssetDatabase.SaveAssetIfDirty(source);
                }
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (!alreadyOpen && scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(scene, true);
            }
            if (!Application.isBatchMode && openScene)
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                if (SceneView.lastActiveSceneView != null)
                    SceneView.lastActiveSceneView.Frame(levelPrefab.GetComponent<LevelRegion>().WorldBounds, false);
            }
            Debug.Log("Level test scene saved: " + scenePath);
            return scenePath;
        }

        private static T FindOne<T>(Scene scene) where T : Component
        {
            T found = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (T candidate in root.GetComponentsInChildren<T>(true))
                {
                    if (found != null) throw new InvalidOperationException("测试场景存在多个 " + typeof(T).Name + "，已停止更新。");
                    found = candidate;
                }
            return found;
        }

        private static Vector2 FindSpawn(LevelRegion region, PlayerController player, PlayerConfig config)
        {
            if (region.ResolveEntryCheckpoint() != null || region.Source?.EntryPlacement != null)
                return region.EntrySpawnPosition;
            CheckpointFlag flag = region.GetComponentInChildren<CheckpointFlag>();
            if (flag != null) return flag.SpawnPosition;
            BoxCollider2D body = player.Body.GetComponent<BoxCollider2D>();
            BoxCollider2D head = player.Head.GetComponent<BoxCollider2D>();
            Vector2 bodySize = Vector2.Scale(body.size, (Vector2)body.transform.lossyScale);
            Vector2 headSize = Vector2.Scale(head.size, (Vector2)head.transform.lossyScale);
            Collider2D[] obstacles = region.GetComponentsInChildren<Collider2D>();
            WorldSurface[] supports = region.GetComponentsInChildren<WorldSurface>();
            Array.Sort(supports, (a, b) =>
            {
                float ay = a.GetComponent<Collider2D>()?.bounds.max.y ?? float.PositiveInfinity;
                float by = b.GetComponent<Collider2D>()?.bounds.max.y ?? float.PositiveInfinity;
                int y = ay.CompareTo(by);
                return y != 0 ? y : a.transform.position.x.CompareTo(b.transform.position.x);
            });
            foreach (WorldSurface surface in supports)
            {
                Collider2D support = surface.GetComponent<Collider2D>();
                if (support == null || !support.enabled || support.isTrigger) continue;
                Vector2 candidate = new Vector2(support.bounds.center.x,
                    support.bounds.max.y + bodySize.y * 0.5f + Mathf.Max(0.001f, config.Skin));
                Bounds occupied = new Bounds(candidate, bodySize);
                occupied.Encapsulate(new Bounds(candidate + Vector2.up * config.ToWorld(config.JoinedOffset), headSize));
                bool blocked = false;
                foreach (Collider2D obstacle in obstacles)
                {
                    if (!obstacle.enabled || obstacle.isTrigger && obstacle.GetComponent<LethalZone>() == null) continue;
                    Bounds bounds = obstacle.bounds;
                    if (occupied.min.x < bounds.max.x && occupied.max.x > bounds.min.x &&
                        occupied.min.y < bounds.max.y && occupied.max.y > bounds.min.y)
                    { blocked = true; break; }
                }
                if (!blocked) return candidate;
            }
            return region.transform.TransformPoint(new Vector2(region.UnitSize * 0.5f, region.UnitSize * 2f));
        }

        internal static void SetConfig(Component component, PlayerConfig config)
        {
            if (component == null) return;
            var serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty("config");
            if (property != null) { property.objectReferenceValue = config; serialized.ApplyModifiedPropertiesWithoutUndo(); }
        }

        private static string FileName(string name)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(name) ? "Level" : name;
        }

        [MenuItem("Assets/TapTap/更新或创建关卡测试场景", true)]
        private static bool CanCreateFromSelection() => Selection.activeObject is GameObject prefab &&
            PrefabUtility.IsPartOfPrefabAsset(prefab) && prefab.GetComponent<LevelRegion>() != null &&
            !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem("Assets/TapTap/更新或创建关卡测试场景")]
        private static void CreateFromSelection() => Generate((GameObject)Selection.activeObject);
    }
}
