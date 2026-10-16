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
            if (map == null || map.OrderedModules().Count == 0) throw new InvalidOperationException("大世界没有可用关卡。");
            if (openScene && !Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
            WorldMapSceneSync.Synchronize(map);
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
                SceneCameraDefaults.EnablePostProcessing(camera);
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
            bool alreadyOpen = loaded.IsValid() && loaded.isLoaded;
            Scene scene = alreadyOpen ? loaded : File.Exists(path) ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (root.GetComponent<WorldLevelMenu>() != null || (root.name == "Main Camera" && root.GetComponent<Camera>() != null))
                        UnityEngine.Object.DestroyImmediate(root);
                populate(scene);
                if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("无法保存场景：" + path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (!alreadyOpen && scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
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
