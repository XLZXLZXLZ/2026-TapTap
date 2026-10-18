#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TapTap.Editor
{
    public sealed partial class WorldMapEditorWindow : EditorWindow
    {
        private const float SidebarWidth = 300f;
        [SerializeField] private WorldMapDefinition map;
        [SerializeField] private Vector2 pan;
        [SerializeField] private float cellPixels = 2f;
        [SerializeField] private string selectedId;
        [SerializeField] private bool showTerrain = true;
        [SerializeField] private bool showNames = true;
        [SerializeField] private string search = "";
        private readonly List<LevelDefinition> library = new List<LevelDefinition>();
        private readonly Dictionary<LevelDefinition, Texture2D> thumbnails = new Dictionary<LevelDefinition, Texture2D>();
        private readonly Dictionary<LevelDefinition, int> thumbnailVersions = new Dictionary<LevelDefinition, int>();
        private readonly Dictionary<LevelDefinition, int> observedLevelVersions = new Dictionary<LevelDefinition, int>();
        private int observedMapVersion = -1;
        private Vector2 sidebarScroll;
        private bool fitPending = true;
        private LevelDefinition pendingLevel;
        private bool panning, dragging;
        private int canvasControlId;
        private bool resizing;
        private LevelFootprint originalFootprint, boundaryPreview;
        private LevelBoundaryEdge resizeEdge;
        private int resizeDistance;
        private Vector2Int dragAnchor, dragDelta;
        private string error;

        private static string PreferenceKey => "TapTap.WorldMap.LastMap." + Application.dataPath;
        private WorldMapModule Selected => map?.Modules?.Find(module => module != null && module.Id == selectedId);
        public WorldMapDefinition Definition => map;

        [MenuItem("TapTap/World Map Designer", false, 101)]
        public static void Open() => GetWindow<WorldMapEditorWindow>("大地图拼装").Show();

        public static void Open(WorldMapDefinition definition)
        {
            var window = GetWindow<WorldMapEditorWindow>("大地图拼装");
            window.UseMap(definition);
            window.Show();
        }

        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            var definition = EditorUtility.InstanceIDToObject(instanceId) as WorldMapDefinition;
            if (definition == null) return false;
            Open(definition);
            return true;
        }

        private void OnEnable()
        {
            minSize = new Vector2(850f, 540f);
            wantsMouseMove = true;
            designTool = DesignTool.Arrange;
            Undo.undoRedoPerformed += UndoChanged;
            EditorApplication.projectChanged += ProjectChanged;
            LevelLayoutChanges.Changed += LevelChanged;
            if (map == null) map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(
                EditorPrefs.GetString(PreferenceKey, "Assets/Runtime/WorldMaps/World_Character.asset"));
            RefreshLibrary();
            fitPending = true;
        }

        private void OnDisable()
        {
            CancelGesture();
            Undo.undoRedoPerformed -= UndoChanged;
            EditorApplication.projectChanged -= ProjectChanged;
            LevelLayoutChanges.Changed -= LevelChanged;
            ClearTextures();
        }

        private void OnLostFocus() => CancelGesture();

        private void UndoChanged() { CancelGesture(); ClearTextures(); InvalidateLevels(); error = null; Repaint(); }
        private void ProjectChanged() { ClearTextures(); RefreshLibrary(); Repaint(); }
        private void OnFocus() { ClearTextures(); InvalidateLevels(); Repaint(); }
        private void LevelChanged(LevelDefinition level)
        {
            if (resizing && Selected?.Level == level) CancelGesture();
            if (thumbnails.TryGetValue(level, out Texture2D texture))
            { if (texture != null) DestroyImmediate(texture); thumbnails.Remove(level); }
            thumbnailVersions.Remove(level);
            level.InvalidateFootprint();
            Repaint();
        }
        private void OnInspectorUpdate()
        {
            // Also cover Inspector changes and other tools which do not publish layout edits.
            if (map?.Modules == null) return;
            int mapVersion = EditorUtility.GetDirtyCount(map);
            if (mapVersion != observedMapVersion) { observedMapVersion = mapVersion; Repaint(); }
            foreach (WorldMapModule module in map.Modules)
            {
                LevelDefinition level = module?.Level; if (level == null) continue;
                int version = EditorUtility.GetDirtyCount(level);
                if (observedLevelVersions.TryGetValue(level, out int previous) && previous != version) LevelChanged(level);
                observedLevelVersions[level] = version;
            }
        }
        private void InvalidateLevels()
        {
            foreach (LevelDefinition level in library) if (level != null) level.InvalidateFootprint();
            if (map?.Modules != null) foreach (WorldMapModule module in map.Modules) module?.Level?.InvalidateFootprint();
        }

        private void RefreshLibrary()
        {
            library.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:LevelDefinition", new[] { "Assets" }))
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (level != null) library.Add(level);
            }
            library.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        }

        private void ClearTextures()
        {
            foreach (Texture2D texture in thumbnails.Values) if (texture != null) DestroyImmediate(texture);
            thumbnails.Clear();
            thumbnailVersions.Clear();
            observedLevelVersions.Clear();
        }

        private void UseMap(WorldMapDefinition next)
        {
            CancelGesture();
            map = next;
            selectedId = null;
            selectedModuleIds.Clear();
            hasRegion = false;
            pendingLevel = null;
            designTool = DesignTool.Arrange;
            error = null;
            ClearTextures();
            fitPending = true;
            if (map != null) EditorPrefs.SetString(PreferenceKey, AssetDatabase.GetAssetPath(map));
            Repaint();
        }

        private Texture2D Thumbnail(LevelDefinition level)
        {
            int version = EditorUtility.GetDirtyCount(level);
            if (!thumbnails.TryGetValue(level, out Texture2D texture) || texture == null || !thumbnailVersions.TryGetValue(level, out int cached) || cached != version)
            {
                if (texture != null) DestroyImmediate(texture);
                texture = WorldMapDrawing.LevelTexture(level);
                thumbnails[level] = texture;
                thumbnailVersions[level] = version;
            }
            return texture;
        }

        private void OnGUI()
        {
            // Reserve one stable ID before any controls whose count changes with selection or visibility.
            canvasControlId = GUIUtility.GetControlID("TapTap.WorldMapCanvas".GetHashCode(), FocusType.Passive);
            DrawToolbar();
            var canvas = new Rect(0f, 24f, Mathf.Max(1f, position.width - SidebarWidth), position.height - 48f);

            if (map != null)
            {
                if (fitPending) { Fit(canvas); fitPending = false; }
                HandleCanvas(canvas);

            }
            DrawCanvas(canvas);
            GUILayout.BeginArea(new Rect(position.width - SidebarWidth, 24f, SidebarWidth, position.height - 24f));
            sidebarScroll = EditorGUILayout.BeginScrollView(sidebarScroll);
            DrawSidebar();
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
            DrawStatus(new Rect(0f, position.height - 24f, position.width - SidebarWidth, 24f), canvas);
            HandleKeys();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(24f)))
            {
                var next = (WorldMapDefinition)EditorGUILayout.ObjectField(map, typeof(WorldMapDefinition), false, GUILayout.MinWidth(100f));
                if (next != map) UseMap(next);
                if (GUILayout.Button("新建地图", EditorStyles.toolbarButton, GUILayout.Width(75f))) CreateMap();
                using (new EditorGUI.DisabledScope(map == null))
                {
                    if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(42f))) Save();
                    if (GUILayout.Button("查看全图", EditorStyles.toolbarButton, GUILayout.Width(70f))) fitPending = true;
                    if (GUILayout.Button("导出 PNG", EditorStyles.toolbarButton, GUILayout.Width(72f))) ExportPng();
                    using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                        if (GUILayout.Button("保存并打开游玩", EditorStyles.toolbarButton, GUILayout.Width(108f))) GeneratePlayScene();
                }
            }
        }

        private void CreateMap()
        {
            LevelPrefabBuilder.EnsureFolder("Assets/Runtime/WorldMaps");
            string path = EditorUtility.SaveFilePanelInProject("新建大地图", "World_New", "asset", "保存大地图布局", "Assets/Runtime/WorldMaps");
            if (string.IsNullOrEmpty(path)) return;
            // Creation always uses a fresh path; choosing an existing asset does not replace it.
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            var definition = CreateInstance<WorldMapDefinition>();
            AssetDatabase.CreateAsset(definition, path);
            AssetDatabase.SaveAssetIfDirty(definition);
            UseMap(definition);
        }

        private void Save()
        {
            if (map == null) return;
            try
            {
                WorldMapSceneSync.SaveAndSynchronize(map);
                error = null;
                ShowNotification(new GUIContent("大地图与游玩场景已保存并同步"));
            }
            catch (Exception exception) { error = exception.Message; }
        }

        private void ExportPng()
        {
            if (map == null) return;
            string path = EditorUtility.SaveFilePanel("导出大地图像素预览", "", map.name, "png");
            if (string.IsNullOrEmpty(path)) return;
            Texture2D texture = null;
            try
            {

                texture = WorldMapDrawing.Export(map);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                if (path.Replace('\\', '/').StartsWith(Application.dataPath.Replace('\\', '/') + "/", StringComparison.OrdinalIgnoreCase)) AssetDatabase.Refresh();
                ShowNotification(new GUIContent("全图 PNG 已导出"));
                error = null;
            }
            catch (Exception exception) { error = exception.Message; }
            finally { if (texture != null) DestroyImmediate(texture); }
        }

        private void GeneratePlayScene()
        {
            try
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                string path = WorldMapSceneSync.SaveAndSynchronize(map);
                if (string.IsNullOrEmpty(path)) return;
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.SaveScene(scene, path);
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    WorldMapRoot world = root.GetComponent<WorldMapRoot>();
                    if (world == null) continue;
                    Selection.activeGameObject = world.gameObject;
                    if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(world.WorldBounds, false);
                    break;
                }
                error = null;
                ShowNotification(new GUIContent("完整游玩场景已保存并更新"));
            }
            catch (Exception exception) { error = exception.Message; }
        }

        private void DrawSidebar()
        {
            if (map == null)
            {
                EditorGUILayout.HelpBox("点击“新建地图”，或选择现有大地图资源。", MessageType.Info);
                return;
            }
            DrawDesignTools();
            showTerrain = EditorGUILayout.Toggle("显示内部地形", showTerrain);
            showNames = EditorGUILayout.Toggle("显示模块名称", showNames);
            EditorGUILayout.HelpBox("关卡按已有序号推进。修改布局后保存并同步大地图。", MessageType.None);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            GUILayout.Space(8f);
            WorldMapModule selected = Selected;
            int selectionCount = SelectedModules().Count;
            if (selectionCount > 1 && designTool == DesignTool.Arrange)
            {
                EditorGUILayout.LabelField("已选 " + selectionCount + " 个关卡模块", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("拖动选中的模块或选区内空白，整体移动。Shift + 左键拖动重新框选。方向键微调，Esc 取消拖动。", MessageType.None);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("复制所选")) Duplicate();
                    if (GUILayout.Button("删除所选")) DeleteSelected();
                }
                GUILayout.Space(8f);
            }
            if (selectionCount == 1 && selected != null && designTool != DesignTool.NewRegion)
            {
                EditorGUILayout.LabelField("选中的关卡模块", EditorStyles.boldLabel);
                EditorGUILayout.ObjectField("来源", selected.Level, typeof(LevelDefinition), false);
                string label = EditorGUILayout.DelayedTextField("显示名称", selected.Label);
                int sequence = Mathf.Max(0, EditorGUILayout.DelayedIntField("关卡序号", selected.Sequence));
                Vector2Int next = EditorGUILayout.Vector2IntField("关卡原点坐标", selected.Position);
                if (label != selected.Label || next != selected.Position || sequence != selected.Sequence)
                {
                    Record("调整关卡模块");
                    selected.Label = label;
                    selected.Position = next;
                    selected.Sequence = sequence;
                    Changed();
                }
                if (selected.Level != null)
                {
                    LevelDefinition level = selected.Level;
                    LevelFootprint footprint = DisplayFootprint(selected);
                    EditorGUILayout.LabelField("实际范围", footprint.Bounds.width + " × " + footprint.Bounds.height + " 格 / " + footprint.Cells.Count + " 格面积");
                    EditorGUILayout.LabelField("边界", level.UseCustomBoundary ? "手动调整" : "按最外层图格自动生成");
                    EditorGUILayout.HelpBox("选中后拖动白色轮廓，逐格调整范围。已有地形保持原位，缩边不会排除已有图块。同一来源的模块共用边界。", MessageType.None);
                    if (level.UseCustomBoundary && GUILayout.Button("恢复按图格自动生成边界"))
                    { CancelGesture(); LevelBoundaryEditing.Reset(level); Repaint(); }
                    string scenePath = LevelSceneNavigation.FindScenePath(level);
                    SceneAsset currentScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath ?? "");
                    SceneAsset nextScene = (SceneAsset)EditorGUILayout.ObjectField("导航场景", currentScene, typeof(SceneAsset), false);
                    if (nextScene != currentScene) LevelSceneNavigation.Assign(level, nextScene);
                    if (currentScene == null) EditorGUILayout.LabelField("首次导航将生成该关卡的测试场景", EditorStyles.miniLabel);
                    if (GUILayout.Button("打开场景并定位关卡")) Navigate(level);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(selected.Level == null))
                        if (GUILayout.Button("编辑单关")) LevelEditorWindow.Open(selected.Level);
                    if (GUILayout.Button("复制")) Duplicate();
                    if (GUILayout.Button("删除")) DeleteSelected();
                }
                GUILayout.Space(8f);
            }
            EditorGUILayout.LabelField("地图中的模块（" + map.Modules.Count + "）", EditorStyles.boldLabel);
            foreach (WorldMapModule module in map.Modules)
            {
                if (module == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool invalid = module.Level == null;
                    bool moduleSelected = ModuleSelected(module);
                    if (GUILayout.Toggle(moduleSelected, (invalid ? "⚠ " : "") + map.SequenceNumber(module) + ". " + module.DisplayName, "Button") != moduleSelected)
                        SelectModule(module);
                    if (GUILayout.Button("定位", GUILayout.Width(42f))) { SelectModule(module); FocusModule(module); }
                }
            }
            GUILayout.Space(8f);
            EditorGUILayout.LabelField("现有关卡模块库", EditorStyles.boldLabel);
            search = EditorGUILayout.TextField("搜索", search);
            if (pendingLevel != null)
            {
                EditorGUILayout.HelpBox("正在放置：" + pendingLevel.name + "\n点击大地图确定左下角；Esc 取消。", MessageType.Info);
                if (GUILayout.Button("取消放置")) pendingLevel = null;
            }
            foreach (LevelDefinition level in library)
            {
                if (search.Length > 0 && level.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                using (new EditorGUILayout.HorizontalScope(GUILayout.Height(45f)))
                {
                    Rect icon = GUILayoutUtility.GetRect(62f, 42f, GUILayout.Width(62f));
                    EditorGUI.DrawRect(icon, new Color(0.10f, 0.12f, 0.15f));
                    GUI.DrawTexture(icon, Thumbnail(level), ScaleMode.ScaleToFit, true);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        GUILayout.Label(level.name, EditorStyles.miniBoldLabel);
                        GUILayout.Label(level.Size.x + " × " + level.Size.y + " 格", EditorStyles.miniLabel);
                    }
                    if (GUILayout.Button("放置", GUILayout.Width(45f), GUILayout.Height(37f)))
                    {
                        CancelGesture();
                        designTool = DesignTool.Arrange;
                                    pendingLevel = level;
                        GUIUtility.keyboardControl = 0;
                    }
                }
            }
            GUILayout.Space(6f);
            EditorGUILayout.HelpBox("V 拼装 · B 框选新关卡\n空白处拖动框选 · Shift+拖动重新框选\n拖动所选模块或选区内空白整体移动\n滚轮缩放 · 中键或 Alt+左键平移\n单选后拖轮廓调整范围\n双击模块打开关卡编辑器 · F 查看全图\nCtrl+D 复制 · Delete 删除 · Ctrl+Z 撤销\nEsc 取消拖动 · 方向键逐格移动", MessageType.None);
        }

        private void Record(string action) => Undo.RegisterCompleteObjectUndo(map, action);
        private void Changed() { EditorUtility.SetDirty(map); error = null; Repaint(); }

        private void Add(LevelDefinition level, Vector2Int cell)
        {
            Record("放置关卡模块");
            var module = new WorldMapModule { Level = level, Position = cell, Sequence = map.NextSequence() };
            map.Modules.Add(module);
            SelectSingleModule(module);
            pendingLevel = null;
            Changed();
        }

        private void Duplicate()
        {
            List<WorldMapModule> selected = SelectedModules();
            if (selected.Count == 0) return;
            Record("复制关卡模块");
            selectedModuleIds.Clear();
            foreach (WorldMapModule source in selected)
            {
                var module = new WorldMapModule { Level = source.Level, Position = source.Position + new Vector2Int(2, -2), Label = source.Label, Sequence = map.NextSequence() };
                map.Modules.Add(module); selectedModuleIds.Add(module.Id);
            }
            selectedId = selectedModuleIds[0];
            Changed();
        }

        private void DeleteSelected()
        {
            List<WorldMapModule> selected = SelectedModules();
            if (selected.Count == 0) return;
            Record("删除关卡模块");
            foreach (WorldMapModule module in selected) map.Modules.Remove(module);
            SelectSingleModule(null);
            CancelGesture();
            Changed();
        }

        private void Fit(Rect canvas)
        {
            if (map == null) return;
            RectInt bounds = map.PreviewBounds;
            cellPixels = Mathf.Clamp(Mathf.Min((canvas.width - 40f) / bounds.width, (canvas.height - 40f) / bounds.height), 0.1f, 48f);
            pan = canvas.size * 0.5f - new Vector2(bounds.center.x, -bounds.center.y) * cellPixels;
        }

        private void FocusModule(WorldMapModule module)
        {
            Vector2 center = module.Level != null ? module.Bounds.center : (Vector2)module.Position;
            Vector2 canvasSize = new Vector2(position.width - SidebarWidth, position.height - 48f);
            pan = canvasSize * 0.5f - new Vector2(center.x, -center.y) * cellPixels;
        }

        private RectInt DisplayBounds(WorldMapModule module)
        {
            LevelFootprint footprint = DisplayFootprint(module);
            return new RectInt(DisplayPosition(module) + footprint.Bounds.position, footprint.Bounds.size);
        }
        private Vector2Int DisplayPosition(WorldMapModule module) => module.Position + (dragging && ModuleSelected(module) ? dragDelta : Vector2Int.zero);
        private LevelFootprint DisplayFootprint(WorldMapModule module) => resizing && module.Level == Selected?.Level ? boundaryPreview : module.Level.Footprint;

        private Rect CellRect(RectInt cells) => new Rect(pan.x + cells.xMin * cellPixels,
            pan.y + (-cells.yMax) * cellPixels, cells.width * cellPixels, cells.height * cellPixels);

        private Vector2 CellPoint(Vector2 cell) => pan + new Vector2(cell.x, -cell.y) * cellPixels;
        private Vector2Int MouseCell(Rect canvas) => new Vector2Int(Mathf.FloorToInt((Event.current.mousePosition.x - canvas.x - pan.x) / cellPixels),
            Mathf.FloorToInt(-(Event.current.mousePosition.y - canvas.y - pan.y) / cellPixels));

        private void Navigate(LevelDefinition level)
        {
            CancelGesture();
            try { LevelSceneNavigation.Open(level, Selected?.Id); error = null; }
            catch (Exception exception) { error = exception.Message; }
        }

        private void DrawFootprint(LevelFootprint footprint, Vector2Int origin, Color tint, Color outline, float thickness, bool handles = false)
        {
            foreach (RectInt run in footprint.Runs) EditorGUI.DrawRect(CellRect(new RectInt(run.position + origin, run.size)), tint);
            if (thickness <= 0f) return;
            foreach (LevelBoundaryEdge edge in footprint.Edges)
            {
                Vector2 start = CellPoint(edge.Start + origin), end = CellPoint(edge.End + origin);
                bool horizontal = edge.Outward.x == 0;
                Rect line = horizontal ? new Rect(start.x, start.y - thickness / 2f, end.x - start.x, thickness)
                    : new Rect(start.x - thickness / 2f, end.y, thickness, start.y - end.y);
                EditorGUI.DrawRect(line, outline);
                if (handles && (end - start).magnitude >= 10f)
                {
                    Vector2 center = (start + end) * 0.5f;
                    var handle = new Rect(center.x - 3f, center.y - 3f, 6f, 6f);
                    EditorGUI.DrawRect(handle, outline);
                    EditorGUIUtility.AddCursorRect(line, horizontal ? MouseCursor.ResizeVertical : MouseCursor.ResizeHorizontal);
                    EditorGUIUtility.AddCursorRect(handle, horizontal ? MouseCursor.ResizeVertical : MouseCursor.ResizeHorizontal);
                }
            }
        }

        private void DrawCanvas(Rect canvas)
        {
            EditorGUI.DrawRect(canvas, new Color(0.07f, 0.08f, 0.10f));
            bool pointerInside = canvas.Contains(Event.current.mousePosition);
            Vector2Int hover = map != null ? MouseCell(canvas) : Vector2Int.zero;
            GUI.BeginGroup(canvas);
            if (map == null) { GUI.Label(new Rect(25f, 25f, canvas.width - 50f, 50f), "创建或选择大地图后，在这里拼装关卡。"); GUI.EndGroup(); return; }
            Rect view = new Rect(0f, 0f, canvas.width, canvas.height);
            foreach (WorldMapModule module in map.Modules)
            {
                if (module?.Level == null) continue;
                RectInt bounds = DisplayBounds(module);
                LevelFootprint footprint = DisplayFootprint(module);
                Vector2Int origin = DisplayPosition(module);
                Rect rect = CellRect(bounds);
                if (!rect.Overlaps(view)) continue;
                Color color = WorldMapDrawing.ModuleColor(module), tint = color;
                bool selected = ModuleSelected(module) && designTool == DesignTool.Arrange;
                tint.a = selected ? 0.30f : 0.16f;
                DrawFootprint(footprint, origin, tint, Color.clear, 0f);
                if (showTerrain) GUI.DrawTexture(CellRect(new RectInt(origin + module.Level.CanvasOrigin, module.Level.Size)), Thumbnail(module.Level), ScaleMode.StretchToFill, true);
                DrawFootprint(footprint, origin, Color.clear, color, selected ? 4f : 1f);
                if (selected) DrawFootprint(footprint, origin, Color.clear, Color.white, 1f, selectedModuleIds.Count <= 1);
                if (showNames && rect.width >= 32f && rect.height >= 14f)
                {
                    Rect title = new Rect(rect.x + 2f, rect.y + 2f, Mathf.Max(0f, rect.width - 4f), Mathf.Min(18f, rect.height - 4f));
                    EditorGUI.DrawRect(title, new Color(0f, 0f, 0f, 0.72f));
                    var style = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
                    style.normal.textColor = Color.white;
                    GUI.Label(title, map.SequenceNumber(module) + ". " + module.DisplayName, style);
                }
            }
            DrawGrid(canvas);
            if (pendingLevel != null && pointerInside)
            {
                LevelFootprint footprint = pendingLevel.Footprint;
                Color tint = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                GUI.DrawTexture(CellRect(new RectInt(hover + pendingLevel.CanvasOrigin, pendingLevel.Size)), Thumbnail(pendingLevel), ScaleMode.StretchToFill, true);
                GUI.color = tint;
                DrawFootprint(footprint, hover, new Color(0.3f, 1f, 0.55f, 0.18f), Color.green, 2f);
            }
            DrawRegionSelection();
            DrawModuleSelection();
            GUI.EndGroup();
        }

        private void DrawGrid(Rect canvas)
        {
            int step = cellPixels >= 7f ? 1 : cellPixels >= 1f ? 8 : 32;
            Color color = new Color(1f, 1f, 1f, step == 1 ? 0.08f : 0.055f);
            int fromX = Mathf.FloorToInt(-pan.x / cellPixels / step) * step;
            int toX = Mathf.CeilToInt((canvas.width - pan.x) / cellPixels);
            int fromY = Mathf.FloorToInt((pan.y - canvas.height) / cellPixels / step) * step;
            int toY = Mathf.CeilToInt(pan.y / cellPixels);
            for (int x = fromX; x <= toX; x += step)
                EditorGUI.DrawRect(new Rect(pan.x + x * cellPixels, 0f, 1f, canvas.height), color);
            for (int y = fromY; y <= toY; y += step)
                EditorGUI.DrawRect(new Rect(0f, pan.y - y * cellPixels, canvas.width, 1f), color);
        }

        private bool TryPickEdge(Rect canvas, out LevelBoundaryEdge picked)
        {
            picked = default;
            if (Selected?.Level == null || selectedModuleIds.Count > 1 || pendingLevel != null || designTool != DesignTool.Arrange) return false;
            Vector2 pointer = Event.current.mousePosition - canvas.position;
            float nearest = 6f;
            bool found = false;
            foreach (LevelBoundaryEdge edge in Selected.Level.Footprint.Edges)
            {
                Vector2 a = CellPoint(edge.Start + Selected.Position), b = CellPoint(edge.End + Selected.Position);
                Vector2 line = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(pointer - a, line) / Mathf.Max(0.001f, line.sqrMagnitude));
                float distance = (pointer - a - line * t).magnitude;
                if (distance < nearest) { nearest = distance; picked = edge; found = true; }
            }
            return found;
        }

        private void HandleCanvas(Rect canvas)
        {
            Event e = Event.current;
            int control = canvasControlId;
            bool inside = canvas.Contains(e.mousePosition);
            if (!CanvasGestureActive && !e.shift && inside && TryPickEdge(canvas, out LevelBoundaryEdge hoverEdge))
                EditorGUIUtility.AddCursorRect(new Rect(e.mousePosition - Vector2.one * 7f, Vector2.one * 14f),
                    hoverEdge.Outward.x == 0 ? MouseCursor.ResizeVertical : MouseCursor.ResizeHorizontal);
            if (e.type == EventType.ScrollWheel && inside)
            {
                Vector2 local = e.mousePosition - canvas.position;
                float next = Mathf.Clamp(cellPixels * Mathf.Pow(1.15f, -e.delta.y), 0.1f, 48f);
                pan = local - (local - pan) * (next / cellPixels);
                cellPixels = next;
                e.Use(); Repaint();
            }
            else if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && inside)
            {
                LevelDefinition level = DraggedLevel();
                if (level == null) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                designTool = DesignTool.Arrange;
                pendingLevel = level;
                if (e.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); Add(level, MouseCell(canvas)); }
                e.Use(); Repaint();
            }
            else if (e.type == EventType.DragExited) { pendingLevel = null; Repaint(); }
            else if (e.type == EventType.MouseDown && inside && GUIUtility.hotControl == 0 && (e.button == 2 || e.button == 0 && e.alt))
            {
                panning = true;
                GUIUtility.hotControl = control;
                e.Use();
            }
            else if (e.type == EventType.MouseDown && inside && GUIUtility.hotControl == 0 && e.button == 0)
            {
                GUIUtility.keyboardControl = 0;
                Vector2Int cell = MouseCell(canvas);
                if (pendingLevel != null) Add(pendingLevel, cell);
                else if (designTool == DesignTool.NewRegion)
                {
                    previousHasRegion = hasRegion; previousRegionArea = regionArea;
                    drawingRegion = hasRegion = true;
                    regionAnchor = cell;
                    regionArea = WorldMapRegionCreator.Rectangle(cell, cell);
                    GUIUtility.hotControl = control;
                }
                else if (designTool == DesignTool.Arrange && e.shift) BeginModuleSelection(cell);
                else
                {
                    WorldMapModule picked = null;
                    for (int i = map.Modules.Count - 1; i >= 0; i--)
                        if (map.Modules[i]?.Level != null && map.Modules[i].Contains(cell)) { picked = map.Modules[i]; break; }
                    bool edgeHit = TryPickEdge(canvas, out LevelBoundaryEdge edge);
                    if (e.clickCount == 2 && (picked != null || edgeHit))
                    {
                        if (picked != null) SelectSingleModule(picked);
                        CancelGesture();
                        LevelEditorWindow.Open((picked ?? Selected).Level);
                    }
                    else if (edgeHit)
                    {
                        resizing = true;
                        resizeEdge = edge;
                        originalFootprint = boundaryPreview = Selected.Level.Footprint;
                        resizeDistance = 0;
                        dragAnchor = cell;
                        GUIUtility.hotControl = control;
                    }
                    else if (picked != null)
                    {
                        if (!ModuleSelected(picked)) SelectSingleModule(picked);
                        else selectedId = picked.Id;
                        dragging = true;
                        dragAnchor = cell;
                        dragDelta = Vector2Int.zero;
                        GUIUtility.hotControl = control;
                    }
                    else if (selectedModuleIds.Count > 1 && TrySelectedModuleBounds(out RectInt selectedBounds) && selectedBounds.Contains(cell))
                    {
                        dragging = true; dragAnchor = cell; dragDelta = Vector2Int.zero;
                        GUIUtility.hotControl = control;
                    }
                    else BeginModuleSelection(cell);
                }
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == control && CanvasGestureActive)
            {
                if (panning) pan += e.delta;
                else if (selectingModules) moduleSelectionArea = WorldMapRegionCreator.Rectangle(moduleSelectionAnchor, MouseCell(canvas));
                else if (drawingRegion) regionArea = WorldMapRegionCreator.Rectangle(regionAnchor, MouseCell(canvas));
                else if (resizing && Selected?.Level != null)
                {
                    Vector2Int delta = MouseCell(canvas) - dragAnchor;
                    resizeDistance = delta.x * resizeEdge.Outward.x + delta.y * resizeEdge.Outward.y;
                    boundaryPreview = LevelBoundaryEditing.ResizeEdge(Selected.Level, originalFootprint, resizeEdge, resizeDistance);
                }
                else if (dragging) dragDelta = MouseCell(canvas) - dragAnchor;
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == control && CanvasGestureActive)
            {
                if (selectingModules)
                {
                    SelectModulesInArea(WorldMapRegionCreator.Rectangle(moduleSelectionAnchor, MouseCell(canvas)));
                }
                if (drawingRegion)
                {
                    regionArea = WorldMapRegionCreator.Rectangle(regionAnchor, MouseCell(canvas));
                    drawingRegion = false;
                }
                if (resizing && Selected?.Level != null && resizeDistance != 0)
                {
                    LevelBoundaryEditing.Apply(Selected.Level, boundaryPreview);
                    error = null;
                }
                if (dragging && Selected != null)
                {
                    dragDelta = MouseCell(canvas) - dragAnchor;
                    MoveSelectedModules(dragDelta);
                }
                CancelGesture();
                GUIUtility.hotControl = 0;
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseMove && inside) Repaint();
        }

        private static LevelDefinition DraggedLevel()
        {
            foreach (UnityEngine.Object item in DragAndDrop.objectReferences)
            {
                if (item is LevelDefinition level) return level;
                if (item is GameObject prefab && prefab.GetComponent<LevelRegion>() is LevelRegion region && region.Source != null) return region.Source;
            }
            return null;
        }

        private bool CanvasGestureActive => panning || dragging || resizing || drawingRegion || selectingModules;

        private void CancelGesture()
        {
            if (CanvasGestureActive && GUIUtility.hotControl == canvasControlId) GUIUtility.hotControl = 0;
            if (drawingRegion) { hasRegion = previousHasRegion; regionArea = previousRegionArea; }
            drawingRegion = selectingModules = false;
            panning = dragging = resizing = false;
            originalFootprint = boundaryPreview = null;
            resizeDistance = 0;
            dragDelta = Vector2Int.zero;
        }

        private void HandleKeys()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return;
            if (e.keyCode == KeyCode.Escape) { CancelGesture(); pendingLevel = null; hasRegion = false; e.Use(); Repaint(); return; }
            if (map == null || CanvasGestureActive) return;
            bool modifier = e.control || e.command;
            if (modifier && e.keyCode == KeyCode.S) { Save(); e.Use(); }
            else if (!modifier && e.keyCode == KeyCode.V) { SetDesignTool(DesignTool.Arrange); e.Use(); }
            else if (!modifier && e.keyCode == KeyCode.B) { SetDesignTool(DesignTool.NewRegion); e.Use(); }
            else if (modifier && e.keyCode == KeyCode.D) { Duplicate(); e.Use(); }
            else if (!modifier && e.keyCode == KeyCode.F) { fitPending = true; e.Use(); Repaint(); }
            else if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            { if (designTool == DesignTool.NewRegion && hasRegion) hasRegion = false; else DeleteSelected(); e.Use(); Repaint(); }
            else if (!modifier && Selected != null)
            {
                Vector2Int direction = Vector2Int.zero;
                if (e.keyCode == KeyCode.LeftArrow) direction = Vector2Int.left;
                if (e.keyCode == KeyCode.RightArrow) direction = Vector2Int.right;
                if (e.keyCode == KeyCode.UpArrow) direction = Vector2Int.up;
                if (e.keyCode == KeyCode.DownArrow) direction = Vector2Int.down;
                if (direction != Vector2Int.zero)
                {
                    MoveSelectedModules(direction * (e.shift ? 8 : 1)); e.Use();
                }
            }
        }

        private void DrawStatus(Rect bar, Rect canvas)
        {
            EditorGUI.DrawRect(bar, new Color(0.16f, 0.18f, 0.21f));
            string text = "修改布局后保存并同步大地图";
            if (map != null)
            {
                text = ToolName + " · " + map.Modules.Count + " 个模块 · " + text;
                if (canvas.Contains(Event.current.mousePosition)) text += " · 坐标 " + MouseCell(canvas);
            }
            GUI.Label(new Rect(bar.x + 8f, bar.y + 3f, bar.width - 16f, bar.height - 4f), text, EditorStyles.miniLabel);
        }
    }

    [CustomEditor(typeof(WorldMapDefinition))]
    public sealed class WorldMapDefinitionInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("打开大地图拼装编辑器", GUILayout.Height(28f))) WorldMapEditorWindow.Open((WorldMapDefinition)target);
            DrawDefaultInspector();
        }
    }
}
#endif
