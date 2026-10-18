using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    // Run only in an isolated project: -batchmode -executeMethod TapTap.Editor.WorldMapRegressionTests.RunBatch.
    public static class WorldMapRegressionTests
    {
        private static readonly List<string> results = new List<string>();
        private const string Folder = "Assets/WorldMapRegression";

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project.");
            int exit = 0;
            try
            {
                LevelPrefabBuilder.EnsureFolder(Folder);
                Test("Module editing supports undo, independent copies and serialization", Editing);
                Test("Automatic footprints preserve L/S concavities, disconnected islands and enclosed interiors", IrregularFootprints);
                Test("Edge resizing preserves terrain, supports undo and updates shared modules and runtime bounds", BoundaryEditing);
                Test("Scene navigation finds existing scenes, binds by GUID and reuses the target scene", Navigation);
                Test("Region selection creates a same-size ordinary child level and permits occupied areas", RegionCreation);
                Test("Single-level editing immediately invalidates shared world-map previews without focus or saving", LivePreview);
                Test("Directional canvas expansion preserves terrain, world positions, selection, Custom content and undo", CanvasExpansion);
                Test("Whole-map mirrors preserve all layers and IDs, reflect directions and boundaries, refresh previews and support undo", MapMirror);
                Test("Inward canvas resizing supports every edge, preserves out-of-range terrain, clamps to one cell and supports undo", CanvasContraction);
            }
            catch (Exception exception) { results.Add("FAIL: " + exception); Debug.LogException(exception); exit = 1; }
            File.WriteAllLines(Path.Combine(Application.dataPath, "../world-map-regression-results.txt"), results);
            EditorApplication.Exit(exit);
        }

        private static void Test(string name, Action run) { run(); results.Add("PASS: " + name); Debug.Log("WORLD MAP PASS: " + name); }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

        private static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

        private static void Editing()
        {
            var map = ScriptableObject.CreateInstance<WorldMapDefinition>();
            AssetDatabase.CreateAsset(map, Folder + "/Editing.asset");
            var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>("Assets/Runtime/Levels/Level_Test_1.asset");
            Check(level != null, "Existing level was not imported.");
            Vector2Int originalSize = level.Size;
            var window = ScriptableObject.CreateInstance<WorldMapEditorWindow>();
            try
            {
                Invoke(window, "UseMap", map);
                Invoke(window, "Add", level, new Vector2Int(140, 120));
                Undo.FlushUndoRecordObjects();
                Check(map.Modules.Count == 1, "Placement failed.");
                string originalId = map.Modules[0].Id;
                Undo.IncrementCurrentGroup();
                Invoke(window, "Duplicate");
                Undo.FlushUndoRecordObjects();
                Check(map.Modules.Count == 2 && map.Modules[1].Id != originalId && map.Modules[1].Level == level, "Copy must reference the same source with a new ID.");
                Undo.PerformUndo();
                Check(map.Modules.Count == 1, "Undo did not remove the duplicate.");
                Undo.PerformRedo();
                Check(map.Modules.Count == 2, "Redo did not restore the duplicate.");
                Invoke(window, "DeleteSelected");
                Check(map.Modules.Count == 1, "Delete failed.");
                EditorUtility.SetDirty(map);
                AssetDatabase.SaveAssetIfDirty(map);
                string serialized = File.ReadAllText(Folder + "/Editing.asset");
                Check(serialized.Contains(originalId) && serialized.Contains("x: 140") && serialized.Contains("Modules:"), "Layout not serialized.");
                Check(level.Size == originalSize, "Source level changed by map editing.");
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        private static void IrregularFootprints()
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            var brush = ScriptableObject.CreateInstance<LevelBrush>();
            try
            {
                level.Size = new Vector2Int(16, 16);
                Check(level.Footprint.Cells.Count == 256, "Empty layouts should retain their editable canvas.");
                for (int y = 2; y < 9; y++) for (int x = 2; x < 9; x++)
                    if (x < 4 || y < 4) level.Put(new Vector2Int(x, y), brush, null);
                Check(level.Footprint.Contains(new Vector2Int(2, 8)) && !level.Footprint.Contains(new Vector2Int(8, 8)), "L concavity was filled.");
                Check(level.Footprint.Bounds.Equals(new RectInt(2, 2, 7, 7)) && level.Footprint.Edges.Count == 6, "L outline must follow its six segments.");
                level.Placements.Clear(); level.InvalidateFootprint();
                for (int y = 2; y < 9; y++) for (int x = 2; x < 9; x++)
                    if (y == 2 || y == 8 || y == 5 || x == 2 && y >= 5 || x == 8 && y <= 5)
                        level.Put(new Vector2Int(x, y), brush, null);
                Check(!level.Footprint.Contains(new Vector2Int(2, 3)) && !level.Footprint.Contains(new Vector2Int(8, 7)), "S outline lost its open concavities.");
                level.Put(new Vector2Int(13, 13), brush, null);
                Check(level.Footprint.Contains(new Vector2Int(13, 13)) && !level.Footprint.Contains(new Vector2Int(11, 11)), "Disconnected islands became a rectangle.");
                level.Placements.Clear(); level.InvalidateFootprint();
                for (int y = 2; y < 9; y++) for (int x = 2; x < 9; x++)
                    if (x == 2 || x == 8 || y == 2 || y == 8) level.Put(new Vector2Int(x, y), brush, null);
                Check(level.Footprint.Cells.Count == 49 && level.Footprint.Contains(new Vector2Int(5, 5)), "Enclosed playable interior was lost.");
                level.Erase(new Vector2Int(5, 8), brush.LayerId);
                Check(!level.Footprint.Contains(new Vector2Int(5, 5)), "Opening a ring must expose the empty interior to the exterior.");
                var note = ScriptableObject.CreateInstance<LevelBrush>(); note.HandlerId = "annotation";
                level.Put(new Vector2Int(15, 15), note, null);
                Check(!level.Footprint.Contains(new Vector2Int(15, 15)), "Test annotations must not enlarge playable bounds.");
                UnityEngine.Object.DestroyImmediate(note);
            }
            finally { UnityEngine.Object.DestroyImmediate(level); UnityEngine.Object.DestroyImmediate(brush); }
        }

        private static void BoundaryEditing()
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            AssetDatabase.CreateAsset(level, Folder + "/Boundary.asset");
            var brush = ScriptableObject.CreateInstance<LevelBrush>();
            var root = new GameObject("Boundary region");
            try
            {
                level.Size = new Vector2Int(12, 12);
                level.Put(new Vector2Int(1, 1), brush, null);
                LevelPlacement placement = level.Put(new Vector2Int(3, 3), brush, null);
                level.EntryPlacementId = placement.Id;
                level.UseCustomBoundary = true;
                foreach (Vector2Int cell in new RectInt(0, 0, 6, 6).allPositionsWithin) level.BoundaryCells.Add(cell);
                level.InvalidateFootprint(); EditorUtility.SetDirty(level);
                LevelFootprint original = level.Footprint;
                var right = new LevelBoundaryEdge(new Vector2Int(6, 0), new Vector2Int(6, 6), Vector2Int.right);
                LevelFootprint expanded = LevelBoundaryEditing.ResizeEdge(level, original, right, 3);
                Check(expanded.Bounds.width == 9 && level.Footprint.Bounds.width == 6 && level.BoundaryCells.Count == 36, "Preview mutated the source.");
                Undo.IncrementCurrentGroup(); LevelBoundaryEditing.Apply(level, expanded); Undo.FlushUndoRecordObjects();
                var first = new WorldMapModule { Level = level, Position = new Vector2Int(10, 20) };
                var second = new WorldMapModule { Level = level, Position = new Vector2Int(40, 20) };
                Check(first.Bounds.width == 9 && second.Bounds.width == 9, "Shared source boundaries disagree.");
                Check(placement.Cell == new Vector2Int(3, 3) && placement.Id == level.EntryPlacementId && level.Size == new Vector2Int(12, 12), "Resizing moved terrain or changed the source canvas/entry.");
                Undo.PerformUndo(); level.InvalidateFootprint();
                Check(level.Footprint.Bounds.width == 6, "Boundary undo failed.");
                Undo.PerformRedo(); level.InvalidateFootprint(); Check(level.Footprint.Bounds.width == 9, "Boundary redo failed.");
                LevelFootprint shrunk = LevelBoundaryEditing.ResizeEdge(level, original, right, -5);
                Check(shrunk.Contains(new Vector2Int(3, 3)) && shrunk.Contains(new Vector2Int(1, 1)) && !shrunk.Contains(new Vector2Int(5, 5)), "Inward drag removed terrain or retained empty fringe.");
                var partial = new LevelBoundaryEdge(new Vector2Int(6, 2), new Vector2Int(6, 4), Vector2Int.right);
                LevelFootprint partialShape = LevelBoundaryEditing.ResizeEdge(level, original, partial, 2);
                Check(partialShape.Contains(new Vector2Int(7, 3)) && !partialShape.Contains(new Vector2Int(7, 1)), "Resizing a segment changed the whole side.");
                var left = new LevelBoundaryEdge(new Vector2Int(0, 0), new Vector2Int(0, 6), Vector2Int.left);
                LevelBoundaryEditing.Apply(level, LevelBoundaryEditing.ResizeEdge(level, original, left, 2));
                LevelRegion region = root.AddComponent<LevelRegion>(); region.Source = level; region.UnitSize = 2f;
                root.transform.position = new Vector3(10, 20, 0);
                Check(region.WorldBounds.min == new Vector3(6, 20, 0) && region.WorldBounds.max == new Vector3(22, 32, 0), "Runtime bounds ignore local negative boundary offsets.");
                LevelBoundaryEditing.Reset(level);
                Check(!level.UseCustomBoundary && level.Footprint.Bounds.Equals(new RectInt(1, 1, 3, 3)), "Reset did not restore the outermost tile footprint.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(brush); }
        }

        private static void Navigation()
        {
            var source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<LevelDefinition>("Assets/Runtime/Levels/Level_Test_1.asset"));
            source.name = "NavigationSource"; source.OutputPrefab = null; source.TestSceneGuid = "";
            string sourcePath = AssetDatabase.GenerateUniqueAssetPath(Folder + "/NavigationSource.asset");
            AssetDatabase.CreateAsset(source, sourcePath);
            Check(string.IsNullOrEmpty(LevelSceneNavigation.FindScenePath(source)), "Fresh source unexpectedly has a scene.");
            Check(LevelSceneNavigation.Open(source), "First navigation did not generate a scene.");
            source = AssetDatabase.LoadAssetAtPath<LevelDefinition>(sourcePath);
            string path = LevelSceneNavigation.FindScenePath(source);
            Check(!string.IsNullOrEmpty(source.TestSceneGuid) && AssetDatabase.GUIDToAssetPath(source.TestSceneGuid) == path, "Scene creation did not bind its source.");
            string guid = source.TestSceneGuid;
            LevelSceneNavigation.Assign(source, null);
            Check(LevelSceneNavigation.FindScenePath(source) == path, "Legacy scene dependency lookup failed.");
            LevelSceneNavigation.Assign(source, AssetDatabase.LoadAssetAtPath<SceneAsset>(path));
            string renamed = AssetDatabase.GenerateUniqueAssetPath(LevelTestSceneBuilder.TestSceneFolder + "/NavigationRenamed.unity");
            Check(string.IsNullOrEmpty(AssetDatabase.MoveAsset(path, renamed)), "Scene rename failed.");
            Check(source.TestSceneGuid == guid && LevelSceneNavigation.FindScenePath(source) == renamed, "GUID scene binding broke after rename.");
            int before = AssetDatabase.FindAssets("t:Scene").Length;
            Check(LevelSceneNavigation.Open(source), "Scene navigation failed.");
            Check(Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<LevelRegion>()?.Source == source, "Navigation did not select the target region.");
            Check(SceneManager.GetActiveScene().path == renamed, "Wrong scene opened.");
            Check(LevelSceneNavigation.Open(source) && AssetDatabase.FindAssets("t:Scene").Length == before, "Repeat navigation created a duplicate scene.");
            Check(EditorSceneManager.SaveScene(SceneManager.GetActiveScene()), "Navigation scene could not be saved.");
            Scene shared = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            source = AssetDatabase.LoadAssetAtPath<LevelDefinition>(sourcePath);
            var first = (GameObject)PrefabUtility.InstantiatePrefab(source.OutputPrefab, shared);
            var second = (GameObject)PrefabUtility.InstantiatePrefab(source.OutputPrefab, shared);
            const string moduleId = "navigation-second-module";
            first.name = "First room"; second.name = "Second room " + moduleId;
            second.transform.position = new Vector3(80, 40, 0);
            string sharedPath = AssetDatabase.GenerateUniqueAssetPath(Folder + "/SharedNavigation.unity");
            Check(EditorSceneManager.SaveScene(shared, sharedPath), "Shared scene creation failed.");
            AssetDatabase.ImportAsset(sharedPath, ImportAssetOptions.ForceSynchronousImport);
            source = AssetDatabase.LoadAssetAtPath<LevelDefinition>(sourcePath);
            LevelSceneNavigation.Assign(source, AssetDatabase.LoadAssetAtPath<SceneAsset>(sharedPath));
            Check(LevelSceneNavigation.Open(source, moduleId) && Selection.activeGameObject == second, "Shared-scene navigation selected the wrong copy of a level.");
        }

        private static void RegionCreation()
        {
            var map = ScriptableObject.CreateInstance<WorldMapDefinition>();
            string mapPath = AssetDatabase.GenerateUniqueAssetPath(Folder + "/RegionCreation.asset");
            AssetDatabase.CreateAsset(map, mapPath); map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(mapPath);
            RectInt area = WorldMapRegionCreator.Rectangle(new Vector2Int(153, 139), new Vector2Int(140, 130));
            Check(area.Equals(new RectInt(140, 130, 14, 10)), "Reverse dragging or inclusive dimensions are wrong.");
            Check(WorldMapRegionCreator.Rectangle(new Vector2Int(140, 130), new Vector2Int(140, 130)).size == Vector2Int.one, "One-cell selections must be allowed.");
            Check(WorldMapRegionCreator.Validate(map, area) == null, "Valid region rejected.");
            Check(WorldMapRegionCreator.Validate(map, new RectInt(-100, -100, 14, 10)) == null, "Negative-coordinate region rejected.");
            var crossing = new RectInt(96, 245, 144, 12);
            Check(WorldMapRegionCreator.Validate(map, crossing) == null, "Removed template still restricts new regions.");
            var palette = AssetDatabase.LoadAssetAtPath<LevelPalette>(LevelEditorAssets.DefaultPalettePath);
            Undo.IncrementCurrentGroup();
            WorldMapModule created = WorldMapRegionCreator.Create(map, area, palette, Folder + "/RegionLevel.asset");
            map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(mapPath);
            Check(created.Position == area.min && created.Level.Size == area.size && created.Level.Palette == palette, "Created child size, origin or palette is wrong.");
            Check(!created.Level.UseCustomBoundary && created.Level.BoundaryCells.Count == 0, "Creation must not impose a manual boundary.");
            Check(WorldMapRegionCreator.Validate(map, area) == null, "Occupied area must permit shortcut creation.");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Check(map.Modules.Count == 0 && AssetDatabase.LoadAssetAtPath<LevelDefinition>(AssetDatabase.GetAssetPath(created.Level)) != null, "Undo should remove placement while retaining the reusable child asset.");
            Undo.PerformRedo(); Check(map.Modules.Count == 1, "Region placement redo failed.");
            LevelDefinition child = map.Modules[0].Level;
            var brush = palette.Brushes.Find(item => item != null && item.HandlerId != "annotation");
            child.Put(new Vector2Int(2, 2), brush, null); EditorUtility.SetDirty(child); LevelLayoutChanges.Notify(child);
            Check(child.Size == area.size && child.Footprint.Bounds.Equals(new RectInt(2, 2, 1, 1)), "Child canvas size or automatic footprint is wrong after painting.");
            var editor = ScriptableObject.CreateInstance<LevelEditorWindow>();
            try
            {
                Invoke(editor, "UseLayout", child);
                Check((LevelDefinition)typeof(LevelEditorWindow).GetField("layout", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor) == child, "New child cannot be opened in the level editor.");
            }
            finally { UnityEngine.Object.DestroyImmediate(editor); }
            string occupiedPath = AssetDatabase.GetAssetPath(child);
            WorldMapModule next = WorldMapRegionCreator.Create(map, area, palette, occupiedPath);
            Check(next.Level != child && AssetDatabase.GetAssetPath(next.Level) != occupiedPath && child.Placements.Count == 1, "Creating a child overwrote an existing layout.");
            Check(next.Position == area.min &&
                next.Level.Size == area.size && !next.Level.UseCustomBoundary, "Overlapping child creation imposed a boundary or moved the child.");
        }

        private static void CanvasContraction()
        {
            foreach (Vector2Int side in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
            {
                var level = ScriptableObject.CreateInstance<LevelDefinition>();
                level.CanvasOrigin = new Vector2Int(-3, -2); level.Size = new Vector2Int(6, 5);
                level.Palette = AssetDatabase.LoadAssetAtPath<LevelPalette>(LevelEditorAssets.DefaultPalettePath);
                var terrain = AssetDatabase.LoadAssetAtPath<LevelBrush>("Assets/Runtime/LevelDesigner/Brushes/Terrain.asset");
                level.Put(new Vector2Int(-3, -2), terrain, null);
                level.Put(new Vector2Int(2, 2), terrain, null);
                LevelPlacement center = level.Put(Vector2Int.zero, terrain, null);
                var checkpoint = AssetDatabase.LoadAssetAtPath<LevelBrush>("Assets/Runtime/LevelDesigner/Brushes/Checkpoint.asset");
                LevelPlacement entry = level.Put(new Vector2Int(2, 2), checkpoint, new CheckpointPlacementSettings());
                level.EntryPlacementId = entry.Id;
                level.UseCustomBoundary = true;
                level.BoundaryCells.AddRange(new[] { new Vector2Int(-3, -2), new Vector2Int(-3, -1), new Vector2Int(2, 2) });
                AssetDatabase.CreateAsset(level, AssetDatabase.GenerateUniqueAssetPath(Folder + "/Contract.asset"));
                string original = EditorJsonUtility.ToJson(level);
                var map = ScriptableObject.CreateInstance<WorldMapDefinition>();
                map.Modules.Add(new WorldMapModule { Level = level, Position = new Vector2Int(140, 130) });
                var editor = ScriptableObject.CreateInstance<LevelEditorWindow>();
                var world = ScriptableObject.CreateInstance<WorldMapEditorWindow>();
                try
                {
                    Invoke(editor, "UseLayout", level); Invoke(world, "UseMap", map);
                    typeof(LevelEditorWindow).GetField("hasAreaSelection", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(editor, true);
                    typeof(LevelEditorWindow).GetField("selectionArea", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(editor, level.CanvasBounds);
                    Rect before = (Rect)Invoke(editor, "CellRect", center.Cell);
                    Texture2D cached = (Texture2D)Invoke(world, "Thumbnail", level);
                    var originalFootprint = new HashSet<Vector2Int>(level.Footprint.Cells);
                    Invoke(editor, "ApplyCanvasExpansion", side, -2);
                    RectInt expected = side == Vector2Int.left ? new RectInt(-1, -2, 4, 5)
                        : side == Vector2Int.right ? new RectInt(-3, -2, 4, 5)
                        : side == Vector2Int.up ? new RectInt(-3, -2, 6, 3) : new RectInt(-3, 0, 6, 3);
                    Check(level.CanvasBounds.Equals(expected), "Inward drag resized the wrong edge: " + side);
                    Check(level.Placements.Count == 4 && level.EntryPlacement.Id == entry.Id && entry.Cell == new Vector2Int(2, 2), "Contraction removed or moved terrain or the entry.");
                    Check((int)Invoke(editor, "OutsideCount") > 0, "Retained outside terrain is not reported.");
                    Check(level.BoundaryCells.Count == 3 && level.BoundaryCells[0] == new Vector2Int(-3, -2) && originalFootprint.SetEquals(level.Footprint.Cells), "Contraction rewrote the actual level boundary.");
                    Check(((Rect)Invoke(editor, "CellRect", center.Cell)).Equals(before), "Inward drag shifted existing content on screen.");
                    Check(cached == null && map.Modules[0].Position == new Vector2Int(140, 130), "Contraction did not refresh the preview or moved the module.");
                    Check(((RectInt)typeof(LevelEditorWindow).GetField("selectionArea", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor)).Equals(expected), "Selection was not clipped to the smaller canvas.");
                    Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                    Check(EditorJsonUtility.ToJson(level) == original, "Contraction undo did not restore all level data.");
                    Undo.PerformRedo(); Check(level.CanvasBounds.Equals(expected), "Contraction redo failed.");
                    Invoke(editor, "ApplyCanvasExpansion", side, 2);
                    Check(EditorJsonUtility.ToJson(level) == original && (int)Invoke(editor, "OutsideCount") == 0, "Expanding again did not recover retained terrain.");
                    Invoke(editor, "ApplyCanvasExpansion", side, -999);
                    Check((side.x != 0 ? level.Size.x : level.Size.y) == 1 && level.Placements.Count == 4, "Contraction did not clamp at one cell or lost terrain.");
                    string smallest = EditorJsonUtility.ToJson(level);
                    Invoke(editor, "ApplyCanvasExpansion", side, -1);
                    Check(EditorJsonUtility.ToJson(level) == smallest, "Dragging farther inward changed a one-cell canvas.");
                    Check(LevelCanvasEditing.ResizedBounds(new RectInt(-3, -2, 6, 5), side, 9999).size ==
                        (side.x != 0 ? new Vector2Int(2048, 5) : new Vector2Int(6, 2048)), "Outward resizing exceeded the size limit.");
                }
                finally { UnityEngine.Object.DestroyImmediate(editor); UnityEngine.Object.DestroyImmediate(world); UnityEngine.Object.DestroyImmediate(map); }
            }
        }

        private static void MapMirror()
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.CanvasOrigin = new Vector2Int(-3, -2); level.Size = new Vector2Int(8, 6);
            level.Palette = AssetDatabase.LoadAssetAtPath<LevelPalette>(LevelEditorAssets.DefaultPalettePath);
            LevelBrush Brush(string name) => AssetDatabase.LoadAssetAtPath<LevelBrush>("Assets/Runtime/LevelDesigner/Brushes/" + name + ".asset");
            level.Put(new Vector2Int(-3, -2), Brush("Terrain"), null);
            level.Put(new Vector2Int(-3, -1), Brush("Terrain"), null);
            LevelPlacement entry = level.Put(new Vector2Int(-2, -1), Brush("Checkpoint"),
                new CheckpointPlacementSettings { SpawnOffset = new Vector2(0.25f, 0.75f) });
            level.EntryPlacementId = entry.Id;
            LevelPlacement belt = level.Put(new Vector2Int(-1, 0), Brush("Conveyor"),
                new ConveyorPlacementSettings { Speed = 2f, FacingLeftWhenStopped = true });
            LevelPlacement stopped = level.Put(new Vector2Int(0, 0), Brush("Conveyor"),
                new ConveyorPlacementSettings { Speed = 0f, FacingLeftWhenStopped = false });
            for (int i = 0; i < 4; i++) level.Put(new Vector2Int(i - 2, 2), Brush("Spikes"), new SpikePlacementSettings { QuarterTurns = i });
            LevelPlacement note = level.Put(new Vector2Int(-3, -2), Brush("Annotation"), new AnnotationPlacementSettings { Text = "入口提示，保持可读" });
            level.UseCustomBoundary = true;
            level.BoundaryCells.AddRange(new[] { new Vector2Int(-3, -2), new Vector2Int(-2, -2), new Vector2Int(-3, -1) });
            AssetDatabase.CreateAsset(level, AssetDatabase.GenerateUniqueAssetPath(Folder + "/Mirror.asset"));
            string original = EditorJsonUtility.ToJson(level);
            Vector2Int[] originalCells = level.Placements.ConvertAll(item => item.Cell).ToArray();
            Vector2Int[] originalBoundary = level.BoundaryCells.ToArray();
            var map = ScriptableObject.CreateInstance<WorldMapDefinition>();
            map.Modules.Add(new WorldMapModule { Level = level, Position = new Vector2Int(140, 130) });
            var world = ScriptableObject.CreateInstance<WorldMapEditorWindow>();
            var editor = ScriptableObject.CreateInstance<LevelEditorWindow>();
            try
            {
                Invoke(world, "UseMap", map); Invoke(editor, "UseLayout", level);
                typeof(LevelEditorWindow).GetField("isolateLayer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(editor, true);
                typeof(LevelEditorWindow).GetField("selectedPlacementId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(editor, entry.Id);
                typeof(LevelEditorWindow).GetField("selectedCell", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(editor, (Vector2Int?)entry.Cell);
                typeof(LevelEditorWindow).GetField("hasAreaSelection", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(editor, true);
                typeof(LevelEditorWindow).GetField("selectionArea", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(editor, new RectInt(-3, -2, 3, 2));
                Texture2D cached = (Texture2D)Invoke(world, "Thumbnail", level);
                LevelFootprint footprint = level.Footprint;
                Invoke(editor, "ApplyMapMirror", true);
                Check(cached == null && !ReferenceEquals(footprint, level.Footprint), "Mirror retained the old preview or footprint.");
                for (int i = 0; i < originalCells.Length; i++)
                    Check(level.Placements[i].Cell == new Vector2Int(1 - originalCells[i].x, originalCells[i].y), "Horizontal mirror missed a layer or negative-origin cell.");
                for (int i = 0; i < originalBoundary.Length; i++)
                    Check(level.BoundaryCells[i] == new Vector2Int(1 - originalBoundary[i].x, originalBoundary[i].y), "Manual boundary did not mirror.");
                Check(level.EntryPlacement.Id == entry.Id && ((CheckpointPlacementSettings)entry.Settings).SpawnOffset == new Vector2(-0.25f, 0.75f), "Entry reference or spawn offset was lost.");
                Check(((ConveyorPlacementSettings)belt.Settings).Speed == -2f && !((ConveyorPlacementSettings)belt.Settings).FacingLeftWhenStopped &&
                    ((ConveyorPlacementSettings)stopped.Settings).FacingLeft, "Moving or stopped belt did not reverse.");
                for (int i = 0; i < 4; i++) Check(((SpikePlacementSettings)level.Placements[i + 5].Settings).QuarterTurns == ((4 - i) & 3), "Spike horizontal direction incorrect.");
                Check(((AnnotationPlacementSettings)note.Settings).Text == "入口提示，保持可读", "Annotation text was modified.");
                Check(((RectInt)typeof(LevelEditorWindow).GetField("selectionArea", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor)).Equals(new RectInt(2, -2, 3, 2)), "Selection did not follow mirror.");
                Texture2D thumbnail = (Texture2D)Invoke(world, "Thumbnail", level);
                Check(thumbnail.GetPixel(14, 0).a > 0.9f && thumbnail.GetPixel(0, 0).a == 0f, "World thumbnail does not show mirrored terrain.");
                Check(level.CanvasBounds.Equals(new RectInt(-3, -2, 8, 6)) && map.Modules[0].Position == new Vector2Int(140, 130), "Mirror changed canvas dimensions or module origin.");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check(EditorJsonUtility.ToJson(level) == original, "Undo did not restore IDs, boundary or managed settings.");
                Undo.PerformRedo(); Check(level.Placements[0].Cell == new Vector2Int(4, -2), "Mirror redo failed.");
                Invoke(editor, "ApplyMapMirror", true);
                Check(EditorJsonUtility.ToJson(level) == original, "Mirroring twice did not restore the map.");
                Invoke(editor, "ApplyMapMirror", false);
                for (int i = 0; i < originalCells.Length; i++)
                    Check(level.Placements[i].Cell == new Vector2Int(originalCells[i].x, 1 - originalCells[i].y), "Vertical mirror missed a cell.");
                for (int i = 0; i < 4; i++) Check(((SpikePlacementSettings)level.Placements[i + 5].Settings).QuarterTurns == ((2 - i) & 3), "Spike vertical direction incorrect.");
                Check(((ConveyorPlacementSettings)level.Placements[3].Settings).Speed == 2f &&
                    ((CheckpointPlacementSettings)level.EntryPlacement.Settings).SpawnOffset == new Vector2(0.25f, -0.75f), "Vertical mirror changed belt direction or missed spawn offset.");
                Invoke(editor, "ApplyMapMirror", false);
                Check(EditorJsonUtility.ToJson(level) == original, "Vertical double mirror did not restore the map.");
                var legacy = new LevelPlacement { Brush = Brush("Spikes"), Settings = new EmptyPlacementSettings() };
                LevelBrushHandlers.Get("spikes").MirrorSettings(legacy, false);
                Check(((SpikePlacementSettings)legacy.Settings).QuarterTurns == 2, "Legacy upward spikes do not reflect downward.");
            }
            finally { UnityEngine.Object.DestroyImmediate(editor); UnityEngine.Object.DestroyImmediate(world); UnityEngine.Object.DestroyImmediate(map); }
        }

        private static void CanvasExpansion()
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>(); level.Size = new Vector2Int(4, 3);
            level.Palette = AssetDatabase.LoadAssetAtPath<LevelPalette>(LevelEditorAssets.DefaultPalettePath);
            var brush = AssetDatabase.LoadAssetAtPath<LevelBrush>("Assets/Runtime/LevelDesigner/Brushes/Terrain.asset");
            LevelPlacement original = level.Put(new Vector2Int(1, 1), brush, null);
            string levelPath = AssetDatabase.GenerateUniqueAssetPath(Folder + "/CanvasExpansion.asset");
            AssetDatabase.CreateAsset(level, levelPath);
            var map = ScriptableObject.CreateInstance<WorldMapDefinition>();
            var module = new WorldMapModule { Level = level, Position = new Vector2Int(140, 130) }; map.Modules.Add(module);
            var editor = ScriptableObject.CreateInstance<LevelEditorWindow>();
            string prefabPath = AssetDatabase.GenerateUniqueAssetPath(Folder + "/CanvasExpansion.prefab");
            try
            {
                GameObject prefab = LevelPrefabBuilder.Save(level, prefabPath);
                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                new GameObject("Keep position").transform.SetParent(root.transform.Find("Custom"), false);
                root.transform.Find("Custom/Keep position").localPosition = new Vector3(2f, 1.5f, 0f);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath); PrefabUtility.UnloadPrefabContents(root);
                Invoke(editor, "UseLayout", level);
                Rect screenBefore = (Rect)Invoke(editor, "CellRect", original.Cell);
                Texture2D before = WorldMapDrawing.Export(map);
                Color sample = before.GetPixel(7, 7); UnityEngine.Object.DestroyImmediate(before);
                Invoke(editor, "ApplyCanvasExpansion", Vector2Int.left, 3);
                Invoke(editor, "ApplyCanvasExpansion", Vector2Int.right, 2);
                Invoke(editor, "ApplyCanvasExpansion", Vector2Int.up, 2);
                Invoke(editor, "ApplyCanvasExpansion", Vector2Int.down, 1);
                Check(level.CanvasBounds.Equals(new RectInt(-3, -1, 9, 6)), "Expansion did not add space to the requested side.");
                Check(original.Cell == new Vector2Int(1, 1) && module.Position == new Vector2Int(140, 130), "Expansion moved existing terrain in the world map.");
                Check(!level.UseCustomBoundary && level.BoundaryCells.Count == 0 && level.Footprint.Bounds.Equals(new RectInt(1, 1, 1, 1)), "Canvas expansion must not impose a manual footprint.");
                Check(((Rect)Invoke(editor, "CellRect", original.Cell)).Equals(screenBefore), "Expansion moved existing content on screen.");
                Check((Vector2Int)Invoke(editor, "MouseToCell", screenBefore.center) == original.Cell, "Expanded canvas mouse mapping is incorrect.");
                Check((Vector2Int)Invoke(editor, "ClampCell", new Vector2Int(-999, 999)) == new Vector2Int(-3, 4), "Negative canvas coordinates are incorrectly clamped.");
                typeof(LevelEditorWindow).GetField("selectionArea", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(editor, new RectInt(1, 1, 1, 1));
                Check((Vector2Int)Invoke(editor, "ClampSelectionMove", new Vector2Int(-999, 999)) == new Vector2Int(-4, 3), "Selection cannot move into the expanded canvas.");
                Texture2D after = WorldMapDrawing.Export(map);
                Check(after.GetPixel(7, 7) == sample, "World export shifted the existing terrain."); UnityEngine.Object.DestroyImmediate(after);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check(level.CanvasBounds.Equals(new RectInt(-3, 0, 9, 5)), "Undo did not restore the canvas origin and dimensions together.");
                Undo.PerformRedo(); Check(level.CanvasBounds.Equals(new RectInt(-3, -1, 9, 6)), "Canvas expansion redo failed.");
                level.Put(new Vector2Int(-3, -1), brush, null); EditorUtility.SetDirty(level); LevelLayoutChanges.Notify(level);
                Check(level.Contains(new Vector2Int(-3, -1)) && level.Placements.Count == 2 && !level.Contains(new Vector2Int(-4, -1)), "Painting into the added space is incorrect.");
                Check(LevelGridEditing.ConnectedArea(level, new Vector2Int(-2, 0), brush.LayerId).Contains(new Vector2Int(-3, 0)), "Bucket fill cannot reach the added space.");
                Texture2D thumbnail = WorldMapDrawing.LevelTexture(level);
                Check(thumbnail.GetPixel(0, 0).a > 0.9f, "Negative-coordinate terrain missing from thumbnail."); UnityEngine.Object.DestroyImmediate(thumbnail);
                after = WorldMapDrawing.Export(map);
                Check(after.GetPixel(7, 7) == (Color)(Color32)brush.Color, "Negative-coordinate terrain missing from world export."); UnityEngine.Object.DestroyImmediate(after);
                prefab = LevelPrefabBuilder.Save(level, prefabPath);
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                Check(root.transform.Find("Custom/Keep position").localPosition == new Vector3(2f, 1.5f, 0f), "Expansion moved Custom prefab content.");
                bool found = false;
                foreach (Transform item in root.transform.Find("Generated").GetComponentsInChildren<Transform>())
                    if (item.name.EndsWith(original.Id))
                    { found = true; Check((Vector2)item.localPosition == ((Vector2)original.Cell + brush.Anchor) * level.Palette.UnitSize, "Expansion moved existing generated terrain."); }
                Check(found, "Original terrain disappeared from the prefab.");
                PrefabUtility.UnloadPrefabContents(root);
                AssetDatabase.SaveAssetIfDirty(level);
                Check(File.ReadAllText(levelPath).Contains("CanvasOrigin:"), "Directional canvas origin was not saved.");
                var empty = ScriptableObject.CreateInstance<LevelDefinition>(); empty.Size = new Vector2Int(4, 3);
                LevelCanvasEditing.Expand(empty, Vector2Int.left, 2);
                Check(empty.Footprint.Bounds.Equals(new RectInt(-2, 0, 6, 3)), "An empty expanded canvas ignores its new origin.");
                UnityEngine.Object.DestroyImmediate(empty);
            }
            finally { UnityEngine.Object.DestroyImmediate(editor); UnityEngine.Object.DestroyImmediate(map); }
        }

        private static void LivePreview()
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>(); level.Size = new Vector2Int(8, 8);
            var map = ScriptableObject.CreateInstance<WorldMapDefinition>();
            map.Modules.Add(new WorldMapModule { Level = level, Position = new Vector2Int(140, 130) });
            map.Modules.Add(new WorldMapModule { Level = level, Position = new Vector2Int(155, 130) });
            var brush = ScriptableObject.CreateInstance<LevelBrush>(); brush.Color = Color.red;
            var world = ScriptableObject.CreateInstance<WorldMapEditorWindow>();
            var editor = ScriptableObject.CreateInstance<LevelEditorWindow>();
            try
            {
                Invoke(world, "UseMap", map);
                var method = typeof(WorldMapEditorWindow).GetMethod("Thumbnail", BindingFlags.Instance | BindingFlags.NonPublic);
                Texture2D before = (Texture2D)method.Invoke(world, new object[] { level });
                Check(before.GetPixel(4, 4).a == 0f && level.Footprint.Cells.Count == 64, "Preview fixture must begin empty.");
                typeof(LevelEditorWindow).GetField("layout", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(editor, level);
                level.Put(new Vector2Int(2, 2), brush, null);
                Invoke(editor, "Changed");
                Check(before == null, "Editing did not immediately discard the world preview cache.");
                Texture2D after = (Texture2D)method.Invoke(world, new object[] { level });
                Check(after.GetPixel(4, 4).r > 0.9f && after.GetPixel(4, 4).a > 0.9f && map.Modules[0].Bounds.size == Vector2Int.one && map.Modules[1].Bounds.size == Vector2Int.one,
                    "Unsaved edits did not update terrain and shared footprints.");
                Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(level, "Erase for preview");
                level.Erase(new Vector2Int(2, 2), brush.LayerId); Invoke(editor, "Changed"); Undo.FlushUndoRecordObjects();
                Check(after == null && ((Texture2D)method.Invoke(world, new object[] { level })).GetPixel(4, 4).a == 0f, "Erasing retained a stale preview.");
                Undo.PerformUndo();
                Check(((Texture2D)method.Invoke(world, new object[] { level })).GetPixel(4, 4).a > 0.9f, "Undo did not update the map preview.");
                // Inspector edits are detected by the update hook without changing window focus.
                Invoke(world, "OnInspectorUpdate");
                level.Size = new Vector2Int(10, 8); EditorUtility.SetDirty(level);
                Invoke(world, "OnInspectorUpdate");
                Texture2D resized = (Texture2D)method.Invoke(world, new object[] { level });
                Check(resized.width == 20, "Inspector size change retained the old preview dimensions.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(world); UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(level); UnityEngine.Object.DestroyImmediate(map); UnityEngine.Object.DestroyImmediate(brush);
            }
        }

    }
}
