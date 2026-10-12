using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class LevelSceneNavigation
    {
        private static readonly Dictionary<LevelDefinition, string> cache = new Dictionary<LevelDefinition, string>();
        static LevelSceneNavigation() { EditorApplication.projectChanged += cache.Clear; }
        public static void Assign(LevelDefinition level, SceneAsset scene)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            Undo.RecordObject(level, "绑定关卡导航场景");
            level.TestSceneGuid = scene != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(scene)) : "";
            cache.Remove(level);
            EditorUtility.SetDirty(level);
        }

        public static string FindScenePath(LevelDefinition level)
        {
            if (level == null) return null;
            string bound = AssetDatabase.GUIDToAssetPath(level.TestSceneGuid ?? "");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(bound) != null) return bound;
            if (cache.TryGetValue(level, out string cached) && (string.IsNullOrEmpty(cached) || AssetDatabase.LoadAssetAtPath<SceneAsset>(cached) != null)) return cached;
            string source = AssetDatabase.GetAssetPath(level);
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(source))
                foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Array.IndexOf(AssetDatabase.GetDependencies(path, true), source) >= 0) candidates.Add(path);
                }
            string expected = (level.OutputPrefab != null ? level.OutputPrefab.name : level.name) + "_Test";
            candidates.Sort((a, b) =>
            {
                int rankA = Path.GetFileNameWithoutExtension(a) == expected ? 0 : a.StartsWith(LevelTestSceneBuilder.TestSceneFolder + "/") ? 1 : 2;
                int rankB = Path.GetFileNameWithoutExtension(b) == expected ? 0 : b.StartsWith(LevelTestSceneBuilder.TestSceneFolder + "/") ? 1 : 2;
                return rankA != rankB ? rankA.CompareTo(rankB) : string.Compare(a, b, StringComparison.Ordinal);
            });
            return cache[level] = candidates.Count > 0 ? candidates[0] : null;
        }

        public static bool Open(LevelDefinition level, string moduleId = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 Play Mode 后导航场景。");
            if (level == null) return false;
            string sourcePath = AssetDatabase.GetAssetPath(level);
            string path = FindScenePath(level);
            Scene scene = string.IsNullOrEmpty(path) ? default : SceneManager.GetSceneByPath(path);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
                if (string.IsNullOrEmpty(path)) path = LevelTestSceneBuilder.Generate(level, false);
                if (string.IsNullOrEmpty(path)) return false;
                scene = SceneManager.GetSceneByPath(path);
                if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            }
            SceneManager.SetActiveScene(scene);
            // Saving/importing a freshly created asset can replace its native object.
            if (!string.IsNullOrEmpty(sourcePath)) level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(sourcePath);
            if (level == null) throw new InvalidOperationException("导航关卡的来源资源已丢失。");
            if (AssetDatabase.GUIDToAssetPath(level.TestSceneGuid ?? "") != path)
                Assign(level, AssetDatabase.LoadAssetAtPath<SceneAsset>(path));
            LevelRegion target = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (LevelRegion region in root.GetComponentsInChildren<LevelRegion>(true))
                    if (region.Source == level)
                    {
                        if (target == null) target = region;
                        if (!string.IsNullOrEmpty(moduleId) && region.name.EndsWith(" " + moduleId, StringComparison.Ordinal))
                        { Focus(region); return true; }
                    }
            if (target != null) { Focus(target); return true; }
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            return true;
        }

        private static void Focus(LevelRegion region)
        {
            Selection.activeGameObject = region.gameObject;
            EditorGUIUtility.PingObject(region.gameObject);
            if (!Application.isBatchMode)
            {
                SceneView view = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView : EditorWindow.GetWindow<SceneView>();
                view.Show();
                view.Frame(region.WorldBounds, false);
                view.Focus();
            }
        }
    }
}
