using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class LevelPrefabBuilder
    {
        public static IReadOnlyList<string> Validate(LevelDefinition layout)
        {
            var errors = new List<string>();
            if (layout == null)
            {
                errors.Add("请选择布局资源。");
                return errors;
            }
            if (layout.Size.x < 1 || layout.Size.y < 1)
                errors.Add("区域长宽必须至少为 1 格。");
            if (layout.Palette == null)
                errors.Add("布局缺少画笔目录。");
            else if (layout.Palette.UnitSize <= 0f || float.IsNaN(layout.Palette.UnitSize)
                || float.IsInfinity(layout.Palette.UnitSize))
                errors.Add("单位长度必须是大于零的有限数值。");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var cells = new HashSet<string>(StringComparer.Ordinal);
            bool entryFound = string.IsNullOrEmpty(layout.EntryPlacementId);
            if (layout.Placements == null)
            {
                errors.Add("布局缺少放置数据。");
                return errors;
            }
            foreach (LevelPlacement placement in layout.Placements)
            {
                if (placement == null)
                {
                    errors.Add("布局存在空的放置记录。");
                    continue;
                }
                string location = "格子 " + placement.Cell + "：";
                if (!layout.Contains(placement.Cell))
                    errors.Add(location + "位于区域边界外；请先移除或扩大区域。");
                if (string.IsNullOrEmpty(placement.Id) || !ids.Add(placement.Id))
                    errors.Add(location + "放置 ID 为空或重复。");
                if (placement.Brush == null)
                {
                    errors.Add(location + "缺少画笔资源。");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(placement.Brush.LayerId))
                    errors.Add(location + "画笔图层 ID 为空。");
                if (!cells.Add(placement.Cell.x + ":" + placement.Cell.y + ":" + placement.Brush.LayerId))
                    errors.Add(location + "同一图层存在重复内容。");
                LevelBrushHandler handler = LevelBrushHandlers.Get(placement.Brush.HandlerId);
                if (handler == null)
                    errors.Add(location + "找不到处理器 " + placement.Brush.HandlerId + "。");
                if (placement.Brush.Prefab == null || !PrefabUtility.IsPartOfPrefabAsset(placement.Brush.Prefab))
                {
                    errors.Add(location + "画笔需要一个已保存的 Prefab。");
                    continue;
                }
                if (!IsFinite(placement.Brush.Anchor.x) || !IsFinite(placement.Brush.Anchor.y))
                    errors.Add(location + "画笔定位偏移必须是有限数值。");
                if (handler != null)
                {
                    string detail = handler.ValidatePlacement(placement);
                    if (!string.IsNullOrEmpty(detail))
                        errors.Add(location + detail);
                    if (placement.Id == layout.EntryPlacementId)
                    {
                        entryFound = true;
                        if (!handler.IsCheckpoint)
                            errors.Add("入口必须指定为复活点。");
                    }
                }
            }
            if (!entryFound)
                errors.Add("指定的入口复活点已不存在。");
            return errors;
        }

        public static GameObject Save(LevelDefinition layout, string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode，再生成区域 Prefab。");
            IReadOnlyList<string> errors = Validate(layout);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors));
            path = NormalizePrefabPath(path);
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && existing.GetComponent<LevelRegion>() == null)
                throw new InvalidOperationException("该路径已经存在普通 Prefab，请选择其他路径。只有 LevelRegion 可被更新。");
            foreach (LevelPlacement placement in layout.Placements)
                if (AssetDatabase.GetAssetPath(placement.Brush.Prefab) == path)
                    throw new InvalidOperationException("区域不能把自己作为画笔 Prefab。");

            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            Scene preview = default;
            GameObject root = null;
            bool loaded = existing != null;
            GameObject result;
            try
            {
                if (loaded)
                    root = PrefabUtility.LoadPrefabContents(path);
                else
                {
                    preview = EditorSceneManager.NewPreviewScene();
                    root = new GameObject(Path.GetFileNameWithoutExtension(path));
                    SceneManager.MoveGameObjectToScene(root, preview);
                    root.AddComponent<LevelRegion>();
                }
                LevelRegion region = root.GetComponent<LevelRegion>();
                region.Size = layout.Size;
                region.UnitSize = layout.Palette.UnitSize;
                region.Source = layout;
                region.EntryCheckpoint = null;

                Transform generated = FindChild(root.transform, "Generated");
                if (generated == null)
                    generated = Child(root.transform, "Generated");
                // Generated is owned by the builder; Custom is left for hand placed scene content.
                for (int index = generated.childCount - 1; index >= 0; index--)
                    UnityEngine.Object.DestroyImmediate(generated.GetChild(index).gameObject);
                generated.localPosition = Vector3.zero;
                generated.localRotation = Quaternion.identity;
                generated.localScale = Vector3.one;
                if (FindChild(root.transform, "Custom") == null)
                    Child(root.transform, "Custom");

                var layers = new Dictionary<string, Transform>(StringComparer.Ordinal)
                {
                    { "geometry", Child(generated, "Geometry") },
                    { "markers", Child(generated, "Markers") }
                };
                var context = new LevelBuildContext(layout, region);
                foreach (LevelPlacement placement in layout.Placements)
                {
                    if (LevelBrushHandlers.Get(placement.Brush.HandlerId).TestOnly) continue;
                    string layerId = placement.Brush.LayerId;
                    if (!layers.TryGetValue(layerId, out Transform layer))
                    {
                        layer = Child(generated, "Layer " + layerId.Replace('/', '_').Replace('\\', '_'));
                        layers.Add(layerId, layer);
                    }
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(placement.Brush.Prefab, root.scene);
                    instance.transform.SetParent(layer, false);
                    instance.name = placement.Brush.DisplayName + " [" + placement.Cell.x + "," + placement.Cell.y
                        + "] " + placement.Id;
                    Vector2 anchor = ((Vector2)placement.Cell + placement.Brush.Anchor) * context.UnitSize;
                    instance.transform.localPosition = new Vector3(anchor.x, anchor.y, 0f);
                    instance.transform.localRotation = Quaternion.identity;
                    instance.transform.localScale = new Vector3(context.UnitSize, context.UnitSize, 1f);
                    LevelBrushHandlers.Get(placement.Brush.HandlerId).ConfigureInstance(instance, placement, context);
                    RecordInstanceOverrides(instance);
                }

                result = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (!success || result == null)
                    throw new IOException("无法保存区域 Prefab：" + path);
            }
            finally
            {
                if (loaded && root != null)
                    PrefabUtility.UnloadPrefabContents(root);
                else if (preview.IsValid())
                    EditorSceneManager.ClosePreviewScene(preview);
            }
            if (layout.OutputPrefab != result)
            {
                Undo.RegisterCompleteObjectUndo(layout, "Save Level Prefab");
                layout.OutputPrefab = result;
            }
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssetIfDirty(layout);
            return result;
        }

        public static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (folder == "Assets" || AssetDatabase.IsValidFolder(folder))
                return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || !folder.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("资源必须保存在 Assets 目录内。");
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        internal static void RecordInstanceOverrides(GameObject instance)
        {
            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
                foreach (Component component in child.GetComponents<Component>())
                    if (component != null && PrefabUtility.IsPartOfPrefabInstance(component))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
        }

        private static string NormalizePrefabPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("请选择 Prefab 保存路径。");
            string full = Path.GetFullPath(path);
            string assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!full.StartsWith(assets, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetExtension(full), ".prefab", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("区域必须保存为 Assets 内的 .prefab 文件。");
            return "Assets/" + full.Substring(assets.Length).Replace('\\', '/');
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static Transform FindChild(Transform parent, string name)
        {
            for (int index = 0; index < parent.childCount; index++)
                if (parent.GetChild(index).name == name)
                    return parent.GetChild(index);
            return null;
        }
    }
}
