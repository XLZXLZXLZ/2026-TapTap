#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace TapTap.Editor
{
    public sealed class LevelEditorWindow : EditorWindow
    {
        private const float SidebarWidth = 285f;
        private const float GridMargin = 27f;
        private const float ToolbarHeight = 46f;
        private enum GridTool { Brush, Eraser, Rectangle, RectangleOutline, Bucket, Select, Stamp }
        private static readonly GUIContent[] ToolLabels =
        {
            new GUIContent("笔刷", "按住左键连续绘制"), new GUIContent("橡皮擦", "擦除当前图层，Shift 擦除整格"),
            new GUIContent("矩形填充", "拖出矩形；右键拖动可批量擦除"), new GUIContent("矩形边框", "只绘制矩形边缘"),
            new GUIContent("油漆桶 [F]", "填充当前图层中四方向连通的同类格子；右键擦除"), new GUIContent("选择 [V]", "点击配置物体，拖动选择区域，Ctrl+C 复制"),
            new GUIContent("粘贴图章", "Ctrl+V 后点击目标格子放置；Esc 退出")
        };
        [SerializeField] private LevelDefinition layout;
        [SerializeField] private LevelPalette palette;
        [SerializeField] private int brushIndex;
        [SerializeField] private LevelBrush sampledBrush;
        [SerializeField] private GridTool tool;
        [SerializeField] private Vector2 pan = new Vector2(GridMargin, GridMargin);
        [SerializeField] private float cellPixels = 24f;
        [SerializeField] private string selectedPlacementId;
        [SerializeReference] private LevelPlacementSettings paintSettings;
        [SerializeField] private LevelBrush settingsBrush;
        private readonly Dictionary<Vector2Int, List<LevelPlacement>> cells =
            new Dictionary<Vector2Int, List<LevelPlacement>>();
        private readonly HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        private bool cacheDirty = true;
        private bool fitPending = true;
        private bool painting;
        private bool panning;
        private int paintButton;
        private GridTool strokeTool;
        private Vector2Int? rectangleStart;
        private Vector2Int rectangleEnd;
        private string strokeLayerId;
        private int undoGroup = -1;
        private Vector2Int? previousPaintCell;
        private Vector2Int? selectedCell;
        private Vector2 sidebarScroll;
        private string saveError;
        [SerializeField] private string brushSearch = "";
        [SerializeField] private bool isolateLayer;
        [SerializeField] private RectInt selectionArea;
        [SerializeField] private bool hasAreaSelection;
        private Vector2Int selectionAnchor;
        private bool selecting;
        private bool spacePan;
        [SerializeField] private bool prefabNeedsUpdate;
        [SerializeField] private bool previewPhaseActive;
        private readonly List<LevelPlacement> clipboard = new List<LevelPlacement>();
        private int clipboardEntryIndex = -1;
        private bool clipboardIsCut;

        private static string PreferenceKey => "TapTap.LevelDesigner.LastLayout." + Application.dataPath;
        private bool SelectionMode => tool == GridTool.Select;
        private bool RectangleStroke => rectangleStart.HasValue;
        private LevelBrush Brush => sampledBrush != null ? sampledBrush :
            palette != null && palette.Brushes != null && brushIndex >= 0 && brushIndex < palette.Brushes.Count
                ? palette.Brushes[brushIndex] : null;

        [MenuItem("TapTap/Level Designer/Open")]
        public static void Open()
        {
            GetWindow<LevelEditorWindow>("区域编辑器").Show();
        }

        public static void Open(LevelDefinition definition)
        {
            var window = GetWindow<LevelEditorWindow>("区域编辑器");
            window.UseLayout(definition);
            window.Show();
        }

        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            var definition = EditorUtility.InstanceIDToObject(instanceId) as LevelDefinition;
            if (definition == null) return false;
            Open(definition);
            return true;
        }

        private void OnEnable()
        {
            if (tool == GridTool.Stamp && clipboard.Count == 0) tool = GridTool.Select;
            minSize = new Vector2(720f, 450f);
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.projectChanged += OnProjectChanged;
            if (layout == null)
            {
                layout = AssetDatabase.LoadAssetAtPath<LevelDefinition>(
                    EditorPrefs.GetString(PreferenceKey, "Assets/Runtime/Levels/Level_Example.asset"));
            }
            if (layout != null) palette = layout.Palette;
            if (palette == null) UseDefaultPalette();
            cacheDirty = true;
            fitPending = true;
        }

        private void OnDisable()
        {
            EndStroke();
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.projectChanged -= OnProjectChanged;
            RememberLayout();
        }

        private void OnFocus()
        {
            cacheDirty = true;
            Repaint();
        }

        private void OnLostFocus()
        {
            EndStroke();
            panning = false;
            selecting = false;
            spacePan = false;
            GUIUtility.hotControl = 0;
        }

        private void OnProjectChanged()
        {
            cacheDirty = true;
            Repaint();
        }

        private void OnUndoRedo()
        {
            if (painting || panning) GUIUtility.hotControl = 0;
            painting = false;
            panning = false;
            selecting = false;
            undoGroup = -1;
            previousPaintCell = null;
            rectangleStart = null;
            visited.Clear();
            if (layout != null) palette = layout.Palette;
            sampledBrush = null;
            settingsBrush = null;
            if (palette == null || palette.Brushes == null || brushIndex >= palette.Brushes.Count) brushIndex = 0;
            LevelPlacement selected = SelectedPlacement();
            if (selected != null)
            {
                selectedCell = selected.Cell;
                selectionArea = new RectInt(selected.Cell, Vector2Int.one);
                hasAreaSelection = true;
            }
            else hasAreaSelection = false;
            cacheDirty = true;
            saveError = null;
            Repaint();
        }

        private void RememberLayout()
        {
            if (layout != null) EditorPrefs.SetString(PreferenceKey, AssetDatabase.GetAssetPath(layout));
        }

        private void UseLayout(LevelDefinition definition)
        {
            EndStroke();
            layout = definition;
            if (layout != null) palette = layout.Palette;
            if (palette == null) UseDefaultPalette();
            sampledBrush = null;
            settingsBrush = null;
            selectedPlacementId = null;
            selectedCell = null;
            hasAreaSelection = false;
            cacheDirty = true;
            fitPending = true;
            saveError = null;
            prefabNeedsUpdate = false;
            RememberLayout();
            Repaint();
        }

        private void UseDefaultPalette()
        {
            palette = AssetDatabase.LoadAssetAtPath<LevelPalette>(LevelEditorAssets.DefaultPalettePath)
                ?? LevelEditorAssets.EnsureDefaults();
            sampledBrush = null;
            brushIndex = 0;
            settingsBrush = null;
            if (layout != null && layout.Palette != palette)
            {
                Undo.RecordObject(layout, "载入默认笔刷");
                layout.Palette = palette;
                EditorUtility.SetDirty(layout);
            }
        }

        private void OnGUI()
        {
            if (layout != null && palette != layout.Palette)
            {
                EndStroke();
                palette = layout.Palette;
                sampledBrush = null;
                settingsBrush = null;
                brushIndex = 0;
                cacheDirty = true;
            }
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                EndStroke();
                panning = false;
                selecting = false;
                if (tool == GridTool.Stamp) tool = GridTool.Select;
                GUIUtility.hotControl = 0;
                Event.current.Use();
                Repaint();
            }
            HandleShortcuts();
            GUILayout.BeginArea(new Rect(0f, 0f, position.width, ToolbarHeight));
            DrawToolbar();
            GUILayout.EndArea();
            // Reserve the sidebar from the actual window size, independently of toolbar layout widths.
            Rect work = new Rect(0f, ToolbarHeight, position.width, Mathf.Max(1f, position.height - ToolbarHeight - 22f));
            float sidebarWidth = Mathf.Min(SidebarWidth, work.width * 0.45f);
            Rect canvas = new Rect(work.x, work.y, Mathf.Max(1f, work.width - sidebarWidth - 6f), work.height);
            Rect sidebar = new Rect(work.xMax - sidebarWidth, work.y, sidebarWidth, work.height);
            if (Event.current.type == EventType.Repaint && fitPending && layout != null)
            {
                Fit(canvas);
                fitPending = false;
            }
            DrawCanvas(canvas);
            GUILayout.BeginArea(sidebar, EditorStyles.helpBox);
            sidebarScroll = EditorGUILayout.BeginScrollView(sidebarScroll);
            DrawSidebar();
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
            HandleCanvas(canvas);
            DrawStatus(canvas);
        }

        private void DrawToolbar()
        {
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 64f;
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var next = (LevelDefinition)EditorGUILayout.ObjectField("布局", layout,
                typeof(LevelDefinition), false);
            if (next != layout) UseLayout(next);
            if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(48f))) NewLayout();
            using (new EditorGUI.DisabledScope(layout == null))
            {
                if (GUILayout.Button("保存布局", EditorStyles.toolbarButton, GUILayout.Width(72f))) SaveLayout();
                if (GUILayout.Button("保存区域 Prefab", EditorStyles.toolbarButton, GUILayout.Width(110f))) ExportPrefab();
                using (new EditorGUI.DisabledScope(layout == null || layout.OutputPrefab == null))
                {
                    if (GUILayout.Button("定位 Prefab", EditorStyles.toolbarButton, GUILayout.Width(85f)))
                    {
                        Selection.activeObject = layout.OutputPrefab;
                        EditorGUIUtility.PingObject(layout.OutputPrefab);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            using (new EditorGUI.DisabledScope(layout == null))
            {
                EditorGUI.BeginChangeCheck();
                int width = EditorGUILayout.IntField("宽（格）", layout != null ? layout.Size.x : 32,
                    GUILayout.MinWidth(120f));
                int height = EditorGUILayout.IntField("高（格）", layout != null ? layout.Size.y : 18,
                    GUILayout.MinWidth(120f));
                if (EditorGUI.EndChangeCheck() && layout != null)
                {
                    EndStroke();
                    Undo.RecordObject(layout, "修改区域尺寸");
                    layout.Size = new Vector2Int(Mathf.Max(1, width), Mathf.Max(1, height));
                    Changed();
                }
            }
            var nextPalette = (LevelPalette)EditorGUILayout.ObjectField("物体配置", palette,
                typeof(LevelPalette), false);
            if (nextPalette != palette)
            {
                EndStroke();
                palette = nextPalette;
                sampledBrush = null;
                brushIndex = 0;
                settingsBrush = null;
                if (layout != null)
                {
                    Undo.RecordObject(layout, "修改物体配置");
                    layout.Palette = palette;
                    Changed();
                }
            }
            if (GUILayout.Button("适应窗口", EditorStyles.toolbarButton, GUILayout.Width(72f))) fitPending = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUIUtility.labelWidth = previousLabelWidth;
        }

        private void NewLayout()
        {
            string path = EditorUtility.SaveFilePanelInProject("新建区域布局", "Level_New", "asset",
                "选择布局资源的保存位置", "Assets/Runtime/Levels");
            if (string.IsNullOrEmpty(path)) return;
            var definition = CreateInstance<LevelDefinition>();
            definition.Palette = palette != null ? palette : LevelEditorAssets.EnsureDefaults();
            AssetDatabase.CreateAsset(definition, path);
            AssetDatabase.SaveAssets();
            UseLayout(definition);
        }

        private void SaveLayout()
        {
            EndStroke();
            if (layout == null) return;
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssets();
            ShowNotification(new GUIContent("布局已保存"));
        }

        private void ExportPrefab()
        {
            EndStroke();
            if (layout == null) return;
            var problems = LevelPrefabBuilder.Validate(layout);
            if (problems.Count > 0)
            {
                saveError = string.Join("\n", problems);
                ShowNotification(new GUIContent("请先处理右侧提示，再保存 Prefab"));
                return;
            }
            string path = layout.OutputPrefab != null ? AssetDatabase.GetAssetPath(layout.OutputPrefab) : null;
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanelInProject("保存区域 Prefab", layout.name, "prefab",
                    "选择区域 Prefab 的保存位置", "Assets/Prefabs/Levels");
            }
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                layout.OutputPrefab = LevelPrefabBuilder.Save(layout, path);
                EditorUtility.SetDirty(layout);
                AssetDatabase.SaveAssets();
                saveError = null;
                prefabNeedsUpdate = false;
                EditorGUIUtility.PingObject(layout.OutputPrefab);
                ShowNotification(new GUIContent("区域 Prefab 已保存"));
            }
            catch (Exception exception)
            {
                saveError = exception.Message;
                Debug.LogException(exception);
            }
        }

        private void DrawSidebar()
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(layout == null || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("一键生成测试场景", GUILayout.Height(28f))) GenerateTestScene();
            if (GUILayout.Button("定位 Runtime 配置", EditorStyles.miniButton))
            {
                try { Selection.activeObject = LevelTestSceneBuilder.EnsureRuntimePrefab(); EditorGUIUtility.PingObject(Selection.activeObject); }
                catch (Exception exception) { saveError = exception.Message; }
            }
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("工具", EditorStyles.boldLabel);
            int nextTool = GUILayout.SelectionGrid((int)tool, ToolLabels, 2, GUILayout.Height(104f));
            if (nextTool != (int)tool)
            {
                EndStroke();
                tool = (GridTool)nextTool;
            }
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("物体", EditorStyles.boldLabel);
            brushSearch = EditorGUILayout.TextField(brushSearch, EditorStyles.toolbarSearchField);
            isolateLayer = EditorGUILayout.ToggleLeft("仅显示当前图层（" + (Brush != null ? Brush.LayerId : "geometry") + "）", isolateLayer);
            if (palette != null && palette.Brushes != null && palette.Brushes.Exists(item => item != null && item.HandlerId == "phase-block"))
                previewPhaseActive = EditorGUILayout.ToggleLeft("预览：全局开关亮起", previewPhaseActive);
            if (palette == null || palette.Brushes == null || !palette.Brushes.Exists(brush => brush != null))
            {
                EditorGUILayout.HelpBox("请选择物体配置，添加可使用的画笔。", MessageType.Info);
                if (GUILayout.Button("载入默认笔刷"))
                {
                    LevelEditorAssets.EnsureDefaults();
                    UseDefaultPalette();
                    Repaint();
                }
            }
            else
            {
                for (int i = 0; i < palette.Brushes.Count; i++)
                {
                    LevelBrush brush = palette.Brushes[i];
                    if (brush == null) continue;
                    if (!string.IsNullOrEmpty(brushSearch) && BrushName(brush).IndexOf(brushSearch, StringComparison.OrdinalIgnoreCase) < 0
                        && brush.LayerId.IndexOf(brushSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    bool selected = brush == Brush;
                    if (DrawBrushItem(brush, selected))
                    {
                        EndStroke();
                        if (!selected) settingsBrush = null;
                        brushIndex = i;
                        sampledBrush = null;
                        if (SelectionMode) tool = GridTool.Brush;
                    }
                }
                if (GUILayout.Button("管理物体配置", EditorStyles.miniButton))
                {
                    Selection.activeObject = palette;
                    EditorGUIUtility.PingObject(palette);
                }
            }
            if (sampledBrush != null)
                DrawBrushItem(sampledBrush, true);
            EditorGUILayout.Space(8f);
            if (SelectionMode)
            {
                if (hasAreaSelection)
                {
                    EditorGUILayout.LabelField($"已选 {selectionArea.width} × {selectionArea.height} 格", EditorStyles.boldLabel);
                    if (GUILayout.Button("复制区域 [Ctrl+C]")) CopyArea();
                }
                DrawSelection();
            }
            else if (tool == GridTool.Stamp) EditorGUILayout.HelpBox($"点击粘贴 {clipboard.Count} 个物体；可连续放置。", MessageType.Info);
            else DrawBrushSettings();
            if (layout != null)
            {
                EditorGUILayout.Space(10f);
                LevelPlacement entry = layout.EntryPlacement;
                EditorGUILayout.LabelField("区域入口", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(entry != null ? $"复活点 ({entry.Cell.x}, {entry.Cell.y})" : "尚未指定入口复活点");
                int outside = OutsideCount();
                if (outside > 0)
                {
                    EditorGUILayout.HelpBox($"有 {outside} 个物体在区域边界之外。扩大区域或删除它们后才能保存 Prefab。", MessageType.Warning);
                    if (GUILayout.Button("删除越界物体")) RemoveOutside();
                }
            }
            if (!string.IsNullOrEmpty(saveError)) EditorGUILayout.HelpBox(saveError, MessageType.Error);
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(ToolHelp(), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("B 笔刷 · E 橡皮 · R 矩形 · F 油漆桶 · V 选择\nCtrl+C/V 区域复制/粘贴 · Delete 删除选择\n方向键移动选择 · Ctrl+S 保存 · Home 适应窗口\n右键擦当前层 · Shift + 右键擦整格\n中键拾取 · Alt/空格 + 左键平移 · 滚轮缩放", EditorStyles.wordWrappedMiniLabel);
        }

        private void GenerateTestScene()
        {
            EndStroke();
            try
            {
                string path = LevelTestSceneBuilder.Generate(layout);
                if (string.IsNullOrEmpty(path)) return;
                saveError = null;
                prefabNeedsUpdate = false;
                ShowNotification(new GUIContent("已生成：" + System.IO.Path.GetFileName(path)));
            }
            catch (Exception exception)
            {
                saveError = exception.Message;
                Debug.LogException(exception);
            }
        }

        private bool DrawBrushItem(LevelBrush brush, bool selected)
        {
            Rect row = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.Height(52f), GUILayout.ExpandWidth(true));
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = selected ? new Color(0.45f, 0.8f, 0.95f) : Color.white;
            bool clicked = GUI.Button(row, GUIContent.none);
            GUI.backgroundColor = old;
            Rect icon = new Rect(row.x + 6f, row.y + 6f, 40f, 40f);
            Texture2D thumbnail = brush != null ? brush.Thumbnail : null;
            if (thumbnail == null && brush != null && brush.Prefab != null)
            {
                thumbnail = AssetPreview.GetAssetPreview(brush.Prefab);
                if (AssetPreview.IsLoadingAssetPreview(brush.Prefab.GetInstanceID())) Repaint();
            }
            if (thumbnail != null) GUI.DrawTexture(icon, thumbnail, ScaleMode.ScaleToFit, true);
            else if (brush != null)
            {
                EditorGUI.DrawRect(icon, new Color(0.11f, 0.13f, 0.16f));
                DrawPlacement(new LevelPlacement { Brush = brush },
                    new Rect(icon.x + 4f, icon.y + 4f, icon.width - 8f, icon.height - 8f), false);
            }
            GUI.Label(new Rect(icon.xMax + 8f, row.y, Mathf.Max(1f, row.xMax - icon.xMax - 14f), row.height),
                BrushName(brush), selected ? EditorStyles.boldLabel : EditorStyles.label);
            return clicked;
        }

        private string ToolHelp()
        {
            switch (tool)
            {
                case GridTool.Eraser: return "左键擦当前层 · Shift + 左键擦整格";
                case GridTool.Rectangle: return "左键拖出填充矩形 · 右键拖出擦除矩形";
                case GridTool.RectangleOutline: return "拖动绘制矩形边框 · 右键擦除边框";
                case GridTool.Bucket: return "左键填充相连的同类格子 · 右键擦除该区域";
                case GridTool.Select: return "左键选中格子";
                case GridTool.Stamp: return "点击目标位置粘贴区域；Esc 退出图章";
                default: return "左键绘制 · 拖动连续绘制";
            }
        }

        private void DrawBrushSettings()
        {
            LevelBrush brush = Brush;
            if (brush == null) return;
            LevelBrushHandler handler = LevelBrushHandlers.Get(brush.HandlerId);
            if (handler == null)
            {
                EditorGUILayout.HelpBox("此画笔暂不可用，请更新物体配置。", MessageType.Warning);
                return;
            }
            if (settingsBrush != brush || paintSettings == null)
            {
                settingsBrush = brush;
                paintSettings = handler.CreateSettings(brush);
            }
            EditorGUILayout.LabelField("绘制参数", EditorStyles.boldLabel);
            var template = new LevelPlacement { Brush = brush, Settings = paintSettings };
            handler.DrawSettings(template);
            paintSettings = template.Settings;
        }

        private void DrawSelection()
        {
            if (layout == null) return;
            RebuildCache();
            if (selectedCell.HasValue && cells.TryGetValue(selectedCell.Value, out var contents))
            {
                EditorGUILayout.LabelField($"格子 ({selectedCell.Value.x}, {selectedCell.Value.y})", EditorStyles.boldLabel);
                foreach (LevelPlacement item in contents)
                {
                    if (DrawBrushItem(item.Brush, item.Id == selectedPlacementId))
                        selectedPlacementId = item.Id;
                }
            }
            LevelPlacement placement = SelectedPlacement();
            if (placement == null)
            {
                EditorGUILayout.HelpBox("点击格子，选择要配置的物体。", MessageType.Info);
                return;
            }
            EditorGUILayout.Space(6f);
            EditorGUI.BeginChangeCheck();
            int x = EditorGUILayout.IntField("格子 X", placement.Cell.x);
            int y = EditorGUILayout.IntField("格子 Y", placement.Cell.y);
            if (EditorGUI.EndChangeCheck())
            {
                Vector2Int destination = new Vector2Int(x, y);
                var occupied = layout.Find(destination, placement.Brush != null ? placement.Brush.LayerId : "geometry");
                if (layout.Contains(destination) && (occupied == null || occupied == placement))
                {
                    Undo.RecordObject(layout, "移动关卡物体");
                    placement.Cell = destination;
                    selectedCell = destination;
                    selectionArea = new RectInt(destination, Vector2Int.one);
                    hasAreaSelection = true;
                    Changed();
                }
                else ShowNotification(new GUIContent("目标格子越界或同层已有物体"));
            }
            var handler = placement.Brush != null ? LevelBrushHandlers.Get(placement.Brush.HandlerId) : null;
            if (handler == null)
            {
                EditorGUILayout.HelpBox("此物体暂不可配置，请更新物体配置。", MessageType.Warning);
            }
            else
            {
                Undo.RecordObject(layout, "配置关卡物体");
                EditorGUI.BeginChangeCheck();
                handler.DrawSettings(placement);
                if (EditorGUI.EndChangeCheck()) Changed();
                if (handler.IsCheckpoint)
                {
                    bool isEntry = placement.Id == layout.EntryPlacementId;
                    if (GUILayout.Button(isEntry ? "取消入口复活点" : "设为入口复活点"))
                    {
                        Undo.RecordObject(layout, "设置区域入口");
                        layout.EntryPlacementId = isEntry ? null : placement.Id;
                        Changed();
                    }
                }
            }
            if (GUILayout.Button("删除此物体"))
            {
                Undo.RecordObject(layout, "删除关卡物体");
                layout.Placements.Remove(placement);
                if (placement.Id == layout.EntryPlacementId) layout.EntryPlacementId = null;
                selectedPlacementId = null;
                Changed();
            }
        }

        private void Fit(Rect canvas)
        {
            cellPixels = Mathf.Clamp(Mathf.Min((canvas.width - GridMargin * 2f) / layout.Size.x,
                (canvas.height - GridMargin * 2f) / layout.Size.y), 8f, 80f);
            pan = new Vector2(Mathf.Max(GridMargin, (canvas.width - layout.Size.x * cellPixels) * 0.5f),
                Mathf.Max(GridMargin, (canvas.height - layout.Size.y * cellPixels) * 0.5f));
        }

        private void DrawCanvas(Rect canvas)
        {
            EditorGUI.DrawRect(canvas, new Color(0.11f, 0.13f, 0.16f));
            if (layout == null)
            {
                GUI.Label(canvas, "新建布局，或把已有布局拖入上方的“布局”栏", CenterStyle());
                return;
            }
            RebuildCache();
            Vector2 mouse = Event.current.mousePosition - canvas.position;
            bool hoveringCanvas = canvas.Contains(Event.current.mousePosition);
            Vector2Int hover = MouseToCell(mouse);
            GUI.BeginGroup(canvas);
            Rect bounds = new Rect(pan.x, pan.y, layout.Size.x * cellPixels, layout.Size.y * cellPixels);
            EditorGUI.DrawRect(bounds, new Color(0.17f, 0.2f, 0.24f));
            foreach (var pair in cells)
            {
                if (!layout.Contains(pair.Key)) continue;
                Rect cell = CellRect(pair.Key);
                if (cell.xMax < 0 || cell.yMax < 0 || cell.x > canvas.width || cell.y > canvas.height) continue;
                foreach (LevelPlacement item in pair.Value) if (Visible(item)) DrawPlacement(item, cell, false);
            }
            DrawGrid(canvas, bounds);
            if (hoveringCanvas && layout.Contains(hover))
            {
                Rect cell = CellRect(hover);
                if (tool == GridTool.Stamp)
                {
                    foreach (LevelPlacement item in clipboard)
                        if (layout.Contains(hover + item.Cell)) DrawPlacement(item, CellRect(hover + item.Cell), true);
                }
                else if (!SelectionMode && tool != GridTool.Eraser && !RectangleStroke && Brush != null)
                    DrawPlacement(new LevelPlacement { Cell = hover, Brush = Brush, Settings = paintSettings }, cell, true);
                Outline(cell, new Color(1f, 1f, 1f, 0.8f), 1f);
            }
            LevelPlacement selected = SelectedPlacement();
            if (selected != null && layout.Contains(selected.Cell))
                Outline(CellRect(selected.Cell), new Color(1f, 0.82f, 0.24f), 2f);
            if (hasAreaSelection && SelectionMode)
            {
                Rect first = CellRect(new Vector2Int(selectionArea.xMin, selectionArea.yMax - 1));
                Rect area = new Rect(first.x, first.y, selectionArea.width * cellPixels, selectionArea.height * cellPixels);
                EditorGUI.DrawRect(area, new Color(1f, 0.82f, 0.24f, 0.08f));
                Outline(area, new Color(1f, 0.82f, 0.24f, 0.9f), 2f);
            }
            if (RectangleStroke)
            {
                Color fill = paintButton == 1 ? new Color(1f, 0.4f, 0.3f, 0.35f) : new Color(0.35f, 0.85f, 1f, 0.35f);
                Rect area = RectanglePreview();
                if (strokeTool == GridTool.RectangleOutline) Outline(area, fill, cellPixels);
                else EditorGUI.DrawRect(area, fill);
                Outline(area, paintButton == 1 ? new Color(1f, 0.4f, 0.3f) : new Color(0.35f, 0.85f, 1f), 2f);
                GUI.Label(new Rect(area.x, area.y - 20f, Mathf.Max(100f, area.width), 18f),
                    $"{Mathf.Abs(rectangleEnd.x - rectangleStart.Value.x) + 1} × {Mathf.Abs(rectangleEnd.y - rectangleStart.Value.y) + 1}", EntryStyle());
            }
            LevelPlacement entry = layout.EntryPlacement;
            if (entry != null && layout.Contains(entry.Cell))
            {
                Rect cell = CellRect(entry.Cell);
                GUI.Label(new Rect(cell.x - 8f, cell.y - 18f, Mathf.Max(40f, cell.width + 16f), 18f),
                    "入口", EntryStyle());
            }
            GUI.EndGroup();
        }

        private void DrawGrid(Rect canvas, Rect bounds)
        {
            Color gridColor = new Color(0.65f, 0.75f, 0.85f, 0.17f);
            int firstX = Mathf.Clamp(Mathf.FloorToInt(-pan.x / cellPixels), 0, layout.Size.x);
            int lastX = Mathf.Clamp(Mathf.CeilToInt((canvas.width - pan.x) / cellPixels), 0, layout.Size.x);
            int firstRow = Mathf.Clamp(Mathf.FloorToInt(-pan.y / cellPixels), 0, layout.Size.y);
            int lastRow = Mathf.Clamp(Mathf.CeilToInt((canvas.height - pan.y) / cellPixels), 0, layout.Size.y);
            int labelStep = Mathf.Max(1, Mathf.CeilToInt(25f / cellPixels));
            for (int x = firstX; x <= lastX; x++)
            {
                float px = pan.x + x * cellPixels;
                EditorGUI.DrawRect(new Rect(px, bounds.y, 1f, bounds.height), gridColor);
                if (x < layout.Size.x && x % labelStep == 0)
                    GUI.Label(new Rect(px, Mathf.Max(0f, pan.y - 20f), cellPixels * labelStep, 18f), x.ToString(), CoordinateStyle());
            }
            for (int row = firstRow; row <= lastRow; row++)
            {
                float py = pan.y + row * cellPixels;
                EditorGUI.DrawRect(new Rect(bounds.x, py, bounds.width, 1f), gridColor);
                int y = layout.Size.y - 1 - row;
                if (row < layout.Size.y && y % labelStep == 0)
                    GUI.Label(new Rect(Mathf.Max(0f, pan.x - 26f), py, 25f, cellPixels), y.ToString(), CoordinateStyle());
            }
            Outline(bounds, new Color(0.35f, 0.85f, 0.95f), 2f);
        }

        private void DrawPlacement(LevelPlacement placement, Rect cell, bool ghost)
        {
            LevelBrush brush = placement.Brush;
            if (brush == null) return;
            Rect preview = brush.PreviewRect;
            Rect shape = new Rect(cell.x + preview.x * cell.width,
                cell.y + (1f - preview.yMax) * cell.height,
                preview.width * cell.width, preview.height * cell.height);
            Color color = brush.Color;
            if (ghost) color.a *= 0.45f;
            var handler = LevelBrushHandlers.Get(brush.HandlerId);
            if (handler != null && handler.IsCheckpoint)
            {
                float poleX = shape.x + shape.width * 0.3f;
                EditorGUI.DrawRect(new Rect(poleX, shape.y, Mathf.Max(1f, cell.width * 0.07f), shape.height), color);
                EditorGUI.DrawRect(new Rect(poleX, shape.y, shape.width * 0.65f, shape.height * 0.4f), color);
                EditorGUI.DrawRect(new Rect(shape.x, shape.yMax - 2f, shape.width, 2f), color);
            }
            else if (brush.HandlerId == "endpoint")
            {
                Outline(shape, color, Mathf.Max(1f, cell.width * 0.08f));
                if (cell.width >= 14f) GUI.Label(shape, "终", CenterStyle());
            }
            else if (placement.Settings is PhaseBlockPlacementSettings phaseBlock)
            {
                Color fill = color;
                bool solid = phaseBlock.SolidWhenActive == previewPhaseActive;
                if (!solid) fill.a *= 0.22f;
                EditorGUI.DrawRect(shape, fill);
                Outline(shape, color, Mathf.Max(1f, cell.width * 0.07f));
                if (cell.width >= 14f) GUI.Label(shape, solid ? "实" : "虚", CenterStyle());
            }
            else
            {
                EditorGUI.DrawRect(shape, color);
                if (shape.height > 5f) Outline(shape, new Color(0f, 0f, 0f, ghost ? 0.12f : 0.28f), 1f);
            }
            if (placement.Settings is ConveyorPlacementSettings conveyor && cell.width >= 14f)
            {
                GUI.Label(shape, conveyor.Speed < 0f ? "←" : conveyor.Speed > 0f ? "→" : "·", CenterStyle());
            }
        }

        private void HandleCanvas(Rect canvas)
        {
            Event e = Event.current;
            int control = GUIUtility.GetControlID("TapTap.LevelGrid".GetHashCode(), FocusType.Passive);
            bool inside = canvas.Contains(e.mousePosition);
            Vector2 local = e.mousePosition - canvas.position;
            Vector2Int cell = MouseToCell(local);
            if (e.type == EventType.ScrollWheel && inside && layout != null)
            {
                Vector2 underMouse = (local - pan) / cellPixels;
                cellPixels = Mathf.Clamp(cellPixels * Mathf.Pow(1.12f, -e.delta.y), 8f, 120f);
                pan = local - underMouse * cellPixels;
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDown && inside && e.button == 0 && (e.alt || spacePan))
            {
                EndStroke();
                panning = true;
                GUIUtility.hotControl = control;
                e.Use();
            }
            else if (e.type == EventType.MouseDown && inside && e.button == 2)
            {
                EndStroke();
                if (layout != null && layout.Contains(cell)) PickBrush(cell, local);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && panning)
            {
                pan += e.delta;
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDown && inside && layout != null && layout.Contains(cell) &&
                (e.button == 0 || e.button == 1))
            {
                GUIUtility.keyboardControl = 0;
                if (tool == GridTool.Stamp && e.button == 0) PasteArea(cell);
                else if (SelectionMode && e.button == 0)
                {
                    SelectCell(cell);
                    selecting = true;
                    selectionAnchor = cell;
                    selectionArea = new RectInt(cell, Vector2Int.one);
                    hasAreaSelection = true;
                    GUIUtility.hotControl = control;
                }
                else if (e.button == 1 || tool == GridTool.Eraser || CanPaint())
                {
                    BeginStroke(tool == GridTool.Eraser ? 1 : e.button);
                    if (tool == GridTool.Bucket)
                    {
                        ApplyCells(LevelGridEditing.ConnectedArea(layout, cell, strokeLayerId), e.shift);
                        EndStroke();
                    }
                    else
                    {
                        GUIUtility.hotControl = control;
                        if (tool == GridTool.Rectangle || tool == GridTool.RectangleOutline)
                        {
                            rectangleStart = cell;
                            rectangleEnd = cell;
                        }
                        else
                        {
                            PaintLine(cell, cell, e.shift);
                            previousPaintCell = cell;
                        }
                    }
                }
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && selecting)
            {
                Vector2Int end = ClampCell(cell);
                Vector2Int min = Vector2Int.Min(selectionAnchor, end);
                selectionArea = new RectInt(min, Vector2Int.Max(selectionAnchor, end) - min + Vector2Int.one);
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDrag && painting)
            {
                if (RectangleStroke) rectangleEnd = ClampCell(cell);
                else if (inside && layout != null && layout.Contains(cell))
                {
                    PaintLine(previousPaintCell ?? cell, cell, e.shift);
                    previousPaintCell = cell;
                }
                else previousPaintCell = null;
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseUp && (painting || panning || selecting))
            {
                if (RectangleStroke)
                {
                    rectangleEnd = ClampCell(cell);
                    ApplyCells(LevelGridEditing.Rectangle(rectangleStart.Value, rectangleEnd,
                        strokeTool == GridTool.RectangleOutline), e.shift);
                }
                EndStroke();
                panning = false;
                selecting = false;
                GUIUtility.hotControl = 0;
                e.Use();
                Repaint();
            }
            if (inside && e.type == EventType.MouseMove) Repaint();
            wantsMouseMove = true;
        }

        private bool CanPaint()
        {
            return Brush != null && LevelBrushHandlers.Get(Brush.HandlerId) != null;
        }

        private void BeginStroke(int button)
        {
            EndStroke();
            painting = true;
            paintButton = button;
            strokeTool = tool;
            LevelPlacement selected = SelectionMode ? SelectedPlacement() : null;
            strokeLayerId = selected != null && selected.Brush != null ? selected.Brush.LayerId :
                Brush != null ? Brush.LayerId : "geometry";
            visited.Clear();
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(button == 1 ? "擦除区域" : tool == GridTool.Bucket ? "油漆桶填充" :
                tool == GridTool.Rectangle || tool == GridTool.RectangleOutline ? "矩形绘制" : "绘制区域");
        }

        private void EndStroke()
        {
            if (undoGroup >= 0)
            {
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
            }
            undoGroup = -1;
            painting = false;
            previousPaintCell = null;
            rectangleStart = null;
            visited.Clear();
        }

        private void PaintLine(Vector2Int from, Vector2Int to, bool allLayers)
        {
            var targets = new List<Vector2Int>();
            int dx = Mathf.Abs(to.x - from.x), dy = Mathf.Abs(to.y - from.y);
            int sx = from.x < to.x ? 1 : -1, sy = from.y < to.y ? 1 : -1;
            int error = dx - dy;
            while (true)
            {
                if (layout.Contains(from) && visited.Add(from)) targets.Add(from);
                if (from == to) break;
                int twice = error * 2;
                if (twice > -dy) { error -= dy; from.x += sx; }
                if (twice < dx) { error += dx; from.y += sy; }
            }
            ApplyCells(targets, allLayers);
        }

        private void ApplyCells(IEnumerable<Vector2Int> targets, bool allLayers)
        {
            if (layout == null) return;
            LevelBrush brush = Brush;
            if (paintButton == 0 && !CanPaint()) return;
            string layerId = strokeLayerId ?? (brush != null ? brush.LayerId : "geometry");
            Undo.RecordObject(layout, paintButton == 0 ? "绘制区域" : "擦除区域");
            if (paintButton == 0)
            {
                LevelBrushHandler handler = LevelBrushHandlers.Get(brush.HandlerId);
                if (settingsBrush != brush || paintSettings == null)
                {
                    settingsBrush = brush;
                    paintSettings = handler.CreateSettings(brush);
                }
            }
            if (LevelGridEditing.Apply(layout, targets, brush, paintSettings, layerId, paintButton == 1, allLayers)) Changed();
        }

        private static LevelPlacementSettings CloneSettings(LevelPlacementSettings source)
        {
            return LevelGridEditing.CloneSettings(source);
        }

        private Vector2Int ClampCell(Vector2Int cell)
        {
            return new Vector2Int(Mathf.Clamp(cell.x, 0, layout.Size.x - 1), Mathf.Clamp(cell.y, 0, layout.Size.y - 1));
        }

        private Rect RectanglePreview()
        {
            Vector2Int start = rectangleStart.Value;
            Rect topLeft = CellRect(new Vector2Int(Mathf.Min(start.x, rectangleEnd.x), Mathf.Max(start.y, rectangleEnd.y)));
            return new Rect(topLeft.x, topLeft.y, (Mathf.Abs(start.x - rectangleEnd.x) + 1) * cellPixels,
                (Mathf.Abs(start.y - rectangleEnd.y) + 1) * cellPixels);
        }

        private void PickBrush(Vector2Int cell, Vector2 localMouse)
        {
            RebuildCache();
            if (!cells.TryGetValue(cell, out var contents)) return;

            Rect rect = CellRect(cell);
            Vector2 pointInCell = new Vector2((localMouse.x - rect.x) / rect.width,
                1f - (localMouse.y - rect.y) / rect.height);
            LevelPlacement picked = null;
            // Prefer the visible topmost object, then allow sampling anywhere in an occupied cell.
            for (int i = contents.Count - 1; i >= 0; i--)
            {
                LevelPlacement item = contents[i];
                    if (Visible(item) && item.Brush != null && item.Brush.PreviewRect.Contains(pointInCell))
                {
                    picked = item;
                    break;
                }
            }
            if (picked == null)
            {
                picked = Brush != null ? layout.Find(cell, Brush.LayerId) : null;
                if (picked == null) picked = contents.FindLast(item => item.Brush != null && Visible(item));
            }
            if (picked == null || picked.Brush == null) return;
            LevelBrushHandler handler = LevelBrushHandlers.Get(picked.Brush.HandlerId);
            if (handler == null)
            {
                ShowNotification(new GUIContent("此笔刷缺少配置处理器"));
                return;
            }

            int index = palette != null && palette.Brushes != null ? palette.Brushes.IndexOf(picked.Brush) : -1;
            sampledBrush = index < 0 ? picked.Brush : null;
            if (index >= 0) brushIndex = index;
            settingsBrush = picked.Brush;
            paintSettings = picked.Settings != null ? CloneSettings(picked.Settings) : handler.CreateSettings(picked.Brush);
            tool = GridTool.Brush;
            selectedCell = cell;
            selectedPlacementId = picked.Id;
            GUIUtility.keyboardControl = 0;
            ShowNotification(new GUIContent("已拾取：" + BrushName(picked.Brush)));
        }

        private void SelectCell(Vector2Int cell)
        {
            RebuildCache();
            selectedCell = cell;
            selectedPlacementId = null;
            if (!cells.TryGetValue(cell, out var contents)) return;
            LevelPlacement preferred = Brush != null ? layout.Find(cell, Brush.LayerId) : null;
            var visible = preferred != null && Visible(preferred) ? preferred : contents.FindLast(Visible);
            if (visible != null) selectedPlacementId = visible.Id;
        }

        private LevelPlacement SelectedPlacement()
        {
            if (layout == null || layout.Placements == null || string.IsNullOrEmpty(selectedPlacementId)) return null;
            return layout.Placements.Find(item => item != null && item.Id == selectedPlacementId);
        }

        private void RebuildCache()
        {
            if (!cacheDirty) return;
            cells.Clear();
            if (layout != null && layout.Placements != null)
            {
                foreach (LevelPlacement item in layout.Placements)
                {
                    if (item == null) continue;
                    if (!cells.TryGetValue(item.Cell, out var contents))
                    {
                        contents = new List<LevelPlacement>();
                        cells.Add(item.Cell, contents);
                    }
                    contents.Add(item);
                }
            }
            cacheDirty = false;
        }

        private int OutsideCount()
        {
            return layout.Placements == null ? 0 : layout.Placements.FindAll(item =>
                item != null && !layout.Contains(item.Cell)).Count;
        }

        private void RemoveOutside()
        {
            Undo.RecordObject(layout, "删除越界物体");
            layout.Placements.RemoveAll(item => item != null && !layout.Contains(item.Cell));
            if (layout.EntryPlacement == null) layout.EntryPlacementId = null;
            Changed();
        }

        private void Changed()
        {
            EditorUtility.SetDirty(layout);
            prefabNeedsUpdate = true;
            cacheDirty = true;
            saveError = null;
            Repaint();
        }

        private bool Visible(LevelPlacement item) => item != null && (!isolateLayer ||
            item.Brush != null && item.Brush.LayerId == (Brush != null ? Brush.LayerId : "geometry"));

        private void HandleShortcuts()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyUp && e.keyCode == KeyCode.Space) { spacePan = false; e.Use(); return; }
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return;
            if (e.keyCode == KeyCode.Space) { spacePan = true; e.Use(); return; }
            if (painting || panning || selecting) return;
            if (e.control || e.command)
            {
                switch (e.keyCode)
                {
                    case KeyCode.S: if (e.shift) ExportPrefab(); else SaveLayout(); break;
                    case KeyCode.C: CopyArea(); break;
                    case KeyCode.X: if (CopyArea()) { DeleteArea(); clipboardIsCut = true; } break;
                    case KeyCode.V:
                        if (clipboard.Count == 0) ShowNotification(new GUIContent("先选择区域并复制"));
                        else { tool = GridTool.Stamp; GUIUtility.keyboardControl = 0; }
                        break;
                    default: return;
                }
            }
            else
            {
                switch (e.keyCode)
                {
                    case KeyCode.B: tool = GridTool.Brush; break;
                    case KeyCode.E: tool = GridTool.Eraser; break;
                    case KeyCode.R: tool = e.shift ? GridTool.RectangleOutline : GridTool.Rectangle; break;
                    case KeyCode.F: tool = GridTool.Bucket; break;
                    case KeyCode.V: tool = GridTool.Select; break;
                    case KeyCode.Home: fitPending = true; break;
                    case KeyCode.Delete: case KeyCode.Backspace: if (SelectionMode) DeleteArea(); else return; break;
                    case KeyCode.LeftArrow: if (SelectionMode) MoveArea(Vector2Int.left); else return; break;
                    case KeyCode.RightArrow: if (SelectionMode) MoveArea(Vector2Int.right); else return; break;
                    case KeyCode.UpArrow: if (SelectionMode) MoveArea(Vector2Int.up); else return; break;
                    case KeyCode.DownArrow: if (SelectionMode) MoveArea(Vector2Int.down); else return; break;
                    default: return;
                }
            }
            e.Use(); Repaint();
        }

        private List<LevelPlacement> AreaContents()
        {
            if (layout == null || !hasAreaSelection) return new List<LevelPlacement>();
            return layout.Placements.FindAll(item => Visible(item) && selectionArea.Contains(item.Cell));
        }

        private bool CopyArea()
        {
            List<LevelPlacement> items = AreaContents();
            items.RemoveAll(item => item.Brush == null);
            if (items.Count == 0) { ShowNotification(new GUIContent("选择区域中没有物体")); return false; }
            clipboard.Clear();
            clipboardEntryIndex = -1;
            clipboardIsCut = false;
            foreach (LevelPlacement item in items)
            {
                if (item.Id == layout.EntryPlacementId) clipboardEntryIndex = clipboard.Count;
                clipboard.Add(new LevelPlacement { Cell = item.Cell - selectionArea.min, Brush = item.Brush, Settings = CloneSettings(item.Settings) });
            }
            ShowNotification(new GUIContent($"已复制 {items.Count} 个物体；Ctrl+V 放置"));
            return true;
        }

        private void PasteArea(Vector2Int origin)
        {
            if (layout == null || clipboard.Count == 0) return;
            foreach (LevelPlacement item in clipboard)
                if (item.Brush == null || !layout.Contains(origin + item.Cell)) { ShowNotification(new GUIContent("粘贴区域越界或笔刷已删除")); return; }
            Undo.RecordObject(layout, "粘贴关卡区域");
            foreach (LevelPlacement item in clipboard)
                LevelGridEditing.Apply(layout, new[] { origin + item.Cell }, item.Brush, item.Settings, item.Brush.LayerId, false, false);
            if (clipboardIsCut && clipboardEntryIndex >= 0 && layout.EntryPlacement == null)
            {
                LevelPlacement entry = clipboard[clipboardEntryIndex];
                layout.EntryPlacementId = layout.Find(origin + entry.Cell, entry.Brush.LayerId)?.Id;
            }
            clipboardIsCut = false;
            Changed();
        }

        private void DeleteArea()
        {
            List<LevelPlacement> items = AreaContents();
            if (items.Count == 0) return;
            Undo.RecordObject(layout, "删除选择区域");
            foreach (LevelPlacement item in items)
            {
                layout.Placements.Remove(item);
                if (item.Id == layout.EntryPlacementId) layout.EntryPlacementId = null;
            }
            selectedPlacementId = null;
            Changed();
        }

        private void MoveArea(Vector2Int delta)
        {
            List<LevelPlacement> items = AreaContents();
            if (items.Count == 0) return;
            foreach (LevelPlacement item in items)
            {
                if (item.Brush == null) return;
                Vector2Int destination = item.Cell + delta;
                var occupied = layout.Find(destination, item.Brush.LayerId);
                if (!layout.Contains(destination) || occupied != null && !items.Contains(occupied))
                { ShowNotification(new GUIContent("移动目标越界或同层已有物体")); return; }
            }
            Undo.RecordObject(layout, "移动选择区域");
            foreach (LevelPlacement item in items) item.Cell += delta;
            selectionArea.position += delta;
            if (selectedCell.HasValue) selectedCell += delta;
            Changed();
        }

        private void DrawStatus(Rect canvas)
        {
            Rect bar = new Rect(0f, position.height - 22f, position.width, 22f);
            EditorGUI.DrawRect(bar, new Color(0.18f, 0.2f, 0.23f));
            Vector2Int cell = MouseToCell(Event.current.mousePosition - canvas.position);
            string coordinate = canvas.Contains(Event.current.mousePosition) && layout != null && layout.Contains(cell) ? $"格子 {cell.x}, {cell.y}   " : "";
            string saveState = layout == null ? "" : EditorUtility.IsDirty(layout) ? "布局未保存" : "布局已保存";
            string prefabState = layout != null && layout.OutputPrefab == null ? " · Prefab 未生成" : prefabNeedsUpdate ? " · Prefab 待更新" : "";
            GUI.Label(new Rect(8f, bar.y + 2f, bar.width - 16f, 18f),
                coordinate + $"{cellPixels:0} px/格   ·   {(layout != null ? layout.Placements.Count : 0)} 个物体   ·   " + saveState + prefabState,
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.85f, 0.9f, 0.95f) } });
        }

        private Vector2Int MouseToCell(Vector2 local)
        {
            Vector2 p = (local - pan) / cellPixels;
            return new Vector2Int(Mathf.FloorToInt(p.x), layout != null ? layout.Size.y - 1 - Mathf.FloorToInt(p.y) : 0);
        }

        private Rect CellRect(Vector2Int cell)
        {
            return new Rect(pan.x + cell.x * cellPixels,
                pan.y + (layout.Size.y - 1 - cell.y) * cellPixels, cellPixels, cellPixels);
        }

        private static void Outline(Rect rect, Color color, float width)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, width, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }

        private static string BrushName(LevelBrush brush)
        {
            return brush == null ? "缺失物体" : string.IsNullOrEmpty(brush.DisplayName) ? brush.name : brush.DisplayName;
        }

        private static GUIStyle CenterStyle()
        {
            return new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.95f, 0.97f, 1f) } };
        }

        private static GUIStyle CoordinateStyle()
        {
            return new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.75f, 0.82f, 0.88f) } };
        }

        private static GUIStyle EntryStyle()
        {
            return new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.84f, 0.25f) } };
        }
    }

    [CustomEditor(typeof(LevelDefinition))]
    public sealed class LevelDefinitionInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("打开区域编辑器", GUILayout.Height(28f)))
                LevelEditorWindow.Open((LevelDefinition)target);
            EditorGUILayout.Space(4f);
            DrawDefaultInspector();
        }
    }
}
#endif
