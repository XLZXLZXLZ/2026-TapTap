using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class LevelTestSceneBuilder
    {
        public const string RuntimePrefabPath = "Assets/Prefabs/Runtime/GameplayRuntime.prefab";
        public const string TestSceneFolder = "Assets/Scenes/Test";
        private const string DefaultPlayerPath = "Assets/Prefabs/Characters/Player.prefab";
        private const string DefaultConfigPath = "Assets/Runtime/Config/PrototypePlayer.asset";
        private static float ReferenceAspect => (float)Mathf.Max(1, PlayerSettings.defaultScreenWidth)
            / Mathf.Max(1, PlayerSettings.defaultScreenHeight);

        public static GameObject EnsureRuntimePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePrefabPath);
            if (existing != null)
            {
                if (existing.GetComponent<LevelSceneRuntime>() == null)
                    throw new InvalidOperationException("Runtime Prefab 缺少 LevelSceneRuntime 配置。");
                var existingOutline = existing.GetComponentInChildren<PhaseBlockOutline>(true);
                if (existing.GetComponentInChildren<WorldPhaseState>(true) == null || existingOutline == null || existingOutline.GetComponent<MeshFilter>() == null)
                {
                    GameObject root = PrefabUtility.LoadPrefabContents(RuntimePrefabPath);
                    try
                    {
                        Transform managers = root.transform.Find("Managers");
                        if (managers == null) { managers = new GameObject("Managers").transform; managers.SetParent(root.transform, false); }
                        if (managers.GetComponent<WorldPhaseState>() == null) managers.gameObject.AddComponent<WorldPhaseState>();
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
                var follow = new GameObject("Camera Rig");
                follow.transform.SetParent(root.transform, false);
                follow.transform.localPosition = new Vector3(3f, 3.5f, -10f);
                var shake = new GameObject("Camera Shake");
                shake.transform.SetParent(follow.transform, false);
                var cameraObject = new GameObject("Main Camera");
                cameraObject.transform.SetParent(shake.transform, false);
                cameraObject.tag = "MainCamera";
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5.6f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.045f, 0.075f, 0.12f);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100f;
                cameraObject.AddComponent<AudioListener>();
                DemoCamera rig = follow.AddComponent<DemoCamera>();
                rig.Configure(null, camera, shake.transform);
                var overlay = new GameObject("HUD");
                overlay.transform.SetParent(root.transform, false);
                PrototypeHUD hud = overlay.AddComponent<PrototypeHUD>();
                hud.Configure(null, config);
                root.AddComponent<LevelSceneRuntime>().ConfigureResources(character, config, rig, hud);
                PlayerEffectsPrefabs.BindRuntime(root, GameplayVisualAssets.EnsureVisualSettings());
                GameObject result = PrefabUtility.SaveAsPrefabAsset(root, RuntimePrefabPath, out bool success);
                if (!success || result == null) throw new IOException("无法保存 Runtime Prefab。");
                return result;
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
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
            GameObject runtimePrefab = EnsureRuntimePrefab();
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
            string scenePath = AssetDatabase.GenerateUniqueAssetPath(TestSceneFolder + "/" + FileName(levelPrefab.name) + "_Test.unity");
            Scene previous = SceneManager.GetActiveScene();
            bool untitledScene = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                untitledScene |= string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path);
            if (!Application.isBatchMode && untitledScene && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return null;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                Application.isBatchMode || untitledScene ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var regionObject = (GameObject)PrefabUtility.InstantiatePrefab(levelPrefab, scene);
                LevelRegion region = regionObject.GetComponent<LevelRegion>();
                regionObject.transform.position = Vector3.zero;
                var playerObject = (GameObject)PrefabUtility.InstantiatePrefab(shared.PlayerPrefab, scene);
                PlayerController player = playerObject.GetComponent<PlayerController>();
                Vector3 scale = playerObject.transform.localScale;
                playerObject.transform.localScale = new Vector3(scale.x * config.UnitSize, scale.y * config.UnitSize, scale.z);
                SetConfig(player, config);
                SetConfig(player.GetComponent<RespawnService>(), config);
                SetConfig(player.GetComponent<PlayerView>(), config);
                Physics2D.SyncTransforms();
                Vector2 spawn = FindSpawn(region, player, config);
                playerObject.transform.position += (Vector3)(spawn - (Vector2)player.Body.transform.position);
                player.Head.transform.position = (Vector3)(spawn + Vector2.up * config.ToWorld(config.JoinedOffset));
                player.GetComponent<RespawnService>().SetCheckpoint(spawn);
                var runtimeObject = (GameObject)PrefabUtility.InstantiatePrefab(runtimePrefab, scene);
                LevelSceneRuntime runtime = runtimeObject.GetComponent<LevelSceneRuntime>();
                runtime.Configure(player, region, config, spawn);
                runtime.CameraRig?.FrameRegionHorizontally(region, ReferenceAspect);
                LevelPrefabBuilder.RecordInstanceOverrides(regionObject);
                LevelPrefabBuilder.RecordInstanceOverrides(playerObject);
                LevelPrefabBuilder.RecordInstanceOverrides(runtimeObject);
                if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("无法保存测试场景。");
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
            }
            if (!Application.isBatchMode && openScene)
            {
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
                EditorGUIUtility.PingObject(Selection.activeObject);
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    if (SceneView.lastActiveSceneView != null)
                        SceneView.lastActiveSceneView.Frame(levelPrefab.GetComponent<LevelRegion>().WorldBounds, false);
                }
            }
            Debug.Log("Level test scene saved: " + scenePath);
            return scenePath;
        }

        [MenuItem("TapTap/Level Designer/Update Test Scene Cameras")]
        public static void UpdateTestSceneCameras()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Scene previous = SceneManager.GetActiveScene();
            int updated = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { TestSceneFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Scene scene = SceneManager.GetSceneByPath(path);
                bool loaded = scene.IsValid() && scene.isLoaded;
                if (loaded && scene.isDirty) continue;
                if (!loaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    LevelRegion region = null;
                    LevelSceneRuntime runtime = null;
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        if (region == null) region = root.GetComponentInChildren<LevelRegion>(true);
                        if (runtime == null) runtime = root.GetComponentInChildren<LevelSceneRuntime>(true);
                    }
                    if (region == null || runtime == null || runtime.CameraRig == null) continue;
                    runtime.CameraRig.FrameRegionHorizontally(region, ReferenceAspect);
                    LevelPrefabBuilder.RecordInstanceOverrides(runtime.gameObject);
                    if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("无法保存测试场景：" + path);
                    updated++;
                }
                finally
                {
                    if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                    if (!loaded && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                }
            }
            Debug.Log("Fixed test scene cameras updated: " + updated);
        }

        private static Vector2 FindSpawn(LevelRegion region, PlayerController player, PlayerConfig config)
        {
            if (region.EntryCheckpoint != null) return region.EntrySpawnPosition;
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

        private static void SetConfig(Component component, PlayerConfig config)
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

        [MenuItem("Assets/TapTap/Create Level Test Scene", true)]
        private static bool CanCreateFromSelection() => Selection.activeObject is GameObject prefab &&
            PrefabUtility.IsPartOfPrefabAsset(prefab) && prefab.GetComponent<LevelRegion>() != null &&
            !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem("Assets/TapTap/Create Level Test Scene")]
        private static void CreateFromSelection() => Generate((GameObject)Selection.activeObject);

        public static void CreateExampleTestScene()
        {
            LevelEditorAssets.CreateStarterAssets();
            Generate(AssetDatabase.LoadAssetAtPath<GameObject>(LevelEditorAssets.ExamplePrefabPath), null, false);
            AdoptRuntimeInDemo();
        }

        public static void AdoptRuntimeInDemo()
        {
            if (!File.Exists(PrototypeBuilder.DemoScenePath)) return;
            Scene previous = SceneManager.GetActiveScene();
            Scene demo = SceneManager.GetSceneByPath(PrototypeBuilder.DemoScenePath);
            bool alreadyLoaded = demo.IsValid() && demo.isLoaded;
            if (alreadyLoaded && demo.isDirty) return;
            if (!alreadyLoaded) demo = EditorSceneManager.OpenScene(PrototypeBuilder.DemoScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject[] roots = demo.GetRootGameObjects();
                PlayerController player = null;
                DemoCamera oldCamera = null;
                PrototypeHUD oldHud = null;
                EffectManager oldEffects = null;
                foreach (GameObject root in roots)
                {
                    if (root.GetComponentInChildren<LevelSceneRuntime>(true) != null) return;
                    if (player == null) player = root.GetComponentInChildren<PlayerController>();
                    if (oldCamera == null) oldCamera = root.GetComponentInChildren<DemoCamera>();
                    if (oldHud == null) oldHud = root.GetComponentInChildren<PrototypeHUD>();
                    if (oldEffects == null) oldEffects = root.GetComponentInChildren<EffectManager>();
                }
                if (player == null) return;
                var runtimeObject = (GameObject)PrefabUtility.InstantiatePrefab(EnsureRuntimePrefab(), demo);
                LevelSceneRuntime runtime = runtimeObject.GetComponent<LevelSceneRuntime>();
                if (oldCamera != null)
                {
                    Camera previousCamera = oldCamera.GetComponentInChildren<Camera>();
                    Camera nextCamera = runtime.CameraRig.GetComponentInChildren<Camera>();
                    if (previousCamera != null) EditorUtility.CopySerialized(previousCamera, nextCamera);
                    runtime.CameraRig.transform.position = oldCamera.transform.position;
                }
                if (oldEffects != null)
                    EditorUtility.CopySerialized(oldEffects, runtimeObject.GetComponentInChildren<EffectManager>());
                runtime.Configure(player, null, player.Config, player.Body.transform.position);
                if (oldCamera != null) UnityEngine.Object.DestroyImmediate(oldCamera.gameObject);
                if (oldHud != null) UnityEngine.Object.DestroyImmediate(oldHud.gameObject);
                if (oldEffects != null) UnityEngine.Object.DestroyImmediate(oldEffects);
                LevelPrefabBuilder.RecordInstanceOverrides(runtimeObject);
                if (!EditorSceneManager.SaveScene(demo, PrototypeBuilder.DemoScenePath))
                    throw new IOException("无法保存 Demo 的 Runtime Prefab 引用。");
                Debug.Log("Demo now references GameplayRuntime.prefab.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (!alreadyLoaded && demo.IsValid() && demo.isLoaded) EditorSceneManager.CloseScene(demo, true);
            }
        }
    }
}
