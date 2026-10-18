using System;
using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public static class LevelEditorMaintenance
    {
        [MenuItem("TapTap/维护/升级关卡编辑资源")]
        public static void UpgradeResources()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出运行模式。");
            LevelEditorAssets.EnsureDefaults();
            foreach (string guid in AssetDatabase.FindAssets("t:LevelPalette", new[] { "Assets/Runtime" }))
            {
                LevelPalette palette = AssetDatabase.LoadAssetAtPath<LevelPalette>(AssetDatabase.GUIDToAssetPath(guid));
                LevelEditorAssets.EnsureSpringInPalette(palette);
                LevelEditorAssets.EnsureSquarePlatformInPalette(palette);
                SmartTileAssets.EnsureInPalette(palette);
                ProgressionEditorAssets.EnsureInPalette(palette);
                if (palette.Brushes.RemoveAll(brush => brush != null && brush.HandlerId == "annotation") > 0)
                {
                    EditorUtility.SetDirty(palette);
                    AssetDatabase.SaveAssetIfDirty(palette);
                }
            }
            LevelTestSceneBuilder.EnsureRuntimePrefab();
            Debug.Log("关卡编辑资源升级完成。打开编辑器不再自动安装或迁移资源。");
        }
    }
}
