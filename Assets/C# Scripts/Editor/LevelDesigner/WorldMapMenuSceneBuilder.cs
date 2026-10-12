using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class WorldMapMenuSceneBuilder
    {
        public static string Generate(WorldMapDefinition map, bool openScene = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            if (map == null || map.OrderedModules().Count == 0) throw new InvalidOperationException("大世界没有可用关卡。");
            int missing = map.Modules.FindAll(module => module?.Level == null).Count;
            if (missing > 0) Debug.LogWarning("大世界中有 " + missing + " 个模块缺少来源，未加入选关；请在大世界编辑器补全或移除历史引用。", map);
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
            string mapPath = AssetDatabase.GetAssetPath(map);
            GameObject resources = LevelTestSceneBuilder.EnsureRuntimePrefab();
            var runtime = resources.GetComponent<LevelSceneRuntime>();
            if (runtime.PlayerPrefab == null || runtime.PlayerConfig == null || runtime.CameraRig == null)
                throw new InvalidOperationException("Runtime 缺少玩家、配置或镜头。");
            var ids = new HashSet<string>();
            var numbers = new HashSet<int>();
            foreach (WorldMapModule module in map.OrderedModules())
            {
                if (string.IsNullOrEmpty(module.Id) || !ids.Add(module.Id) || !numbers.Add(map.SequenceNumber(module)))
                    throw new InvalidOperationException("模块 ID 或关卡序号重复，请先修正。");
                LevelRegion region = module.Level.OutputPrefab != null ? module.Level.OutputPrefab.GetComponent<LevelRegion>() : null;
                if (region == null || module.Level.Palette == null) throw new InvalidOperationException(module.DisplayName + " 缺少生成的关卡 Prefab 或 Palette。");
                PlayerConfig config = module.Level.Palette.Config ?? runtime.PlayerConfig;
                if (!Mathf.Approximately(module.Level.Palette.UnitSize, config.UnitSize)
                    || !Mathf.Approximately(module.Level.Palette.UnitSize, region.UnitSize))
                    throw new InvalidOperationException(module.DisplayName + " 的网格单位与玩家/Prefab 不一致。");
                if (region.EntryCheckpoint == null && region.GetComponentsInChildren<CheckpointFlag>(true).Length != 1)
                    throw new InvalidOperationException(module.DisplayName + " 请指定唯一入口复活点。");
                if (region.GetComponentInChildren<LevelEndpoint>(true) == null)
                    throw new InvalidOperationException(module.DisplayName + " 缺少终点。");
                LevelOutlineBuilder.Save(module.Level);
            }
            AssetDatabase.SaveAssets();
            LevelPrefabBuilder.EnsureFolder("Assets/Scenes/WorldMaps");
            string play = AssetDatabase.GUIDToAssetPath(map.PlaySceneGuid);
            if (string.IsNullOrEmpty(play))
            {
                play = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/WorldMaps/" + WorldMapPrefabBuilder.SafeName(map.name) + "_Play.unity");
                SaveScene(play, scene =>
                {
                    var launch = new GameObject("World Gameplay").AddComponent<WorldMapGameplayBootstrap>();
                    launch.World = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(mapPath);
                    launch.RuntimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LevelTestSceneBuilder.RuntimePrefabPath);
                });
            }
            map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(mapPath);
            map.PlaySceneGuid = AssetDatabase.AssetPathToGUID(play);
            map.PlayScenePath = play;
            EditorUtility.SetDirty(map); AssetDatabase.SaveAssetIfDirty(map);
            string selection = SaveSelection(map);
            if (openScene && !Application.isBatchMode) EditorSceneManager.OpenScene(selection, OpenSceneMode.Single);
            return selection;
        }

        public static string SaveSelection(WorldMapDefinition map)
        {
            string mapPath = AssetDatabase.GetAssetPath(map);
            string selection = AssetDatabase.GUIDToAssetPath(map.SelectionSceneGuid);
            if (string.IsNullOrEmpty(selection)) selection = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/Scenes/WorldMaps/" + WorldMapPrefabBuilder.SafeName(map.name) + "_Select.unity");
            SaveScene(selection, scene =>
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.018f, 0.03f, 0.055f);
                cameraObject.AddComponent<AudioListener>();
                var menu = new GameObject("World Level Selection").AddComponent<WorldLevelMenu>();
                menu.World = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(mapPath);
            });
            map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(mapPath);
            map.SelectionSceneGuid = AssetDatabase.AssetPathToGUID(selection);
            map.SelectionScenePath = selection;
            map.ProgressId = AssetDatabase.AssetPathToGUID(mapPath);
            EditorUtility.SetDirty(map); AssetDatabase.SaveAssetIfDirty(map);
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(selection, true),
                new EditorBuildSettingsScene(map.PlayScenePath, true)
            };
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (scene.path != selection && scene.path != map.PlayScenePath) scenes.Add(scene);
            EditorBuildSettings.scenes = scenes.ToArray();
            return selection;
        }

        private static void SaveScene(string path, Action<Scene> populate)
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene loaded = SceneManager.GetSceneByPath(path);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                string.IsNullOrEmpty(previous.path) || (loaded.IsValid() && loaded.isLoaded) ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                populate(scene);
                if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("无法保存场景：" + path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
            }
        }

        public static void GenerateDefaultBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("批量生成仅用于隔离项目。");
            try
            {
                var map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
                string path = Generate(map, false);
                File.WriteAllText(Path.Combine(Application.dataPath, "../world-menu-generated.txt"), path);
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }
    }
}
