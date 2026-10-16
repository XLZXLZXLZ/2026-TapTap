using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    // Generated visuals are ordinary clones, so prefab reimports must rebuild them explicitly.
    public sealed class LevelStyleAssetRefresh : AssetPostprocessor
    {
        private static readonly HashSet<string> ChangedPaths = new HashSet<string>();

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported) ChangedPaths.Add(path);
            foreach (string path in deleted) ChangedPaths.Add(path);
            foreach (string path in moved) ChangedPaths.Add(path);
            foreach (string path in movedFrom) ChangedPaths.Add(path);
            EditorApplication.delayCall -= RefreshChangedModels;
            EditorApplication.delayCall += RefreshChangedModels;
        }

        private static void RefreshChangedModels()
        {
            var changed = new HashSet<string>(ChangedPaths);
            ChangedPaths.Clear();
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var affectedStyles = new Dictionary<LevelStyle, bool>();
            foreach (LevelStyleView view in Resources.FindObjectsOfTypeAll<LevelStyleView>())
            {
                if (!view.gameObject.scene.IsValid()) continue;
                LevelStyle style = view.GetComponent<LevelRegion>().Source?.Style;
                if (style == null) continue;
                if (!affectedStyles.TryGetValue(style, out bool affected))
                {
                    affected = DependsOnChangedAsset(style, changed);
                    affectedStyles.Add(style, affected);
                }
                if (affected) view.Refresh(true);
            }
            // Also update the standalone brick prefab preview, which has no LevelRegion.
            foreach (TerrainTileVisual tile in Resources.FindObjectsOfTypeAll<TerrainTileVisual>())
            {
                if (!tile.gameObject.scene.IsValid() || tile.GetComponentInParent<LevelRegion>(true) != null) continue;
                if (DependsOnChangedAsset(tile.DefaultModel, changed)) tile.ApplyModel(tile.DefaultModel, true);
            }
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static bool DependsOnChangedAsset(Object asset, HashSet<string> changed)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return false;
            foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                if (changed.Contains(dependency)) return true;
            return false;
        }
    }
}
