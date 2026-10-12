using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class WorldMapSceneBuilder
    {
        public static List<string> Validate(WorldMapDefinition map)
        {
            List<string> errors = WorldMapPrefabBuilder.Validate(map);
            if (map?.Modules == null) return errors;
            foreach (WorldMapModule module in map.Modules)
            {
                if (module?.Level == null) continue;
                LevelPlacement entry = module.Level.EntryPlacement;
                if (entry?.Brush == null || entry.Brush.HandlerId != "checkpoint")
                    errors.Add(module.DisplayName + "：请在单关编辑器指定入口复活点。");
                if (!module.Level.Placements.Exists(placement => placement?.Brush?.HandlerId == "endpoint"))
                    errors.Add(module.DisplayName + "：缺少终点。");
            }
            return errors;
        }

        public static string Generate(WorldMapDefinition map, bool openScene = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            List<string> errors = Validate(map);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
            LevelPrefabBuilder.EnsureFolder("Assets/Prefabs/WorldMaps");
            string prefabPath = AssetDatabase.GetAssetPath(map.OutputPrefab);
            if (string.IsNullOrEmpty(prefabPath)) prefabPath = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/Prefabs/WorldMaps/" + WorldMapPrefabBuilder.SafeName(map.name) + ".prefab");
            GameObject prefab = WorldMapPrefabBuilder.Save(map, prefabPath);
            GameObject resources = LevelTestSceneBuilder.EnsureRuntimePrefab();
            LevelSceneRuntime shared = resources.GetComponent<LevelSceneRuntime>();
            PlayerConfig config = map.OrderedModules()[0].Level.Palette.Config;
            if (config == null) config = shared.PlayerConfig;
            if (config == null || shared.PlayerPrefab == null || shared.CameraRig == null)
                throw new InvalidOperationException("Runtime 缺少玩家、配置或镜头。");
            foreach (WorldMapModule module in map.OrderedModules())
                if (!Mathf.Approximately(module.Level.Palette.UnitSize, config.UnitSize))
                    throw new InvalidOperationException("关卡网格单位必须与玩家配置的单位长度一致。");
            LevelPrefabBuilder.EnsureFolder("Assets/Scenes/WorldMaps");
            string scenePath = AssetDatabase.GUIDToAssetPath(map.PlaySceneGuid);
            if (string.IsNullOrEmpty(scenePath) || !scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                scenePath = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/WorldMaps/"
                    + WorldMapPrefabBuilder.SafeName(map.name) + "_Play.unity");
            Scene previous = SceneManager.GetActiveScene();
            string configPath = AssetDatabase.GetAssetPath(config);
            Scene existingScene = SceneManager.GetSceneByPath(scenePath);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                Application.isBatchMode || string.IsNullOrEmpty(previous.path)
                    || (existingScene.IsValid() && existingScene.isLoaded) ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                // Creating a Single scene can unload asset wrappers; resolve the new instance's live graph.
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                var worldObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                WorldMapRoot world = worldObject.GetComponent<WorldMapRoot>();
                map = world.Source;
                resources = AssetDatabase.LoadAssetAtPath<GameObject>(LevelTestSceneBuilder.RuntimePrefabPath);
                shared = resources.GetComponent<LevelSceneRuntime>();
                config = AssetDatabase.LoadAssetAtPath<PlayerConfig>(configPath);
                LevelRegion first = world.FindRegion(map.OrderedModules()[0]);
                var playerObject = (GameObject)PrefabUtility.InstantiatePrefab(shared.PlayerPrefab, scene);
                PlayerController player = playerObject.GetComponent<PlayerController>();
                playerObject.transform.localScale = Vector3.Scale(playerObject.transform.localScale,
                    new Vector3(config.UnitSize, config.UnitSize, 1f));
                LevelTestSceneBuilder.SetConfig(player, config);
                LevelTestSceneBuilder.SetConfig(player.GetComponent<RespawnService>(), config);
                LevelTestSceneBuilder.SetConfig(player.GetComponent<PlayerView>(), config);
                Vector2 spawn = first.EntrySpawnPosition;
                playerObject.transform.position += (Vector3)(spawn - (Vector2)player.Body.transform.position);
                player.Head.transform.position = spawn + Vector2.up * config.ToWorld(config.JoinedOffset);
                player.GetComponent<RespawnService>().SetCheckpoint(spawn);
                var runtimeObject = (GameObject)PrefabUtility.InstantiatePrefab(resources, scene);
                runtimeObject.GetComponent<LevelSceneRuntime>().Configure(player, first, config, spawn);
                if (worldObject.GetComponent<WorldMapProgression>() == null) worldObject.AddComponent<WorldMapProgression>();
                foreach (WorldMapModule module in map.OrderedModules())
                    LevelTestSceneBuilder.GenerateAnnotations(world.FindRegion(module), player);
                LevelPrefabBuilder.RecordInstanceOverrides(worldObject);
                LevelPrefabBuilder.RecordInstanceOverrides(playerObject);
                LevelPrefabBuilder.RecordInstanceOverrides(runtimeObject);
                if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("无法保存大世界游玩场景。");
                AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceSynchronousImport);
                map.PlaySceneGuid = AssetDatabase.AssetPathToGUID(scenePath);
                map.PlayScenePath = scenePath;
                EditorUtility.SetDirty(map);
                AssetDatabase.SaveAssetIfDirty(map);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
            }
            string mapPath = AssetDatabase.GetAssetPath(map);
            map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(mapPath);
            string selectionPath = WorldMapMenuSceneBuilder.SaveSelection(map);
            if (openScene && !Application.isBatchMode) EditorSceneManager.OpenScene(selectionPath, OpenSceneMode.Single);
            return scenePath;
        }
    }
}
