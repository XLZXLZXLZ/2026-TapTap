using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class WorldMapPrefabBuilder
    {
        public static List<string> Validate(WorldMapDefinition map)
        {
            var errors = new List<string>();
            if (map == null) { errors.Add("请选择大地图。"); return errors; }
            if (map.Modules == null || map.Modules.Count == 0) { errors.Add("大地图还没有关卡模块。"); return errors; }
            var ids = new HashSet<string>();
            var sequenceNumbers = new HashSet<int>();
            var checkedLevels = new HashSet<LevelDefinition>();
            float? unit = null;
            for (int i = 0; i < map.Modules.Count; i++)
            {
                WorldMapModule module = map.Modules[i];
                if (module?.Level == null) { errors.Add("第 " + (i + 1) + " 个模块缺少关卡。"); continue; }
                if (string.IsNullOrEmpty(module.Id) || !ids.Add(module.Id)) errors.Add(module.DisplayName + "：模块 ID 为空或重复。");
                if (module.Sequence < 0 || !sequenceNumbers.Add(map.SequenceNumber(module)))
                    errors.Add(module.DisplayName + "：关卡序号为负数或重复，请设置唯一的正数序号。");
                for (int j = 0; j < i; j++)
                    if (map.Modules[j]?.Level != null && WorldMapOverlap.HasConflict(module.Level, module.Level.Footprint, module.Position,
                        map.Modules[j].Level, map.Modules[j].Level.Footprint, map.Modules[j].Position))
                        errors.Add(module.DisplayName + " 与 " + map.Modules[j].DisplayName + " 的重叠格块类型不一致。");
                if (checkedLevels.Add(module.Level))
                {
                    foreach (string error in LevelPrefabBuilder.Validate(module.Level)) errors.Add(module.DisplayName + "：" + error);
                    float current = module.Level.Palette != null ? module.Level.Palette.UnitSize : 1f;
                    if (unit.HasValue && !Mathf.Approximately(unit.Value, current)) errors.Add("关卡单位长度不一致，不能按统一网格拼装。");
                    if (!unit.HasValue) unit = current;
                }
            }
            return errors;
        }

        public static GameObject Save(WorldMapDefinition map, string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            List<string> errors = Validate(map);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            path = path.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("大地图 Prefab 必须保存在 Assets 下。");
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && existing.GetComponent<WorldMapRoot>() == null)
                throw new InvalidOperationException("该路径是其他类型的 Prefab，请选择新的路径。");
            LevelPrefabBuilder.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            // Each source remains a linked level Prefab, with its Custom contents preserved.
            var prefabs = new Dictionary<LevelDefinition, GameObject>();
            foreach (WorldMapModule module in map.Modules)
                if (!prefabs.ContainsKey(module.Level))
                {
                    string levelPath = AssetDatabase.GetAssetPath(module.Level.OutputPrefab);
                    if (string.IsNullOrEmpty(levelPath))
                        levelPath = AssetDatabase.GenerateUniqueAssetPath("Assets/Prefabs/Levels/" + SafeName(module.Level.name) + ".prefab");
                    prefabs.Add(module.Level, LevelPrefabBuilder.Save(module.Level, levelPath));
                }
            Scene preview = default;
            GameObject root = null;
            bool loaded = existing != null;
            GameObject result;
            try
            {
                if (loaded) root = PrefabUtility.LoadPrefabContents(path);
                else
                {
                    preview = EditorSceneManager.NewPreviewScene();
                    root = new GameObject(Path.GetFileNameWithoutExtension(path));
                    SceneManager.MoveGameObjectToScene(root, preview);
                    root.AddComponent<WorldMapRoot>();
                }
                root.GetComponent<WorldMapRoot>().Source = map;
                Transform generated = root.transform.Find("Generated");
                if (generated == null) { generated = new GameObject("Generated").transform; generated.SetParent(root.transform, false); }
                for (int i = generated.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(generated.GetChild(i).gameObject);
                generated.localPosition = Vector3.zero;
                generated.localRotation = Quaternion.identity;
                generated.localScale = Vector3.one;
                if (root.transform.Find("Custom") == null) new GameObject("Custom").transform.SetParent(root.transform, false);
                foreach (WorldMapModule module in map.Modules)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[module.Level], root.scene);
                    instance.transform.SetParent(generated, false);
                    instance.name = module.DisplayName + " [" + module.Position.x + "," + module.Position.y + "] " + module.Id;
                    instance.transform.localPosition = (Vector2)module.Position * module.Level.Palette.UnitSize;
                    LevelRegion region = instance.GetComponent<LevelRegion>();
                    region.WorldModuleId = module.Id;
                    region.SequenceNumber = map.SequenceNumber(module);
                    LevelPrefabBuilder.RecordInstanceOverrides(instance);
                }
                result = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (!success || result == null) throw new IOException("大地图 Prefab 保存失败。");
            }
            finally
            {
                if (loaded && root != null) PrefabUtility.UnloadPrefabContents(root);
                else if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            }
            Undo.RecordObject(map, "生成大地图 Prefab");
            map.OutputPrefab = result;
            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssetIfDirty(map);
            return result;
        }

        public static string SafeName(string name)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(name) ? "WorldMap" : name;
        }
    }
}
