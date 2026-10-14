#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public sealed partial class WorldMapEditorWindow
    {
        private enum DesignTool { Arrange, Passage, NewRegion }
        [SerializeField] private DesignTool designTool;
        [SerializeField] private string selectedPassageId;
        [SerializeField] private bool showPassages = true;
        private bool drawingRegion, hasRegion, previousHasRegion, draggingPassage;
        private RectInt regionArea, previousRegionArea;
        private Vector2Int regionAnchor;
        private LevelPalette regionPalette;
        private WorldMapPassage SelectedPassage => map?.Passages?.Find(passage => passage != null && passage.Id == selectedPassageId);
        private string ToolName => designTool == DesignTool.Passage ? "通行标记 [P]" : designTool == DesignTool.NewRegion ? "框选新关卡 [B]" : "拼装移动 [V]";

        private void SetDesignTool(DesignTool next)
        {
            CancelGesture(); pendingLevel = null;
            designTool = next;
            if (next != DesignTool.NewRegion) hasRegion = false;
            if (next != DesignTool.Passage) selectedPassageId = null;
            if (next == DesignTool.Passage) showPassages = true;
            if (next == DesignTool.NewRegion && regionPalette == null)
                regionPalette = Selected?.Level?.Palette ?? AssetDatabase.LoadAssetAtPath<LevelPalette>(LevelEditorAssets.DefaultPalettePath);
            GUIUtility.keyboardControl = 0;
            Repaint();
        }

        private void SelectModule(WorldMapModule module)
        {
            CancelGesture();
            SelectSingleModule(module); selectedPassageId = null;
            designTool = DesignTool.Arrange; hasRegion = false;
        }

        private void DrawDesignTools()
        {
            EditorGUILayout.LabelField("设计工具", EditorStyles.boldLabel);
            DesignTool next = (DesignTool)GUILayout.Toolbar((int)designTool, new[] { "拼装 [V]", "通行 [P]", "框选 [B]" });
            if (next != designTool) SetDesignTool(next);
            showPassages = EditorGUILayout.Toggle("显示通行标记", showPassages);
            if (!showPassages && SelectedPassage != null) selectedPassageId = null;
            if (designTool == DesignTool.Passage)
                EditorGUILayout.HelpBox("点击画布放置通行箭头，在侧栏调整朝向、填写备注。点击已有箭头可选择并拖动。", MessageType.None);
            if (designTool == DesignTool.NewRegion) DrawRegionSidebar();
            if (SelectedPassage != null) DrawPassageSidebar();
            if (showPassages && map.Passages != null && map.Passages.Count > 0)
            {
                EditorGUILayout.LabelField("通行标记（" + map.Passages.Count + "）", EditorStyles.boldLabel);
                for (int i = 0; i < map.Passages.Count; i++)
                {
                    WorldMapPassage passage = map.Passages[i]; if (passage == null) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("P" + (i + 1) + " " + (string.IsNullOrWhiteSpace(passage.Note) ? "通行箭头" : passage.Note.Replace('\n', ' ')), EditorStyles.miniButton))
                        { CancelGesture(); selectedPassageId = passage.Id; designTool = DesignTool.Arrange; }
                        if (GUILayout.Button("定位", GUILayout.Width(42f)))
                        {
                            CancelGesture(); designTool = DesignTool.Arrange; hasRegion = false;
                            selectedPassageId = passage.Id;
                            Vector2 center = passage.Position + Vector2.one * 0.5f;
                            pan = new Vector2(position.width - SidebarWidth, position.height - 48f) * 0.5f -
                                new Vector2(center.x, map.Size.y - center.y) * cellPixels;
                            Repaint();
                        }
                    }
                }
            }
            GUILayout.Space(8f);
        }

        private void DrawPassageSidebar()
        {
            WorldMapPassage passage = SelectedPassage;
            EditorGUILayout.LabelField("选中的通行标记", EditorStyles.boldLabel);
            Vector2Int point = EditorGUILayout.Vector2IntField("箭头坐标", passage.Position);
            float angle = EditorGUILayout.Slider("箭头角度", passage.Angle, 0f, 360f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("←")) angle = 180f;
                if (GUILayout.Button("↑")) angle = 90f;
                if (GUILayout.Button("↓")) angle = 270f;
                if (GUILayout.Button("→")) angle = 0f;
            }
            bool bidirectional = EditorGUILayout.Toggle("双向通行", passage.Bidirectional);
            EditorGUILayout.LabelField("备注");
            string note = EditorGUILayout.TextArea(passage.Note ?? "", GUILayout.MinHeight(48f));
            if (point != passage.Position || angle != passage.Angle || bidirectional != passage.Bidirectional || note != passage.Note)
            {
                Record("编辑通行标记");
                passage.Position = point; passage.Angle = angle;
                passage.Bidirectional = bidirectional; passage.Note = note; Changed();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("复制标记")) DuplicatePassage();
                if (GUILayout.Button("删除标记")) DeletePassage();
            }
        }

        private void AddPassage(Vector2Int cell)
        {
            Record("添加通行标记");
            if (map.Passages == null) map.Passages = new List<WorldMapPassage>();
            var passage = new WorldMapPassage { Position = cell };
            map.Passages.Add(passage); selectedPassageId = passage.Id; showPassages = true;
            Changed();
        }

        private void DuplicatePassage()
        {
            WorldMapPassage original = SelectedPassage; if (original == null) return;
            Record("复制通行标记");
            var copy = new WorldMapPassage { Position = original.Position + new Vector2Int(2, -2),
                FromModuleId = original.FromModuleId, ToModuleId = original.ToModuleId, Angle = original.Angle,
                Bidirectional = original.Bidirectional, Note = original.Note };
            map.Passages.Add(copy); selectedPassageId = copy.Id; Changed();
        }

        private void DeletePassage()
        {
            WorldMapPassage passage = SelectedPassage; if (passage == null) return;
            Record("删除通行标记"); map.Passages.Remove(passage); selectedPassageId = null; CancelGesture(); Changed();
        }

        private WorldMapPassage PickPassage(Rect canvas)
        {
            if (!showPassages || map.Passages == null) return null;
            Vector2 pointer = Event.current.mousePosition - canvas.position;
            for (int i = map.Passages.Count - 1; i >= 0; i--)
            {
                WorldMapPassage passage = map.Passages[i]; if (passage == null) continue;
                Vector2 pin = CellPoint((Vector2)passage.Position + Vector2.one * 0.5f);
                if ((pointer - pin).sqrMagnitude <= 400f) return passage;
            }
            return null;
        }

        private void DrawPassages()
        {
            if (!showPassages || map.Passages == null) return;
            Handles.BeginGUI(); Color previous = Handles.color;
            for (int i = 0; i < map.Passages.Count; i++)
            {
                WorldMapPassage passage = map.Passages[i]; if (passage == null) continue;
                Vector2Int position = passage.Position + (draggingPassage && passage == SelectedPassage ? dragDelta : Vector2Int.zero);
                Color color = WorldMapDrawing.PassageColor;
                Vector2 center = CellPoint((Vector2)position + Vector2.one * 0.5f);
                Vector2 direction = passage.ArrowDirection; direction.y = -direction.y;
                Vector2 from = center - direction * 14f, to = center + direction * 14f;
                Handles.color = passage == SelectedPassage ? Color.white : Color.black;
                DrawPassageArrow(from, to, passage.Bidirectional, 5f);
                Handles.color = color;
                DrawPassageArrow(from, to, passage.Bidirectional, 3f);
                var style = new GUIStyle(EditorStyles.miniLabel); style.normal.textColor = color;
                GUI.Label(new Rect(center.x - 20f, center.y - 20f, 40f, 40f), new GUIContent("", passage.Note));
                if (passage == SelectedPassage && !string.IsNullOrWhiteSpace(passage.Note))
                {
                    string label = passage.Note.Replace('\n', ' ');
                    var title = new Rect(center.x + 22f, center.y - 10f, Mathf.Min(360f, style.CalcSize(new GUIContent(label)).x + 8f), 20f);
                    EditorGUI.DrawRect(title, new Color(0f, 0f, 0f, 0.8f));
                    GUI.Label(title, new GUIContent(label, passage.Note), style);
                }
            }
            Handles.color = previous; Handles.EndGUI();
        }

        private static void DrawPassageArrow(Vector2 from, Vector2 to, bool bidirectional, float width)
        {
            Vector2 delta = to - from; if (delta.sqrMagnitude < 1f) return;
            Vector2 direction = delta.normalized, side = new Vector2(-direction.y, direction.x);
            Handles.DrawAAPolyLine(width, from, to);
            Handles.DrawAAPolyLine(width, to - direction * 9f + side * 5f, to, to - direction * 9f - side * 5f);
            if (bidirectional)
                Handles.DrawAAPolyLine(width, from + direction * 9f + side * 5f, from, from + direction * 9f - side * 5f);
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
                hasRegion = false; designTool = DesignTool.Arrange; selectedPassageId = null; SelectSingleModule(module);
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

        private static Vector2Int ArrowDirection(KeyCode key)
        {
            if (key == KeyCode.LeftArrow) return Vector2Int.left;
            if (key == KeyCode.RightArrow) return Vector2Int.right;
            if (key == KeyCode.UpArrow) return Vector2Int.up;
            return key == KeyCode.DownArrow ? Vector2Int.down : Vector2Int.zero;
        }
    }
}
#endif
