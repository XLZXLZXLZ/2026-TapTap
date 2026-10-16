using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Run only in an isolated project: -batchmode -executeMethod TapTap.Editor.SmartTileRegressionTests.RunBatch.
    public static class SmartTileRegressionTests
    {
        private const string Folder = "Assets/SmartTileRegression";
        private const string Pending = "TapTap.SmartTiles.Pending";
        private static string Results => Path.Combine(Application.dataPath, "../smart-tile-results.txt");
        private static IEnumerator runtimeTests;
        private static double deadline;
        private static LevelPalette palette;
        private static LevelBrush piston, thin, square;

        [InitializeOnLoadMethod]
        private static void Subscribe()
        {
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            EditorApplication.playModeStateChanged += PlayModeChanged;
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project.");
            File.WriteAllText(Results, "");
            try
            {
                LevelPrefabBuilder.EnsureFolder(Folder);
                palette = LevelEditorAssets.EnsureDefaults();
                piston = palette.Brushes.Find(brush => brush != null && brush.HandlerId == "phase-piston");
                thin = AssetDatabase.LoadAssetAtPath<LevelBrush>("Assets/Runtime/LevelDesigner/Brushes/Platform.asset");
                square = AssetDatabase.LoadAssetAtPath<LevelBrush>("Assets/Runtime/LevelDesigner/Brushes/SquarePlatform.asset");
                Check(piston != null && thin.HandlerId == "smart-platform" && square.HandlerId == "smart-platform", "New brushes missing.");
                Check(palette.Brushes.FindAll(brush => brush != null && brush.HandlerId == "phase-block").Count == 2, "Legacy phase brushes changed.");
                int count = palette.Brushes.Count;
                SmartTileAssets.EnsureInPalette(palette);
                Check(palette.Brushes.Count == count, "Palette setup duplicated brushes.");
                Pass("New piston and smart platform assets coexist with both legacy phase brushes; setup is idempotent");
                OccupancyAndEditing();
                EditorOperations();
                PlatformVariants();
                SerializationAndGeneration();
                Overlaps();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Folder + "/Runtime.unity");
                SessionState.SetBool(Pending, true);
                Subscribe();
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static LevelDefinition Level()
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.Size = new Vector2Int(16, 12); level.Palette = palette;
            return level;
        }

        private static void OccupancyAndEditing()
        {
            foreach (int turns in new[] { 0, 1, 2, 3 })
            {
                var level = Level();
                var settings = new PistonPlacementSettings { QuarterTurns = turns, MaxLength = 4 };
                LevelPlacement placed = level.Put(new Vector2Int(6, 6), piston, settings);
                for (int i = 0; i < 4; i++)
                {
                    Vector2Int cell = placed.Cell + settings.Direction * i;
                    Check(level.Find(cell, piston.LayerId) == placed && level.Footprint.Contains(cell), "Tail missing from footprint or lookup.");
                }
                Vector2Int tip = placed.Cell + settings.Direction * 3;
                Check(LevelGridEditing.MoveArea(level, new RectInt(tip, Vector2Int.one), Vector2Int.up, _ => true), "Moving by tail failed.");
                Check(placed.Cell == new Vector2Int(6, 7), "Partial selection split piston.");
                tip += Vector2Int.up;
                Check(LevelGridEditing.Apply(level, new[] { tip }, null, null, piston.LayerId, true, false,
                    () => Undo.RegisterCompleteObjectUndo(level, "Erase piston")), "Erase by tail failed.");
                Check(level.Placements.Count == 0, "Erase left orphaned piston cells.");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check(level.Placements.Count == 1 && ((PistonPlacementSettings)level.Placements[0].Settings).MaxLength == 4, "Undo lost settings.");
                Vector2Int original = level.Placements[0].Cell;
                LevelGridEditing.Mirror(level, true); LevelGridEditing.Mirror(level, true);
                Check(level.Placements[0].Cell == original && ((PistonPlacementSettings)level.Placements[0].Settings).QuarterTurns == turns, "Mirror failed to round trip.");
                Check(!LevelGridEditing.MoveArea(level, LevelPlacementGeometry.Bounds(level.Placements[0]), new Vector2Int(100, 0), _ => true), "Out-of-bounds move accepted.");
                Object.DestroyImmediate(level);
            }
            var supported = Level();
            var terrain = palette.Brushes.Find(brush => brush != null && brush.DisplayName == "地形");
            supported.Put(new Vector2Int(2, 2), terrain, null);
            Check(LevelGridEditing.Apply(supported, new[] { new Vector2Int(3, 2) }, piston,
                new PistonPlacementSettings { MaxLength = 3 }, piston.LayerId, false, false), "Base cannot attach to a fixed block.");
            Check(supported.Find(new Vector2Int(2, 2), "geometry") != null, "Base overwrote supporting terrain.");
            Check(!LevelGridEditing.Apply(supported, new[] { new Vector2Int(4, 2) }, terrain, null, "geometry", false, false), "Terrain overlapped piston body.");
            Check(!LevelGridEditing.Apply(supported, new[] { new Vector2Int(15, 2) }, piston,
                new PistonPlacementSettings { MaxLength = 2 }, piston.LayerId, false, false), "Only start was checked for canvas overflow.");
            Object.DestroyImmediate(supported);
            Pass("All four directions support whole-object lookup, footprint, tail selection/move/erase, undo, mirror, support attachment and overflow checks");
        }

        private static void PlatformVariants()
        {
            var level = Level();
            for (int x = 1; x <= 3; x++) level.Put(new Vector2Int(x, 2), thin, null);
            LevelPlacement single = level.Put(new Vector2Int(6, 2), thin, null);
            level.Put(new Vector2Int(7, 2), square, null);
            Check(PlatformLevelBrushHandler.Part(level, level.Placements[0]) == PlatformPart.Left, "Left end missing.");
            Check(PlatformLevelBrushHandler.Part(level, level.Placements[1]) == PlatformPart.Middle, "Middle missing.");
            Check(PlatformLevelBrushHandler.Part(level, level.Placements[2]) == PlatformPart.Right, "Right end missing.");
            Check(PlatformLevelBrushHandler.Part(level, single) == PlatformPart.Single, "Thin/square platform groups connected.");
            GameObject prefab = LevelPrefabBuilder.Save(level, Folder + "/Platforms.prefab");
            PlatformTileVisual[] visuals = prefab.GetComponentsInChildren<PlatformTileVisual>(true);
            Check(visuals.Length == 5, "Platform export lost cells.");
            foreach (PlatformTileVisual visual in visuals)
            {
                Check(visual.transform.Find("Platform Visual (" + visual.Part + ")") != null, "Selected model missing.");
                Check(visual.GetComponent<WorldSurface>().Kind == SurfaceKind.OneWay, "Visual changed one-way physics.");
                float height = visual.GetComponent<BoxCollider2D>().size.y;
                Check(Mathf.Approximately(height, visual.ConnectionGroup == "thin-platform" ? 0.25f : 1f), "Visual changed collision height.");
            }
            level.Erase(new Vector2Int(2, 2), "geometry");
            Check(PlatformLevelBrushHandler.Part(level, level.Placements[0]) == PlatformPart.Single, "Deleting neighbour did not refresh end selection.");
            LevelPrefabBuilder.Save(level, Folder + "/Platforms.prefab");
            Object.DestroyImmediate(level);
            Pass("All four platform model variants export correctly, refresh after erasure, preserve one-way collision, and separate thin/square groups");
        }

        private static void EditorOperations()
        {
            var level = Level();
            LevelPlacement original = level.Put(new Vector2Int(2, 2), piston,
                new PistonPlacementSettings { MaxLength = 3, ExtendedWhenActive = false });
            var editor = ScriptableObject.CreateInstance<LevelEditorWindow>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object Call(string name, params object[] args) => typeof(LevelEditorWindow).GetMethod(name, flags).Invoke(editor, args);
            void Set(string name, object value) => typeof(LevelEditorWindow).GetField(name, flags).SetValue(editor, value);
            try
            {
                Call("UseLayout", level);
                Call("SelectCell", new Vector2Int(4, 2));
                Check((LevelPlacement)Call("SelectedPlacement") == original, "Canvas cache cannot select piston by tip.");
                Set("hasAreaSelection", true); Set("selectionArea", new RectInt(4, 2, 1, 1));
                Check((bool)Call("CopyArea"), "Copying a partial piston selection failed.");
                Call("PasteArea", new Vector2Int(8, 2));
                Check(level.Placements.Count == 2, "Pasting created individual cells instead of one piston.");
                LevelPlacement pasted = level.Find(new Vector2Int(10, 2), piston.LayerId);
                Check(pasted != null && pasted != original && pasted.Settings != original.Settings &&
                    ((PistonPlacementSettings)pasted.Settings).MaxLength == 3 && !((PistonPlacementSettings)pasted.Settings).ExtendedWhenActive,
                    "Copy lost length/inversion or shared mutable settings.");
                Call("SelectCell", new Vector2Int(10, 2));
                Set("selectionArea", LevelPlacementGeometry.Bounds(pasted));
                FieldInfo tool = typeof(LevelEditorWindow).GetField("tool", flags);
                tool.SetValue(editor, Enum.Parse(tool.FieldType, "Select"));
                Call("RotateCurrentTarget");
                Check(((PistonPlacementSettings)pasted.Settings).QuarterTurns == 1, "Selected piston did not rotate.");
                Call("DeleteArea");
                Check(level.Placements.Count == 1 && level.Placements[0] == original, "Deleting selected piston touched the original.");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check(level.Placements.Count == 2, "Undo failed after canvas deletion.");
            }
            finally { Object.DestroyImmediate(editor); Object.DestroyImmediate(level); }
            Pass("Actual editor cache selects by tip; partial selections copy/paste complete independent pistons; rotation, deletion and undo preserve settings");
        }

        private static void SerializationAndGeneration()
        {
            var level = Level();
            level.Put(new Vector2Int(4, 6), piston, new PistonPlacementSettings { QuarterTurns = 1, MaxLength = 4, ExtendedWhenActive = false });
            AssetDatabase.CreateAsset(level, Folder + "/Piston.asset");
            AssetDatabase.SaveAssetIfDirty(level);
            GameObject prefab = LevelPrefabBuilder.Save(level, Folder + "/Piston.prefab");
            PhasePiston generated = prefab.GetComponentInChildren<PhasePiston>(true);
            Check(generated.MaxLength == 4 && generated.QuarterTurns == 1, "Generated piston settings lost.");
            BoxCollider2D box = generated.GetComponent<BoxCollider2D>();
            Check(box.size == new Vector2(1, 4) && box.offset == new Vector2(0, -1.5f), "Directional collision shape wrong.");
            Check(generated.transform.Find("Piston Visual/Base") != null && generated.transform.Find("Piston Visual/Tip") != null, "Visual assembly missing base or tip.");
            Check(generated.transform.Find("Piston Visual").childCount == (generated.GetComponent<PhasePiston>().UseThinCover ? 6 : 5),
                "Visual assembly has an incorrect number of body segments.");
            Check(generated.GetComponent<PhaseBlock>().SolidWhenActive == false, "Inverse switch relation lost.");
            AssetDatabase.ImportAsset(Folder + "/Piston.asset", ImportAssetOptions.ForceUpdate);
            level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Folder + "/Piston.asset");
            var settings = level.Placements[0].Settings as PistonPlacementSettings;
            Check(settings != null && settings.MaxLength == 4 && settings.QuarterTurns == 1 && !settings.ExtendedWhenActive, "Managed settings did not serialize.");
            var invalid = Level();
            invalid.Placements.Add(new LevelPlacement { Cell = new Vector2Int(15, 0), Brush = piston, Settings = new PistonPlacementSettings { MaxLength = 2 } });
            Check(LevelPrefabBuilder.Validate(invalid).Count > 0, "Builder accepted tail outside canvas.");
            Object.DestroyImmediate(invalid);
            Pass("Prefab and managed-reference serialization preserve direction, integer length, inversion and separate base/body/tip models; builder validates full extent");
        }

        private static void Overlaps()
        {
            var a = Level(); var b = Level();
            a.Put(new Vector2Int(2, 2), piston, new PistonPlacementSettings { MaxLength = 3 });
            b.Put(new Vector2Int(2, 2), piston, new PistonPlacementSettings { MaxLength = 3 });
            Check(!WorldMapOverlap.HasConflict(a, a.Footprint, Vector2Int.zero, b, b.Footprint, Vector2Int.zero), "Identical pistons conflict.");
            ((PistonPlacementSettings)b.Placements[0].Settings).ExtendedWhenActive = false;
            b.InvalidateFootprint();
            Check(WorldMapOverlap.ConflictCells(a, a.Footprint, Vector2Int.zero, b, b.Footprint, Vector2Int.zero).Contains(new Vector2Int(4, 2)), "World overlap ignored piston tail.");
            Object.DestroyImmediate(a); Object.DestroyImmediate(b);
            Pass("World map overlap compares complete piston bodies, including off-anchor tip cells");
        }

        private static void PlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            runtimeTests = Runtime(); deadline = EditorApplication.timeSinceStartup + 30;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Runtime test timed out.");
                if (runtimeTests.MoveNext()) return;
                EditorApplication.update -= Tick;
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { EditorApplication.update -= Tick; Fail(exception); }
        }

        private static IEnumerator Runtime()
        {
            WorldPhaseState state = WorldPhaseState.Instance;
            state.RestoreState(false);
            var instances = new List<PhasePiston>();
            for (int turns = 0; turns < 4; turns++)
            {
                GameObject root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<LevelBrush>(SmartTileAssets.PistonBrushPath).Prefab);
                root.transform.position = new Vector3(100 + turns * 10, 100);
                PhasePiston view = root.GetComponent<PhasePiston>();
                view.Configure(turns, turns == 0 ? 1 : 4);
                Check(!root.GetComponent<BoxCollider2D>().enabled && view.VisualExtension == 0, "Initial retracted state incorrect.");
                Vector2 direction = new PistonPlacementSettings { QuarterTurns = turns }.Direction;
                Bounds bounds = root.GetComponent<PhaseBlock>().WorldBounds;
                Check(Vector2.Distance(bounds.center, (Vector2)root.transform.position + direction * ((view.MaxLength - 1) * 0.5f)) < 0.001f,
                    "Collision bounds centre disagrees with extension direction.");
                Check((Vector2)bounds.size == new Vector2(direction.x != 0 ? view.MaxLength : 1, direction.y != 0 ? view.MaxLength : 1),
                    "Collision bounds size disagrees with integer length.");
                instances.Add(view);
            }
            state.SetActive(true);
            foreach (PhasePiston view in instances)
                Check(view.GetComponent<BoxCollider2D>().enabled && view.VisualExtension == 0, "Collision waited for visual extension.");
            float until = Time.time + 0.35f;
            while (Time.time < until) yield return null;
            foreach (PhasePiston view in instances) Check(view.VisualExtension > 0.999f, "Animation did not reach full extension.");
            state.SetActive(false);
            foreach (PhasePiston view in instances)
                Check(!view.GetComponent<BoxCollider2D>().enabled && view.VisualExtension > 0.999f, "Retraction collision waited for animation.");
            until = Time.time + 0.05f;
            while (Time.time < until) yield return null;
            float partial = instances[0].VisualExtension;
            state.SetActive(true);
            Check(Mathf.Approximately(partial, instances[0].VisualExtension), "Rapid toggle snapped the animation.");
            state.RestoreState(false);
            foreach (PhasePiston view in instances)
                Check(view.VisualExtension == 0 && !view.GetComponent<BoxCollider2D>().enabled, "Rewind left stale model or collision.");
            state.RestoreState(true);
            foreach (PhasePiston view in instances)
                Check(view.VisualExtension == 1 && view.GetComponent<BoxCollider2D>().enabled, "Restored extension did not snap.");
            foreach (PhasePiston view in instances) Object.DestroyImmediate(view.gameObject);
            GameObject exported = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Piston.prefab"));
            PhasePiston inverse = exported.GetComponentInChildren<PhasePiston>(true);
            state.RestoreState(false);
            Check(inverse.MaxLength == 4 && inverse.VisualExtension == 1 && inverse.GetComponent<BoxCollider2D>().enabled,
                "Exported inverse piston failed to restore its configured state.");
            Transform exportedTip = inverse.transform.Find("Piston Visual/Tip");
            float expectedTip = inverse.UseThinCover ? 4f - inverse.CoverThickness : 3.5f;
            Check(exportedTip != null && exportedTip.gameObject.activeInHierarchy && Mathf.Approximately(exportedTip.localPosition.x, expectedTip),
                "Exported prefab lost generated model references.");
            state.SetActive(true);
            Check(!inverse.GetComponent<BoxCollider2D>().enabled && inverse.VisualExtension == 1,
                "Inverse piston did not switch collision before animation.");
            state.RestoreState(true);
            Check(exportedTip.gameObject.activeInHierarchy == inverse.UseThinCover, "Restored exported piston has an incorrect resting cover state.");
            if (inverse.UseThinCover)
                Check(Mathf.Approximately(exportedTip.localPosition.x, 0f), "Retracted cover did not return to the base.");
            Object.DestroyImmediate(exported);
            GameObject legacy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GameplayVisualAssets.PhasePrefabPath));
            state.RestoreState(false);
            Check(!legacy.GetComponent<BoxCollider2D>().enabled, "Legacy ghost collision changed.");
            state.SetActive(true);
            Check(legacy.GetComponent<BoxCollider2D>().enabled, "Legacy phase block no longer toggles.");
            Object.DestroyImmediate(legacy);
            Pass("Live play mode: directional bounds, one-cell and exported inverse pistons switch collisions immediately, animate independently, reverse continuously and snap on rewind; generated model references and legacy phase blocks still work");
        }

        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Pass(string message) { File.AppendAllText(Results, "PASS: " + message + "\n"); Debug.Log("SMART TILE PASS: " + message); }
        private static void Fail(Exception exception)
        {
            File.AppendAllText(Results, "FAIL: " + exception + "\n");
            Debug.LogException(exception); EditorApplication.Exit(1);
        }
    }
}
