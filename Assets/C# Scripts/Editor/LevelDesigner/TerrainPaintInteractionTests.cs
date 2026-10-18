using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TapTap.Editor
{
    // Exercises the actual SceneView input path, including picking dynamically created model meshes.
    public static class TerrainPaintInteractionTests
    {
        private static TerrainPaintWindow window;
        private static SceneView view;
        private static LevelRegion region;
        private static TerrainTileVisual[] tiles;
        private static TerrainPaintProfile profile;
        private static Vector2 from, to;
        private static bool ready;
        private static double deadline, earliest;
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../TerrainPaintValidation/interaction-results.txt"));
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Set(string field, object value) => typeof(TerrainPaintWindow).GetField(field, Flags).SetValue(window, value);

        public static void RunBatch()
        {
            if (!Application.isBatchMode || Directory.Exists(Path.Combine(Application.dataPath, "../.git")))
                throw new InvalidOperationException("Use an isolated graphics batch project.");
            File.WriteAllText(Output, "");
            try
            {
                TerrainPaintRegressionTests.ValidateExistingScene();
                EditorSceneManager.OpenScene("Assets/TerrainPaintRegression/Fixture.unity");
                region = UnityEngine.Object.FindObjectsOfType<LevelRegion>().Single(item => item.WorldModuleId == "module-a");
                profile = region.Source.TerrainPaint;
                tiles = Enumerable.Range(2, 3).Select(x => region.GetComponentsInChildren<TerrainTileVisual>().Single(tile =>
                    tile.PlacementId == region.Source.Find(new Vector2Int(x, 0), region.Source.Style.TerrainBrush.LayerId).Id)).ToArray();
                foreach (TerrainTileVisual tile in tiles) profile.SetPaint(region.WorldModuleId, tile.PlacementId, null);
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile); TerrainPaintSceneIndex.RefreshLoadedPaint();
                window = EditorWindow.GetWindow<TerrainPaintWindow>(); window.position = new Rect(40, 50, 320, 650); window.Show();
                Set("currentRegion", region); Set("paintingEnabled", true); Set("crossRegions", false); Set("brushSize", 1); Set("variantIndex", 1);
                view = EditorWindow.GetWindow<SceneView>(); view.position = new Rect(370, 50, 1100, 750); view.Show();
                view.in2DMode = false; view.LookAt(new Vector3(4, 0.75f, 0.5f), Quaternion.identity, 3.2f, true, true); view.Focus();
                SceneView.duringSceneGui += Probe;
                deadline = EditorApplication.timeSinceStartup + 45; earliest = EditorApplication.timeSinceStartup + 2;
                EditorApplication.update += Tick; view.Repaint();
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static void Probe(SceneView sceneView)
        {
            if (sceneView != view || Event.current.type != EventType.Repaint) return;
            from = HandleUtility.WorldToGUIPoint(tiles[0].transform.position);
            to = HandleUtility.WorldToGUIPoint(tiles[2].transform.position);
            GameObject picked = HandleUtility.PickGameObject(from, false);
            ready = picked != null && picked.GetComponentInParent<TerrainTileVisual>() == tiles[0];
        }

        private static void Stroke(Vector2 a, Vector2 b)
        {
            view.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = a });
            view.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = b, delta = b - a });
            view.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = b });
        }

        private static void Tick()
        {
            try
            {
                Check(EditorApplication.timeSinceStartup < deadline, "SceneView picking/input did not become available.");
                if (!ready || EditorApplication.timeSinceStartup < earliest) { view.Repaint(); return; }
                SceneView.duringSceneGui -= Probe; EditorApplication.update -= Tick;
                Stroke(from, to);
                Check(tiles.All(tile => tile.PaintMaterial == profile.Variants[1]), "Actual mouse drag did not paint all crossed cells.");
                Undo.PerformUndo(); TerrainPaintSceneIndex.RefreshLoadedPaint();
                Check(tiles.All(tile => tile.PaintMaterial == null), "Actual drag did not form one undo operation.");
                Undo.PerformRedo(); TerrainPaintSceneIndex.RefreshLoadedPaint();
                Set("tool", Enum.ToObject(typeof(TerrainPaintWindow).GetField("tool", Flags).FieldType, 3));
                Stroke(from, from);
                Check((int)typeof(TerrainPaintWindow).GetField("variantIndex", Flags).GetValue(window) == 1, "Eyedropper did not sample the actual scene material.");
                Set("tool", Enum.ToObject(typeof(TerrainPaintWindow).GetField("tool", Flags).FieldType, 4));
                Stroke(from, from); Check(tiles[0].PaintMaterial == null, "Scene eraser did not restore the source material.");
                Set("tool", Enum.ToObject(typeof(TerrainPaintWindow).GetField("tool", Flags).FieldType, 1)); Set("variantIndex", 2);
                Stroke(from, to); Check(tiles.All(tile => tile.PaintMaterial == profile.Variants[2]), "Scene rectangle did not apply paint on mouse release.");
                File.WriteAllText(Output, "PASS: Actual SceneView mesh picking, continuous mouse drag, whole-stroke undo/redo, eyedropper, eraser and rectangle fill\n");
                Finish(null);
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static void Finish(Exception exception)
        {
            SceneView.duringSceneGui -= Probe; EditorApplication.update -= Tick;
            if (exception != null) { File.AppendAllText(Output, "FAIL: " + exception + "\n"); Debug.LogException(exception); }
            EditorApplication.Exit(exception == null ? 0 : 1);
        }
    }
}
