using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    // Only run in an isolated project: -batchmode -executeMethod TapTap.Editor.WorldFlowRegressionTests.RunBatch
    [InitializeOnLoad]
    public static class WorldFlowRegressionTests
    {
        private const string Key = "TapTap.WorldFlowRegression";
        private const string Folder = "Assets/WorldFlowRegression";
        private static int stage;
        private static int sceneHandle;
        private static LevelRegion firstRegion;
        private static bool capturedPreview;
        private static double deadline;
        private static readonly List<string> results = new List<string>();

        static WorldFlowRegressionTests()
        {
            if (SessionState.GetBool(Key, false))
            {
                deadline = EditorApplication.timeSinceStartup + 90d;
                EditorApplication.update += Tick;
            }
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
            try
            {
                TestContours();
                if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
                LevelPrefabBuilder.EnsureFolder(Folder);
                LevelPalette palette = AssetDatabase.LoadAssetAtPath<LevelPalette>(LevelEditorAssets.DefaultPalettePath);
                LevelBrush terrain = palette.Brushes.Find(brush => brush.HandlerId == "terrain");
                if (terrain == null) terrain = palette.Brushes.Find(brush => brush.Prefab.GetComponent<WorldSurface>() != null && brush.HandlerId != "spring");
                LevelBrush entryBrush = palette.Brushes.Find(brush => brush.HandlerId == "checkpoint");
                LevelBrush endBrush = LevelEditorAssets.EnsureEndpointBrush();
                LevelBrush springBrush = LevelEditorAssets.EnsureSpringBrush();
                var map = ScriptableObject.CreateInstance<WorldMapDefinition>();
                AssetDatabase.CreateAsset(map, Folder + "/Flow.asset");
                for (int i = 0; i < 3; i++)
                {
                    var level = ScriptableObject.CreateInstance<LevelDefinition>();
                    level.Size = new Vector2Int(8, 6); level.Palette = palette;
                    level.UseCustomBoundary = true;
                    foreach (Vector2Int cell in new RectInt(0, 0, 8, 6).allPositionsWithin) level.BoundaryCells.Add(cell);
                    for (int x = 0; x < 8; x++) level.Put(new Vector2Int(x, 0), terrain, new EmptyPlacementSettings());
                    LevelPlacement entry = level.Put(new Vector2Int(1, 1), entryBrush, new CheckpointPlacementSettings());
                    level.EntryPlacementId = entry.Id;
                    level.Put(new Vector2Int(6, 1), endBrush, new EndpointPlacementSettings());
                    level.Put(new Vector2Int(4, 0), springBrush, new SpringPlacementSettings());
                    AssetDatabase.CreateAsset(level, Folder + "/Room" + i + ".asset");
                    GameObject prefab = LevelPrefabBuilder.Save(level, Folder + "/Room" + i + ".prefab");
                    GameObject contents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));
                    var custom = new GameObject("State Probe");
                    custom.transform.SetParent(contents.transform.Find("Custom"), false);
                    custom.transform.localPosition = new Vector3(2f, 3f, 0f);
                    PrefabUtility.SaveAsPrefabAsset(contents, AssetDatabase.GetAssetPath(prefab));
                    PrefabUtility.UnloadPrefabContents(contents);
                    map.Modules.Add(new WorldMapModule { Level = level, Position = new Vector2Int(120 + i * 12, 100),
                        Sequence = new[] { 30, 10, 20 }[i] });
                }
                Check(map.OrderedModules()[0].Sequence == 10, "Sequence does not override list order.");
                var legacy = new WorldMapModule { Level = map.Modules[0].Level };
                map.Modules.Add(legacy);
                Check(map.SequenceNumber(legacy) == 4, "Legacy sequence migration failed.");
                map.Modules.Remove(legacy);
                map.Modules[1].Sequence = 30;
                Check(WorldMapPrefabBuilder.Validate(map).Exists(error => error.Contains("序号")), "Duplicate sequence accepted.");
                map.Modules[1].Sequence = 10;
                var before = map.Modules[0].Level.Outline;
                LevelOutlineBuilder.Save(map.Modules[0].Level);
                Check(before == map.Modules[0].Level.Outline, "Outline regeneration replaced the asset.");
                string path = WorldMapSceneBuilder.Generate(map, false);
                map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(Folder + "/Flow.asset");
                Check(WorldMapSceneBuilder.Generate(map, false) == path, "Regeneration changed the linked scene path.");
                map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(Folder + "/Flow.asset");
                WorldMapProgressStore.Clear(map);
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Check(UnityEngine.Object.FindObjectsOfType<WorldMapRoot>().Length == 1
                    && UnityEngine.Object.FindObjectsOfType<LevelRegion>().Length == 3, "World must be in one scene.");
                WorldMapProgression flow = UnityEngine.Object.FindObjectOfType<WorldMapProgression>();
                flow.FadeDuration = 0.05f; flow.AssemblyDuration = 0.5f; flow.AssemblyHold = 0.05f;
                EditorSceneManager.SaveScene(scene);
                Pass("Contours, legacy order, duplicate validation, stable outline assets and single-scene generation");
                File.WriteAllLines(ResultPath, results);
                SessionState.SetBool(Key, true);
                deadline = EditorApplication.timeSinceStartup + 90d;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static void TestContours()
        {
            foreach (var cells in new[]
            {
                new[] { Vector2Int.zero },
                new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.up },
                new[] { Vector2Int.zero, Vector2Int.one },
                new[] { new Vector2Int(-2, -1), new Vector2Int(-1, -1), new Vector2Int(-2, 0) }
            })
            {
                var footprint = new LevelFootprint(cells);
                var outline = ScriptableObject.CreateInstance<LevelOutlineData>();
                outline.Build(footprint);
                Check(outline.Loops.Count == (cells.Length == 2 ? 2 : 1), "Wrong contour island count.");
                float area = 0f;
                foreach (LevelOutlineLoop loop in outline.Loops)
                    for (int i = 0; i < loop.Points.Length; i++)
                    {
                        Vector2 a = loop.Points[i], b = loop.Points[(i + 1) % loop.Points.Length];
                        area += (a.x * b.y - b.x * a.y) * 0.5f;
                    }
                Check(Mathf.Approximately(area, cells.Length), "Contour changed the footprint area.");
                UnityEngine.Object.DestroyImmediate(outline);
            }
            var ring = new List<Vector2Int>();
            foreach (Vector2Int cell in new RectInt(0, 0, 3, 3).allPositionsWithin)
                if (cell != Vector2Int.one) ring.Add(cell);
            var hole = ScriptableObject.CreateInstance<LevelOutlineData>();
            hole.Build(new LevelFootprint(ring));
            Check(hole.Loops.Count == 2, "Interior cutout was joined to the outer contour.");
            UnityEngine.Object.DestroyImmediate(hole);
        }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Play verification timed out at " + stage);
                if (!EditorApplication.isPlaying) return;
                WorldMapProgression flow = WorldMapProgression.Current;
                if (flow == null || flow.CurrentModule == null) return;
                PlayerController player = UnityEngine.Object.FindObjectOfType<PlayerController>();
                if (flow.IsTransitioning)
                {
                    Check(!RewindManager.Current.BeginRewind(), "Rewind was allowed during a transition.");
                    var view = flow.GetComponent<WorldMapAssemblyView>();
                    var texture = (RenderTexture)typeof(WorldMapAssemblyView).GetField("texture",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(view);
                    if (texture != null && !capturedPreview)
                    {
                        SavePreview(texture, "world-flow-preview.png");
                        capturedPreview = true;
                    }
                    return;
                }
                switch (stage)
                {
                    case 0:
                        Check(flow.CurrentNumber == 10 && flow.LevelCount == 3, "Wrong first level.");
                        sceneHandle = SceneManager.GetActiveScene().handle;
                        firstRegion = flow.CurrentRegion;
                        Check(UnityEngine.Object.FindObjectsOfType<LevelRegion>().Length == 1, "Neighbors are visible/active.");
                        firstRegion.transform.Find("Custom/State Probe").localPosition = Vector3.one * 90f;
                        firstRegion.GetComponentInChildren<SpringPad>().BounceHeight = 99f;
                        WorldPhaseState.Instance.SetActive(true);
                        TouchEndpoint(player, flow);
                        stage = 1;
                        break;
                    case 1:
                        if (flow.CurrentNumber != 20) return;
                        Check(SceneManager.GetActiveScene().handle == sceneHandle, "Scene was reloaded.");
                        Check(firstRegion == null && flow.CompletedCount == 1, "Old room was not refreshed/completion lost.");
                        Check(!WorldPhaseState.Instance.Active, "Global mechanism state leaked.");
                        Check(flow.CurrentRegion.GetComponentInChildren<SpringPad>().BounceHeight == 4.5f, "Spring state leaked.");
                        Check(flow.CurrentRegion.transform.Find("Custom/State Probe").localPosition == new Vector3(2, 3, 0), "Custom state leaked.");
                        Check(player.Phase == PlayerPhase.Joined && Vector2.Distance(player.GetComponent<RespawnService>().Checkpoint,
                            flow.CurrentRegion.EntrySpawnPosition) < 0.01f, "Player/respawn did not move to entry.");
                        Check(UnityEngine.Object.FindObjectOfType<DemoCamera>().Region == flow.CurrentRegion, "Camera still follows the old room.");
                        Check(Vector2.Distance(player.Body.Motor.Position, flow.CurrentRegion.EntrySpawnPosition) < 1f, "Player was not teleported.");
                        Check(UnityEngine.Object.FindObjectsOfType<LevelRegion>().Length == 1, "Refreshed neighbors are active.");
                        Pass("Endpoint advances, resets mechanisms/Custom/player and respawn, hides neighbors, preserves scene identity");
                        Check(flow.SkipNext(), "Skip failed."); stage = 2;
                        break;
                    case 2:
                        Check(flow.CurrentNumber == 30 && flow.CompletedCount == 1, "Skip falsely completed a room.");
                        Check(!flow.GoToSequence(999) && flow.GoToSequence(10), "Explicit sequence jump failed."); stage = 3;
                        break;
                    case 3:
                        Check(flow.CurrentNumber == 10 && SceneResetManager.Instance.ResetCurrentScene(), "Restart failed."); stage = 4;
                        break;
                    case 4:
                        Check(flow.CurrentNumber == 10 && flow.CompletedCount == 1, "Restart lost progression.");
                        Check(flow.GoToSequence(30), "Jump to final room failed."); stage = 5;
                        break;
                    case 5:
                        Check(flow.CurrentNumber == 30, "Wrong final room."); TouchEndpoint(player, flow); stage = 6;
                        break;
                    case 6:
                        if (!flow.IsComplete) return;
                        Check(flow.CompletedCount == 2 && WorldMapProgression.InputBlocked && Time.timeScale > 0f,
                            "Final completion or pause cleanup failed.");
                        Check(capturedPreview, "Assembly never rendered.");
                        var completeTexture = (RenderTexture)typeof(WorldMapAssemblyView).GetField("texture",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                            .GetValue(flow.GetComponent<WorldMapAssemblyView>());
                        SavePreview(completeTexture, "world-flow-complete.png");
                        Check(flow.GoToSequence(10), "Cannot replay after completion."); stage = 7;
                        break;
                    case 7:
                        Check(!flow.IsComplete && flow.CurrentNumber == 10 && flow.CompletedCount == 2, "Replay lost progress.");
                        Pass("Skip and explicit jumps, restart retains progress, final completion and replay, rendered assembly preview");
                        SessionState.SetBool(Key, false);
                        EditorApplication.update -= Tick;
                        File.AppendAllLines(ResultPath, results);
                        EditorApplication.Exit(0);
                        break;
                }
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static void TouchEndpoint(PlayerController player, WorldMapProgression flow)
        {
            LevelEndpoint endpoint = flow.CurrentRegion.GetComponentInChildren<LevelEndpoint>();
            player.TeleportJoined((Vector2)endpoint.transform.position + Vector2.up * 0.5f);
            Physics2D.SyncTransforms();
        }
        private static void SavePreview(RenderTexture texture, string name)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Application.dataPath, "../" + name), image.EncodeToPNG());
            UnityEngine.Object.Destroy(image); RenderTexture.active = previous;
        }
        private static string ResultPath => Path.Combine(Application.dataPath, "../world-flow-regression-results.txt");
        private static void Pass(string message) { results.Add("PASS: " + message); Debug.Log("WORLD FLOW PASS: " + message); }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Fail(Exception exception)
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            File.AppendAllText(ResultPath, "FAIL: " + exception + "\n");
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }
}
