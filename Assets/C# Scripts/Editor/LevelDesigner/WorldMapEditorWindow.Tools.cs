#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public sealed partial class WorldMapEditorWindow
    {
        private enum DesignTool { Arrange, NewRegion }
        [SerializeField] private DesignTool designTool;
        private bool drawingRegion, hasRegion, previousHasRegion;
        private RectInt regionArea, previousRegionArea;
        private Vector2Int regionAnchor;
        private LevelPalette regionPalette;
        private string ToolName => designTool == DesignTool.NewRegion ? "框选新关卡 [B]" : "拼装移动 [V]";

        private void SetDesignTool(DesignTool next)
        {
            CancelGesture(); pendingLevel = null;
            designTool = next;
            if (next != DesignTool.NewRegion) hasRegion = false;
            if (next == DesignTool.NewRegion && regionPalette == null)
                regionPalette = Selected?.Level?.Palette ?? AssetDatabase.LoadAssetAtPath<LevelPalette>(LevelEditorAssets.DefaultPalettePath);
            GUIUtility.keyboardControl = 0;
            Repaint();
        }

        private void SelectModule(WorldMapModule module)
        {
            CancelGesture();
            SelectSingleModule(module);
            designTool = DesignTool.Arrange; hasRegion = false;
        }

        private void DrawDesignTools()
        {
            EditorGUILayout.LabelField("设计工具", EditorStyles.boldLabel);
            DesignTool next = (DesignTool)GUILayout.Toolbar((int)designTool, new[] { "拼装 [V]", "框选 [B]" });
            if (next != designTool) SetDesignTool(next);
            if (designTool == DesignTool.NewRegion) DrawRegionSidebar();
            GUILayout.Space(8f);
        }

        private void DrawRegionSidebar()
        {
            EditorGUILayout.HelpBox("左键拖框指定新关卡的画布大小和位置，可与已有关卡重叠。创建后进入单关编辑器，轮廓随实际图块自动生成。Esc 取消。", MessageType.None);
            if (!hasRegion) return;
            Vector2Int origin = EditorGUILayout.Vector2IntField("范围左下角", regionArea.min);
            Vector2Int size = EditorGUILayout.Vector2IntField("新关卡大小（格）", regionArea.size);
            if (!drawingRegion) regionArea = new RectInt(origin, new Vector2Int(Mathf.Clamp(size.x, 1, 2048), Mathf.Clamp(size.y, 1, 2048)));
            if (regionPalette == null) regionPalette = Selected?.Level?.Palette ?? AssetDatabase.LoadAssetAtPath<LevelPalette>(LevelEditorAssets.DefaultPalettePath);
            regionPalette = (LevelPalette)EditorGUILayout.ObjectField("物体配置", regionPalette, typeof(LevelPalette), false);
            string problem = WorldMapRegionCreator.Validate(map, regionArea);
            if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Warning);
            else EditorGUILayout.LabelField("将创建 " + regionArea.width + " × " + regionArea.height + " 格的子关卡", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUI.DisabledScope(drawingRegion || problem != null || regionPalette == null || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("确定创建子关卡并进入编辑器", GUILayout.Height(28f))) CreateRegionLevel();
            if (GUILayout.Button("取消框选")) { CancelGesture(); hasRegion = false; Repaint(); }
        }

        private void CreateRegionLevel()
        {
            string problem = WorldMapRegionCreator.Validate(map, regionArea);
            if (problem != null) { error = problem; return; }
            LevelPrefabBuilder.EnsureFolder("Assets/Runtime/Levels");
            string path = EditorUtility.SaveFilePanelInProject("创建框选子关卡", "Level_New", "asset", "保存新关卡布局", "Assets/Runtime/Levels");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                WorldMapModule module = WorldMapRegionCreator.Create(map, regionArea, regionPalette, path);
                hasRegion = false; designTool = DesignTool.Arrange; SelectSingleModule(module);
                RefreshLibrary(); Changed();
                LevelEditorWindow.Open(module.Level);
            }
            catch (Exception exception) { error = exception.Message; }
        }

        private void DrawRegionSelection()
        {
            if (!hasRegion || designTool != DesignTool.NewRegion) return;
            Rect rect = CellRect(regionArea);
            Color color = new Color(0.3f, 1f, 0.65f);
            EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.16f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), color);
            var label = new Rect(rect.x, rect.y - 23f, Mathf.Max(180f, rect.width), 22f);
            EditorGUI.DrawRect(label, new Color(0f, 0f, 0f, 0.8f));
            GUI.Label(label, regionArea.width + " × " + regionArea.height + " 格 · 松开后在右侧确认创建");
        }

    }
}
#endif
