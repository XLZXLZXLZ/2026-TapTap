using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TapTap.Editor
{
    public sealed class TerrainPaintWindow : EditorWindow
    {
        private enum PaintTool { Brush, Rectangle, Fill, Sample, Eraser }
        [SerializeField] private bool paintingEnabled, crossRegions, showGuides = true;
        [SerializeField] private int brushSize = 1, variantIndex;
        [SerializeField] private PaintTool tool;
        [SerializeField] private LevelRegion currentRegion;
        [SerializeField] private bool useCustomMaterial;
        [SerializeField] private Material customMaterial;
        private List<TerrainPaintRegion> regions = new List<TerrainPaintRegion>();
        private bool indexDirty = true;
        private TerrainPaintStroke stroke;
        private TerrainPaintRegion strokeRegion;
        private Vector2 strokeStart, previousMouse;
        private string notice = "选择关卡和笔刷，在 Scene 视图中启用绘制。";
        private Vector2 scroll;

        [MenuItem("TapTap/场景地形涂装", false, 103)]
        public static void Open() => GetWindow<TerrainPaintWindow>("场景地形涂装").Show();

        private void OnEnable()
        {
            minSize = new Vector2(300, 470);
            SceneView.duringSceneGui += DuringSceneGUI;
            EditorApplication.hierarchyChanged += InvalidateIndex;
            EditorApplication.projectChanged += InvalidateIndex;
            Selection.selectionChanged += FollowSelection;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            FollowSelection();
        }

        private void OnDisable()
        {
            FinishStroke();
            SceneView.duringSceneGui -= DuringSceneGUI;
            EditorApplication.hierarchyChanged -= InvalidateIndex;
            EditorApplication.projectChanged -= InvalidateIndex;
            Selection.selectionChanged -= FollowSelection;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnPlayModeChanged(PlayModeStateChange state) { FinishStroke(); paintingEnabled = false; InvalidateIndex(); }
        private void InvalidateIndex() { indexDirty = true; Repaint(); }
        private void FollowSelection()
        {
            if (Selection.activeGameObject != null)
            {
                LevelRegion selected = Selection.activeGameObject.GetComponentInParent<LevelRegion>(true);
                if (selected != null) { FinishStroke(); currentRegion = selected; }
            }
            InvalidateIndex();
        }

        private void OnUndoRedo()
        {
            InvalidateIndex();
        }

        private void EnsureIndex()
        {
            if (!indexDirty) return;
            regions = TerrainPaintSceneIndex.Build().OrderBy(entry => entry.Region.SequenceNumber)
                .ThenBy(entry => entry.Region.Source.name).ToList();
            if (currentRegion == null || !regions.Exists(entry => entry.Region == currentRegion))
                currentRegion = regions.Count > 0 ? regions[0].Region : null;
            indexDirty = false;
        }

        private TerrainPaintRegion Current => regions.Find(entry => entry.Region == currentRegion);
        private Material MaterialFor(TerrainPaintRegion region) => tool == PaintTool.Eraser || variantIndex < 0 && !useCustomMaterial
            ? null : useCustomMaterial ? customMaterial
            : region.Profile.Variants.Count > variantIndex ? region.Profile.Variants[variantIndex] : null;

        private void OnGUI()
        {
            EnsureIndex();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                bool next = GUILayout.Toggle(paintingEnabled, paintingEnabled ? "● 正在绘制：在 Scene 中拖动" : "启用 Scene 涂装", "Button", GUILayout.Height(34));
                if (next != paintingEnabled) { FinishStroke(); paintingEnabled = next; SceneView.RepaintAll(); }
                EditorGUILayout.Space();
                if (regions.Count == 0)
                {
                    EditorGUILayout.HelpBox("打开游玩地图或单关测试场景后，在真实地形上绘制。", MessageType.Info);
                    if (GUILayout.Button("打开当前游玩地图")) OpenPlayScene();
                    if (GUILayout.Button("准备缺失的关卡笔刷")) { TerrainPaintAssets.PrepareAll(); InvalidateIndex(); }
                }
                else
                {
                    int selected = regions.FindIndex(entry => entry.Region == currentRegion);
                    string[] labels = regions.Select(entry => (entry.Region.SequenceNumber > 0 ? "第 " + entry.Region.SequenceNumber + " 关 · " : "") + entry.Region.Source.name).ToArray();
                    int chosen = EditorGUILayout.Popup("当前关卡", Mathf.Max(0, selected), labels);
                    if (chosen != selected) { FinishStroke(); currentRegion = regions[chosen].Region; }
                    crossRegions = EditorGUILayout.ToggleLeft("允许跨关卡绘制（各关使用自己的同号变体）", crossRegions);
                    tool = (PaintTool)GUILayout.SelectionGrid((int)tool, new[] { "画笔 B", "矩形 R", "填充 F", "吸管 I", "橡皮 E" }, 3);
                    if (tool == PaintTool.Brush || tool == PaintTool.Eraser) brushSize = EditorGUILayout.IntSlider("笔刷大小（格）", brushSize, 1, 16);
                    showGuides = EditorGUILayout.ToggleLeft("显示笔刷范围与目标轮廓", showGuides);
                    EditorGUILayout.Space();
                    TerrainPaintProfile profile = Current.Profile;
                    EditorGUILayout.LabelField("当前关卡材质", EditorStyles.boldLabel);
                    for (int row = 0; row < 2; row++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                            for (int column = 0; column < 2; column++)
                            {
                                int index = row * 2 + column - 1;
                                Material material = index < 0 ? profile.BaseMaterial
                                    : profile.Variants.Count > index ? profile.Variants[index] : null;
                                DrawSwatch(material, index < 0 ? "恢复原色" : TerrainPaintAssets.VariantLabels[index], index);
                            }
                    }
                    useCustomMaterial = EditorGUILayout.ToggleLeft("使用指定材质", useCustomMaterial);
                    if (useCustomMaterial) customMaterial = (Material)EditorGUILayout.ObjectField("材质", customMaterial, typeof(Material), false);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("定位笔刷目录"))
                        {
                            Object folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(Path.GetDirectoryName(AssetDatabase.GetAssetPath(profile)).Replace('\\', '/'));
                            Selection.activeObject = folder; EditorGUIUtility.PingObject(folder);
                        }
                        if (GUILayout.Button("定位当前关卡")) SceneView.lastActiveSceneView?.Frame(currentRegion.WorldBounds, false);
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("正面绘制")) FrontView();
                        if (GUILayout.Button("对齐游戏相机")) AlignGameCamera();
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("撤销")) Undo.PerformUndo();
                        if (GUILayout.Button("重做")) Undo.PerformRedo();
                    }
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("笔刷和涂装数据", EditorStyles.miniBoldLabel);
                    EditorGUILayout.SelectableLabel(AssetDatabase.GetAssetPath(profile), EditorStyles.wordWrappedMiniLabel, GUILayout.Height(38));
                }
            }
            EditorGUILayout.HelpBox(notice, MessageType.None);
            EditorGUILayout.LabelField("左键绘制 · Ctrl+单击切换关卡\n[ / ] 调大小 · Ctrl+Z 撤销 · Alt 导航\n松开鼠标自动保存整笔涂装；填充以当前关卡为界。", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
        }

        private void DrawSwatch(Material material, string label, int index)
        {
            Rect rect = GUILayoutUtility.GetRect(100, 68, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none, !useCustomMaterial && variantIndex == index ? "SelectionRect" : "box");
            Texture preview = material != null ? AssetPreview.GetAssetPreview(material) : null;
            Rect swatch = new Rect(rect.x + 5, rect.y + 5, 42, 42);
            if (preview != null) GUI.DrawTexture(swatch, preview, ScaleMode.ScaleToFit);
            else EditorGUI.DrawRect(swatch, TerrainPaintAssets.ReadColor(material));
            GUI.Label(new Rect(rect.x + 51, rect.y + 11, rect.width - 53, 30), label, EditorStyles.wordWrappedLabel);
            if (material != null && GUI.Button(rect, GUIContent.none, GUIStyle.none))
            { variantIndex = index; useCustomMaterial = false; if (tool == PaintTool.Eraser || tool == PaintTool.Sample) tool = PaintTool.Brush; Repaint(); }
            if (material != null && preview == null) Repaint();
        }

        private void OpenPlayScene()
        {
            var map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
            string path = map != null ? WorldMapSceneSync.ScenePath(map) : null;
            if (!string.IsNullOrEmpty(path) && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(path);
        }

        private void FrontView()
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null || currentRegion == null) return;
            view.in2DMode = false;
            view.LookAt(currentRegion.WorldBounds.center, currentRegion.transform.rotation,
                Mathf.Max(currentRegion.WorldBounds.extents.y, currentRegion.WorldBounds.extents.x / Mathf.Max(0.1f, view.camera.aspect)) * 1.15f, true);
        }

        private void AlignGameCamera()
        {
            Camera camera = Camera.main;
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null || camera == null) { notice = "当前场景没有启用的 MainCamera。"; return; }
            view.in2DMode = false; view.orthographic = camera.orthographic;
            view.cameraSettings.fieldOfView = camera.fieldOfView;
            if (camera.orthographic) view.size = camera.orthographicSize;
            view.AlignViewToObject(camera.transform); view.Repaint();
        }

        private TerrainPaintRegion Pick(Vector2 mouse, out TerrainTileVisual tile)
        {
            GameObject picked = HandleUtility.PickGameObject(mouse, false);
            tile = picked != null ? picked.GetComponentInParent<TerrainTileVisual>(true) : null;
            TerrainTileVisual target = tile;
            return target != null ? regions.Find(entry => entry.Cells.ContainsKey(target)) : null;
        }

        private void DuringSceneGUI(SceneView view)
        {
            if (!paintingEnabled || EditorApplication.isPlayingOrWillChangePlaymode) return;
            EnsureIndex();
            Event e = Event.current;
            int control = GUIUtility.GetControlID("TapTap.TerrainPaint".GetHashCode(), FocusType.Passive);
            if (stroke != null && (e.rawType == EventType.MouseUp || e.type == EventType.Ignore))
            {
                if (tool == PaintTool.Rectangle && e.type != EventType.Ignore) PaintRectangle(strokeStart, e.mousePosition);
                FinishStroke(); if (GUIUtility.hotControl == control) GUIUtility.hotControl = 0;
                e.Use(); view.Repaint(); return;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            { FinishStroke(); paintingEnabled = false; GUIUtility.hotControl = 0; e.Use(); Repaint(); view.Repaint(); return; }
            if (!e.alt && !e.control && !e.command && e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            if (e.alt || e.button == 1 || e.button == 2 || Tools.viewToolActive) return;
            if (e.type == EventType.KeyDown && !e.control && !e.command)
            {
                switch (e.keyCode)
                {
                    case KeyCode.B: tool = PaintTool.Brush; break;
                    case KeyCode.R: tool = PaintTool.Rectangle; break;
                    case KeyCode.F: tool = PaintTool.Fill; break;
                    case KeyCode.I: tool = PaintTool.Sample; break;
                    case KeyCode.E: tool = PaintTool.Eraser; break;
                    case KeyCode.LeftBracket: brushSize = Mathf.Max(1, brushSize - 1); break;
                    case KeyCode.RightBracket: brushSize = Mathf.Min(16, brushSize + 1); break;
                    default: return;
                }
                e.Use(); Repaint(); view.Repaint(); return;
            }
            TerrainPaintRegion hit = null; TerrainTileVisual tile = null;
            if (stroke == null && (e.type == EventType.MouseDown || e.type == EventType.Repaint)) hit = Pick(e.mousePosition, out tile);
            if (e.type == EventType.Repaint && showGuides)
            {
                if (stroke != null && tool == PaintTool.Rectangle) DrawRectangle(strokeStart, e.mousePosition);
                else if (hit != null && (crossRegions || hit.Region == currentRegion)) DrawBrush(hit, hit.Cells[tile]);
            }
            if (e.type == EventType.MouseMove) { view.Repaint(); return; }
            if (e.type == EventType.MouseDown && e.button == 0 && hit != null)
            {
                if (e.control || e.command) { currentRegion = hit.Region; e.Use(); Repaint(); return; }
                if (!crossRegions && hit.Region != currentRegion) { notice = "此关卡已锁定。Ctrl+单击目标地形切换关卡。"; Repaint(); e.Use(); return; }
                if (!tile.CanPaint) { notice = "此模型不包含已配置的可涂装材质，请检查关卡笔刷配置。"; Repaint(); e.Use(); return; }
                if (tool == PaintTool.Sample) { Sample(hit, tile); e.Use(); return; }
                if (useCustomMaterial && customMaterial == null && tool != PaintTool.Eraser)
                { notice = "请先指定要使用的材质。"; Repaint(); e.Use(); return; }
                stroke = new TerrainPaintStroke(); strokeRegion = hit; strokeStart = previousMouse = e.mousePosition;
                GUIUtility.hotControl = control;
                if (tool == PaintTool.Fill)
                    foreach (TerrainTileVisual target in TerrainPaintSceneIndex.Fill(hit, hit.Cells[tile])) stroke.Apply(target, MaterialFor(hit));
                else if (tool != PaintTool.Rectangle) PaintSegment(e.mousePosition, e.mousePosition);
                e.Use(); view.Repaint();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && stroke != null)
            {
                if (tool == PaintTool.Brush || tool == PaintTool.Eraser) PaintSegment(previousMouse, e.mousePosition);
                previousMouse = e.mousePosition; e.Use(); view.Repaint();
            }
        }

        private IEnumerable<TerrainPaintRegion> StrokeRegions() => crossRegions ? regions : new[] { strokeRegion };

        private void PaintSegment(Vector2 from, Vector2 to)
        {
            TerrainPaintRegion hitA = Pick(from, out TerrainTileVisual tileA);
            TerrainPaintRegion hitB = Pick(to, out TerrainTileVisual tileB);
            foreach (TerrainPaintRegion region in StrokeRegions())
            {
                if (!CellAt(region, from, hitA, tileA, out Vector2Int start)
                    || !CellAt(region, to, hitB, tileB, out Vector2Int end)) continue;
                RectInt bounds = region.Region.Source.CanvasBounds;
                int radius = brushSize / 2;
                bounds = new RectInt(bounds.min - Vector2Int.one * radius, bounds.size + Vector2Int.one * radius * 2);
                if (!TerrainPaintSceneIndex.ClipLine(ref start, ref end, bounds)) continue;
                foreach (Vector2Int center in TerrainPaintSceneIndex.Line(start, end))
                    foreach (Vector2Int cell in BrushBounds(center).allPositionsWithin)
                        if (region.Tiles.TryGetValue(cell, out TerrainTileVisual tile)) stroke.Apply(tile, MaterialFor(region));
            }
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private RectInt BrushBounds(Vector2Int cell) => new RectInt(cell - Vector2Int.one * (brushSize / 2), Vector2Int.one * brushSize);
        private static RectInt Rectangle(Vector2Int a, Vector2Int b) => new RectInt(Vector2Int.Min(a, b), Vector2Int.Max(a, b) - Vector2Int.Min(a, b) + Vector2Int.one);

        private void PaintRectangle(Vector2 from, Vector2 to)
        {
            TerrainPaintRegion hitA = Pick(from, out TerrainTileVisual tileA);
            TerrainPaintRegion hitB = Pick(to, out TerrainTileVisual tileB);
            foreach (TerrainPaintRegion region in StrokeRegions())
            {
                if (!CellAt(region, from, hitA, tileA, out Vector2Int a)
                    || !CellAt(region, to, hitB, tileB, out Vector2Int b)) continue;
                RectInt bounds = Rectangle(a, b);
                foreach (var entry in region.Tiles) if (bounds.Contains(entry.Key)) stroke.Apply(entry.Value, MaterialFor(region));
            }
        }

        private static bool CellAt(TerrainPaintRegion region, Vector2 mouse, TerrainPaintRegion picked,
            TerrainTileVisual tile, out Vector2Int cell)
        {
            if (picked == region && tile != null) { cell = region.Cells[tile]; return true; }
            return TerrainPaintSceneIndex.Project(HandleUtility.GUIPointToWorldRay(mouse), region.Region, out cell);
        }

        private void Sample(TerrainPaintRegion region, TerrainTileVisual tile)
        {
            currentRegion = region.Region;
            Material material = tile.PaintMaterial;
            variantIndex = material == null ? -1 : region.Profile.Variants.IndexOf(material);
            useCustomMaterial = material != null && variantIndex < 0;
            if (useCustomMaterial) customMaterial = material;
            tool = PaintTool.Brush; notice = "已吸取 " + (material != null ? material.name : "原始材质") + "。"; Repaint();
        }

        private void FinishStroke()
        {
            if (stroke == null) return;
            int count = stroke.Count;
            stroke.Finish(); stroke = null; strokeRegion = null;
            GUIUtility.hotControl = 0;
            notice = count > 0 ? "已保存本笔涂装：" + count + " 块。Ctrl+Z 可整笔撤销。" : "本笔没有改变地形涂装。";
            Repaint();
        }

        private void DrawBrush(TerrainPaintRegion region, Vector2Int cell)
        {
            RectInt bounds = tool == PaintTool.Brush || tool == PaintTool.Eraser ? BrushBounds(cell) : new RectInt(cell, Vector2Int.one);
            DrawBounds(region.Region, bounds, tool == PaintTool.Eraser ? new Color(1, 0.4f, 0.3f) : new Color(0.3f, 1, 0.7f));
        }

        private void DrawRectangle(Vector2 from, Vector2 to)
        {
            TerrainPaintRegion hitA = Pick(from, out TerrainTileVisual tileA);
            TerrainPaintRegion hitB = Pick(to, out TerrainTileVisual tileB);
            foreach (TerrainPaintRegion region in StrokeRegions())
                if (CellAt(region, from, hitA, tileA, out Vector2Int a)
                    && CellAt(region, to, hitB, tileB, out Vector2Int b))
                    DrawBounds(region.Region, Rectangle(a, b), new Color(0.3f, 1, 0.7f));
        }

        private static void DrawBounds(LevelRegion region, RectInt bounds, Color color)
        {
            float unit = region.UnitSize;
            Vector3 Point(float x, float y) => region.transform.TransformPoint(new Vector3(x * unit, y * unit, -0.02f));
            Handles.DrawSolidRectangleWithOutline(new[] { Point(bounds.xMin, bounds.yMin), Point(bounds.xMax, bounds.yMin),
                Point(bounds.xMax, bounds.yMax), Point(bounds.xMin, bounds.yMax) }, new Color(color.r, color.g, color.b, 0.07f), color);
        }
    }
}
