using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    // Synchronization is explicit: editing, Undo and entering Play do not load or save scenes.
    public static class WorldMapSceneSync
    {
        private static bool synchronizing;

        [MenuItem("TapTap/保存并同步大地图", false, 102)]
        public static void SaveCurrentWorld()
        {
            WorldMapDefinition map = CurrentMap();
            if (map == null) return;
            try
            {
                SaveAndSynchronize(map);
                Debug.Log("大地图及游玩场景已保存并同步：" + map.name, map);
            }
            catch (Exception exception) { Debug.LogError("大地图保存失败：" + exception.Message, map); }
        }

        [MenuItem("TapTap/保存并同步大地图", true)]
        private static bool CanSaveCurrentWorld() => !EditorApplication.isPlayingOrWillChangePlaymode && CurrentMap() != null;

        private static WorldMapDefinition CurrentMap()
        {
            if (EditorWindow.focusedWindow is WorldMapEditorWindow focused && focused.Definition != null) return focused.Definition;
            if (Selection.activeObject is WorldMapDefinition selected) return selected;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.GetComponent<WorldMapRoot>() is WorldMapRoot world && world.Source != null) return world.Source;
            foreach (WorldMapEditorWindow window in Resources.FindObjectsOfTypeAll<WorldMapEditorWindow>())
                if (window.Definition != null) return window.Definition;
            return AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(EditorPrefs.GetString(
                "TapTap.WorldMap.LastMap." + Application.dataPath, "Assets/Runtime/WorldMaps/World_Character.asset"));
        }

        public static string SaveAndSynchronize(WorldMapDefinition map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑状态保存大地图。");
            AssetDatabase.SaveAssetIfDirty(map);
            foreach (WorldMapModule module in map.OrderedModules()) AssetDatabase.SaveAssetIfDirty(module.Level);
            string path = Synchronize(map);
            Scene scene = SceneManager.GetSceneByPath(path);
            if (scene.IsValid() && scene.isLoaded && !EditorSceneManager.SaveScene(scene, path))
                throw new IOException("无法保存游玩场景：" + path);
            AssetDatabase.SaveAssetIfDirty(map);
            return path;
        }

        public static string ScenePath(WorldMapDefinition map)
        {
            string path = AssetDatabase.GUIDToAssetPath(map.PlaySceneGuid);
            return !string.IsNullOrEmpty(path) ? path : map.PlayScenePath;
        }

        public static string Synchronize(WorldMapDefinition map, ISet<LevelDefinition> changedLevels = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑状态更新大世界场景。");
            if (synchronizing) return ScenePath(map);
            synchronizing = true;
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = default;
            bool opened = false;
            try
            {
                GameObject resources = LevelTestSceneBuilder.EnsureRuntimePrefab();
                var levels = new Dictionary<LevelDefinition, GameObject>();
                foreach (WorldMapModule module in map.OrderedModules())
                {
                    if (levels.ContainsKey(module.Level)) continue;
                    string levelPath = AssetDatabase.GetAssetPath(module.Level.OutputPrefab);
                    if (string.IsNullOrEmpty(levelPath)) levelPath = AssetDatabase.GenerateUniqueAssetPath(
                        "Assets/Prefabs/Levels/" + WorldMapPrefabBuilder.SafeName(module.Level.name) + ".prefab");
                    GameObject prefab = module.Level.OutputPrefab;
                    if (prefab == null || changedLevels == null || changedLevels.Contains(module.Level))
                    {
                        try { prefab = LevelPrefabBuilder.Save(module.Level, levelPath); }
                        catch (InvalidOperationException exception)
                        {
                            // Incomplete edits must not prevent other modules from moving in the scene.
                            Debug.LogWarning(module.DisplayName + " 的地形暂未更新：" + exception.Message, module.Level);
                        }
                    }
                    if (prefab != null) levels.Add(module.Level, prefab);
                }
                string path = ScenePath(map);
                if (string.IsNullOrEmpty(path))
                {
                    LevelPrefabBuilder.EnsureFolder("Assets/Scenes/WorldMaps");
                    path = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/WorldMaps/"
                        + WorldMapPrefabBuilder.SafeName(map.name) + "_Play.unity");
                }
                scene = SceneManager.GetSceneByPath(path);
                bool loaded = scene.IsValid() && scene.isLoaded;
                if (!loaded)
                {
                    scene = File.Exists(path) ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive)
                        : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    opened = true;
                }
                Populate(scene, map, resources, levels, changedLevels);
                WorldMapSceneConflicts.Check(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                // Keep unsaved manual work in an already-open scene under normal Unity save control.
                if (opened && !EditorSceneManager.SaveScene(scene, path)) throw new IOException("无法保存游玩场景：" + path);
                if (!File.Exists(path)) throw new IOException("游玩场景尚未保存：" + path);
                if (opened) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (map.PlaySceneGuid != guid || map.PlayScenePath != path)
                {
                    map.PlaySceneGuid = guid; map.PlayScenePath = path;
                    EditorUtility.SetDirty(map); AssetDatabase.SaveAssetIfDirty(map);
                }
                SceneView.RepaintAll();
                return path;
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                synchronizing = false;
            }
        }

        private static T Find<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            { T found = root.GetComponentInChildren<T>(true); if (found != null) return found; }
            return null;
        }

        private static void Populate(Scene scene, WorldMapDefinition map, GameObject resources,
            Dictionary<LevelDefinition, GameObject> levels, ISet<LevelDefinition> changedLevels)
        {
            WorldMapRoot world = Find<WorldMapRoot>(scene);
            if (world == null)
            {
                var root = new GameObject(map.name); SceneManager.MoveGameObjectToScene(root, scene);
                world = root.AddComponent<WorldMapRoot>();
            }
            else if (PrefabUtility.IsPartOfPrefabInstance(world.gameObject))
                PrefabUtility.UnpackPrefabInstance(world.gameObject, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            world.Source = map;
            Transform generated = world.transform.Find("Generated");
            if (generated == null) { generated = new GameObject("Generated").transform; generated.SetParent(world.transform, false); }
            if (world.transform.Find("Custom") == null) new GameObject("Custom").transform.SetParent(world.transform, false);
            var existing = new Dictionary<string, LevelRegion>();
            foreach (LevelRegion region in generated.GetComponentsInChildren<LevelRegion>(true)) existing[region.WorldModuleId] = region;
            var retained = new HashSet<LevelRegion>();
            foreach (WorldMapModule module in map.OrderedModules())
            {
                if (!levels.TryGetValue(module.Level, out GameObject prefab)) continue;
                existing.TryGetValue(module.Id, out LevelRegion region);
                if (region != null && region.Source != module.Level)
                { UnityEngine.Object.DestroyImmediate(region.gameObject); region = null; }
                if (region == null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    instance.transform.SetParent(generated, false); region = instance.GetComponent<LevelRegion>();
                }
                retained.Add(region);
                region.gameObject.SetActive(true);
                region.name = module.DisplayName + " [" + module.Position.x + "," + module.Position.y + "] " + module.Id;
                region.transform.localPosition = (Vector2)module.Position * module.Level.Palette.UnitSize;
                region.Source = module.Level; region.Size = module.Level.Size; region.UnitSize = module.Level.Palette.UnitSize;
                region.WorldModuleId = module.Id; region.SequenceNumber = map.SequenceNumber(module);
                if (region.EntryCheckpoint == null) region.EntryCheckpoint = region.GetComponentInChildren<CheckpointFlag>(true);
                PrefabUtility.RecordPrefabInstancePropertyModifications(region);
                PrefabUtility.RecordPrefabInstancePropertyModifications(region.transform);
            }
            foreach (LevelRegion region in existing.Values)
                if (region != null && !retained.Contains(region)) UnityEngine.Object.DestroyImmediate(region.gameObject);
            world.InvalidateRegions();
            LevelSceneRuntime runtime = Find<LevelSceneRuntime>(scene);
            if (runtime == null) runtime = ((GameObject)PrefabUtility.InstantiatePrefab(resources, scene)).GetComponent<LevelSceneRuntime>();
            PlayerController player = Find<PlayerController>(scene);
            bool newPlayer = player == null;
            if (newPlayer) player = ((GameObject)PrefabUtility.InstantiatePrefab(runtime.PlayerPrefab, scene)).GetComponent<PlayerController>();
            var modules = map.OrderedModules();
            LevelRegion first = modules.Find(module => levels.ContainsKey(module.Level)) is WorldMapModule entry ? world.FindRegion(entry) : null;
            PlayerConfig config = first != null ? first.Source.Palette.Config : runtime.PlayerConfig;
            if (config == null) config = runtime.PlayerConfig;
            LevelTestSceneBuilder.SetConfig(player, config);
            LevelTestSceneBuilder.SetConfig(player.GetComponent<RespawnService>(), config);
            LevelTestSceneBuilder.SetConfig(player.GetComponent<PlayerView>(), config);
            if (newPlayer && first != null)
            {
                player.transform.localScale = Vector3.Scale(player.transform.localScale, new Vector3(config.UnitSize, config.UnitSize, 1f));
                Vector2 spawn = first.EntrySpawnPosition;
                player.transform.position += (Vector3)(spawn - (Vector2)player.Body.transform.position);
                player.Head.transform.position = spawn + Vector2.up * config.ToWorld(config.JoinedOffset);
                player.GetComponent<RespawnService>().SetCheckpoint(spawn);
            }
            runtime.Configure(player, first, config, first != null ? first.EntrySpawnPosition : (Vector2)player.Body.transform.position);
            if (world.GetComponent<WorldMapProgression>() == null) world.gameObject.AddComponent<WorldMapProgression>();
            foreach (LevelRegion region in retained)
                if (changedLevels == null || changedLevels.Contains(region.Source) || region.transform.Find("Test Annotations") == null)
                {
                    Transform annotations = region.transform.Find("Test Annotations");
                    if (annotations != null) UnityEngine.Object.DestroyImmediate(annotations.gameObject);
                    LevelTestSceneBuilder.GenerateAnnotations(region, player);
                }
            LevelPrefabBuilder.RecordInstanceOverrides(player.gameObject);
            LevelPrefabBuilder.RecordInstanceOverrides(runtime.gameObject);
            WorldMapGameplayBootstrap bootstrap = Find<WorldMapGameplayBootstrap>(scene);
            if (bootstrap != null) UnityEngine.Object.DestroyImmediate(bootstrap.gameObject);
        }

        public static void GenerateDefaultBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project.");
            try
            {
                var map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
                string path = Synchronize(map);
                File.WriteAllText(Path.Combine(Application.dataPath, "../world-scene-generated.txt"), path);
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }
    }
}
