#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public sealed partial class LevelEditorWindow
    {
        private static readonly Vector2Int[] ExpansionDirections =
            { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
        private static readonly string[] ExpansionArrows = { "←", "→", "↑", "↓" };
        private static readonly int[] ExpansionSteps = { 1, 4, 8, 16 };
        [SerializeField] private int expansionStepIndex = 1;
        private int gridControlId;
        private bool expandingCanvas;
        private Vector2 expansionPointerStart;
        private Vector2Int expansionDirection;
        private int expansionCount;

        private void DrawMapMirrorSidebar()
        {
            EditorGUILayout.LabelField("快速镜像", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(layout == null || painting || panning || selecting || movingSelection ||
                expandingCanvas || EditorApplication.isPlayingOrWillChangePlaymode))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("↔ 左右镜像", "以当前画布中线翻转整张地图的所有图层，Ctrl+Z 撤销"))) ApplyMapMirror(true);
                if (GUILayout.Button(new GUIContent("↕ 上下镜像", "以当前画布中线翻转整张地图的所有图层，Ctrl+Z 撤销"))) ApplyMapMirror(false);
            }
            EditorGUILayout.Space(6f);
        }

        private void ApplyMapMirror(bool horizontal)
        {
            if (layout == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            EndStroke(); CancelCanvasExpansion();
            selecting = movingSelection = false;
            selectionMoveDelta = Vector2Int.zero;
            if (!LevelGridEditing.Mirror(layout, horizontal, () => RecordLayoutUndo(horizontal ? "左右镜像地图" : "上下镜像地图"))) return;
            if (selectedCell.HasValue) selectedCell = LevelGridEditing.MirrorCell(layout.CanvasBounds, selectedCell.Value, horizontal);
            if (hasAreaSelection)
            {
                Vector2Int first = LevelGridEditing.MirrorCell(layout.CanvasBounds, selectionArea.min, horizontal);
                Vector2Int last = LevelGridEditing.MirrorCell(layout.CanvasBounds, selectionArea.max - Vector2Int.one, horizontal);
                selectionArea = new RectInt(Vector2Int.Min(first, last), selectionArea.size);
            }
            Changed();
        }

        private void DrawCanvasExpansionSidebar()
        {
            EditorGUILayout.LabelField("画布边缘", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("拖动蓝色边缘：向外扩展，向内回缩，最小保留 1 格。已有图块和大地图位置保持不动；回缩越过的图块保留，重新扩展可恢复显示。下方按钮用于扩展。", MessageType.None);
            expansionStepIndex = EditorGUILayout.Popup("每次扩展", Mathf.Clamp(expansionStepIndex, 0, ExpansionSteps.Length - 1),
                new[] { "1 格", "4 格", "8 格", "16 格" });
            using (new EditorGUI.DisabledScope(layout == null || expandingCanvas || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("← 向左扩展")) ApplyCanvasExpansion(Vector2Int.left, ExpansionSteps[expansionStepIndex]);
                    if (GUILayout.Button("向右扩展 →")) ApplyCanvasExpansion(Vector2Int.right, ExpansionSteps[expansionStepIndex]);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("↑ 向上扩展")) ApplyCanvasExpansion(Vector2Int.up, ExpansionSteps[expansionStepIndex]);
                    if (GUILayout.Button("向下扩展 ↓")) ApplyCanvasExpansion(Vector2Int.down, ExpansionSteps[expansionStepIndex]);
                }
            }
            EditorGUILayout.Space(6f);
        }

        private static Rect ExpansionEdgeRect(Rect bounds, Vector2Int direction)
        {
            if (direction.x < 0) return new Rect(bounds.xMin - 10f, bounds.yMin, 10f, bounds.height);
            if (direction.x > 0) return new Rect(bounds.xMax, bounds.yMin, 10f, bounds.height);
            if (direction.y > 0) return new Rect(bounds.xMin, bounds.yMin - 10f, bounds.width, 10f);
            return new Rect(bounds.xMin, bounds.yMax, bounds.width, 10f);
        }

        private void DrawCanvasExpansion(Rect canvas, Rect bounds)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Color color = new Color(0.35f, 0.85f, 0.95f);
            for (int i = 0; i < ExpansionDirections.Length; i++)
            {
                Vector2Int direction = ExpansionDirections[i];
                Rect edge = ExpansionEdgeRect(bounds, direction);
                if (!edge.Overlaps(new Rect(Vector2.zero, canvas.size))) continue;
                EditorGUI.DrawRect(edge, new Color(color.r, color.g, color.b, 0.14f));
                Vector2 center = edge.center;
                if (direction.x != 0) center.y = (Mathf.Max(0f, edge.yMin) + Mathf.Min(canvas.height, edge.yMax)) * 0.5f;
                else center.x = (Mathf.Max(0f, edge.xMin) + Mathf.Min(canvas.width, edge.xMax)) * 0.5f;
                Rect handle = direction.x != 0 ? new Rect(center.x - 5f, center.y - 16f, 10f, 32f)
                    : new Rect(center.x - 16f, center.y - 5f, 32f, 10f);
                EditorGUI.DrawRect(handle, color);
                GUI.Label(new Rect(center - Vector2.one * 12f, Vector2.one * 24f),
                    new GUIContent(ExpansionArrows[i], "向外拖动扩展，向内拖动回缩；Esc 取消"), CenterStyle());
            }
            if (!expandingCanvas) return;
            RectInt next = LevelCanvasEditing.ResizedBounds(layout.CanvasBounds, expansionDirection, expansionCount);
            Rect preview = new Rect(bounds.position + new Vector2(next.xMin - layout.CanvasBounds.xMin,
                layout.CanvasBounds.yMax - next.yMax) * cellPixels, (Vector2)next.size * cellPixels);
            Outline(preview, Color.yellow, 2f);
            GUI.Label(new Rect(Event.current.mousePosition.x + 12f, Event.current.mousePosition.y + 12f, 260f, 22f),
                (expansionCount < 0 ? "回缩 " : "扩展 ") + Mathf.Abs(expansionCount) + " 格 · 松开确认 · Esc 取消", EditorStyles.helpBox);
        }

        private bool HandleCanvasExpansion(Rect canvas)
        {
            if (layout == null || EditorApplication.isPlayingOrWillChangePlaymode) return false;
            Event e = Event.current;
            if (expandingCanvas && GUIUtility.hotControl == gridControlId)
            {
                if (e.type == EventType.MouseDrag || e.type == EventType.MouseUp)
                {
                    Vector2 direction = new Vector2(expansionDirection.x, -expansionDirection.y);
                    int requested = Mathf.RoundToInt(Vector2.Dot(e.mousePosition - expansionPointerStart, direction) / cellPixels);
                    RectInt next = LevelCanvasEditing.ResizedBounds(layout.CanvasBounds, expansionDirection, requested);
                    expansionCount = expansionDirection.x != 0 ? next.width - layout.Size.x : next.height - layout.Size.y;
                    if (e.type == EventType.MouseUp)
                    {
                        Vector2Int side = expansionDirection; int count = expansionCount;
                        CancelCanvasExpansion();
                        ApplyCanvasExpansion(side, count);
                    }
                    e.Use(); Repaint(); return true;
                }
                if (e.type == EventType.ScrollWheel) { e.Use(); return true; }
                return false;
            }
            if (painting || panning || selecting || movingSelection || e.alt || spacePan) return false;
            Rect bounds = new Rect(pan.x, pan.y, layout.Size.x * cellPixels, layout.Size.y * cellPixels);
            foreach (Vector2Int direction in ExpansionDirections)
            {
                Rect edge = ExpansionEdgeRect(bounds, direction); edge.position += canvas.position;
                EditorGUIUtility.AddCursorRect(edge, direction.x != 0 ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);
                if (e.type != EventType.MouseDown || e.button != 0 || GUIUtility.hotControl != 0 ||
                    !canvas.Contains(e.mousePosition) || !edge.Contains(e.mousePosition)) continue;
                EndStroke();
                expandingCanvas = true; expansionDirection = direction;
                expansionPointerStart = e.mousePosition; expansionCount = 0;
                GUIUtility.keyboardControl = 0;
                GUIUtility.hotControl = gridControlId;
                e.Use(); Repaint(); return true;
            }
            return false;
        }

        private void ApplyCanvasExpansion(Vector2Int direction, int count)
        {
            if (layout == null || count == 0) return;
            EndStroke(); selecting = movingSelection = false;
            RectInt previous = layout.CanvasBounds;
            if (!LevelCanvasEditing.Resize(layout, direction, count, () => RecordLayoutUndo(count < 0 ? "回缩关卡画布" : "扩展关卡画布"))) return;
            pan += new Vector2(layout.CanvasBounds.xMin - previous.xMin, previous.yMax - layout.CanvasBounds.yMax) * cellPixels;
            if (hasAreaSelection)
            {
                Vector2Int min = Vector2Int.Max(selectionArea.min, layout.CanvasBounds.min);
                Vector2Int max = Vector2Int.Min(selectionArea.max, layout.CanvasBounds.max);
                hasAreaSelection = max.x > min.x && max.y > min.y;
                selectionArea = hasAreaSelection ? new RectInt(min, max - min) : default;
            }
            Changed();
        }

        private void CancelCanvasExpansion()
        {
            if (expandingCanvas && GUIUtility.hotControl == gridControlId) GUIUtility.hotControl = 0;
            expandingCanvas = false; expansionCount = 0;
        }
    }
}
#endif
