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
    // Run in an isolated project: -batchmode -executeMethod TapTap.Editor.CameraRegressionTests.RunBatch.
    public static class CameraRegressionTests
    {
        private static readonly List<string> results = new List<string>();
        private static readonly MethodInfo Tick = typeof(DemoCamera).GetMethod("UpdateFollow", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project.");
            int exit = 0;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Test("Automatic sizing centers small maps and follows above the twelve-unit cap", DefaultViewSize);
                Test("Small movements remain in the dead zone; large movements follow smoothly", DeadZone);
                Test("The viewport is clamped on all edges of an offset map", Edges);
                Test("Long horizontal and vertical rooms center only their smaller axis", Corridors);
                Test("Changing the aspect ratio reclamps the whole viewport", AspectRatio);
                Test("Per-level size, offset and independent axes are honored", Settings);
                Test("Scaled levels and authored camera child offsets remain correct", ScaledHierarchy);
                Test("Rewind follows immediately and clears forward smoothing velocity", Rewind);
                Test("Pausing and resuming keep the camera finite and responsive", Pause);
                Test("Respawn motion catches up faster than ordinary movement", Return);
                Test("Irregular cutouts remain visible inside the enclosing rectangle", Irregular);
                Test("Old test scenes retain region overrides and follow without regeneration", ExistingScenes);
                Test("Newly generated scenes bind the player and use the level view size", GeneratedScene);
            }
            catch (Exception exception) { results.Add("FAIL: " + exception); Debug.LogException(exception); exit = 1; }
            File.WriteAllLines(Path.Combine(Application.dataPath, "../camera-regression-results.txt"), results);
            EditorApplication.Exit(exit);
        }

        private static void Test(string name, Action run) { run(); results.Add("PASS: " + name); Debug.Log("CAMERA PASS: " + name); }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < 0.01f, message + ": " + actual + " != " + expected);

        private sealed class Fixture : IDisposable
        {
            private readonly GameObject root = new GameObject("Camera regression");
            public readonly PlayerController Player;
            public readonly MovableEntity Body;
            public readonly LevelRegion Region;
            public readonly DemoCamera Rig;
            public readonly Camera View;

            public Fixture(int width = 100, int height = 100)
            {
                GameObject character = Child("Player", root.transform);
                Player = character.AddComponent<PlayerController>();
                Body = Child("Body", character.transform).AddComponent<MovableEntity>();
                typeof(PlayerController).GetField("body", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Player, Body);
                Region = Child("Region", root.transform).AddComponent<LevelRegion>();
                Region.Size = new Vector2Int(width, height);
                Region.CameraSettings.AutoSize = false;
                Region.CameraSettings.ViewSize = 6f; // Fixed viewport for the geometry scenarios below.
                GameObject rig = Child("Rig", root.transform);
                Transform shake = Child("Shake", rig.transform).transform;
                View = Child("Camera", shake).AddComponent<Camera>();
                View.enabled = false;
                View.aspect = 16f / 9f;
                Rig = rig.AddComponent<DemoCamera>();
                Rig.Configure(Player, View, shake);
                Position(30f, 30f);
                Rig.SetRegion(Region);
            }

            private static GameObject Child(string name, Transform parent)
            {
                var child = new GameObject(name);
                child.transform.SetParent(parent, false);
                return child;
            }
            public void Position(float x, float y) => Body.transform.position = new Vector3(x, y, 0f);
            public void Step(int count = 1, bool rewind = false)
            {
                for (int i = 0; i < count; i++) Tick.Invoke(Rig, new object[] { 1f / 60f, rewind });
            }
            public void Dispose() => UnityEngine.Object.DestroyImmediate(root);
        }

        private static void DefaultViewSize()
        {
            Check(new LevelCameraSettings().AutoSize, "Automatic sizing must be the default");
            foreach (var size in new[] { new Vector2Int(20,10), new Vector2Int(10,20), new Vector2Int(24,12) })
            using (var f = new Fixture(size.x,size.y))
            {
                f.Region.CameraSettings.AutoSize = true;
                f.Rig.SnapToPlayer();
                Near(f.View.orthographicSize, Mathf.Max(size.x,size.y) * 0.5f, "Small map view size");
                f.Position(100f,100f); f.Step(120);
                Near(f.View.transform.position.x,size.x * 0.5f,"Small map horizontal center");
                Near(f.View.transform.position.y,size.y * 0.5f,"Small map vertical center");
            }
            using (var f = new Fixture(100,60))
            {
                f.Region.CameraSettings.AutoSize = true;
                f.Rig.SnapToPlayer();
                Near(f.View.orthographicSize,12f,"Large map cap");
                float start = f.View.transform.position.x;
                f.Position(70f,40f); f.Step(120);
                Check(f.View.transform.position.x > start + 10f,"Large map must follow");
                f.Region.UnitSize = 0.5f;
                f.Region.transform.localScale = Vector3.one * 3f;
                f.Rig.SnapToPlayer();
                Near(f.View.orthographicSize,18f,"Auto cap scales with map units");
                f.Region.Size = new Vector2Int(20,10);
                f.Step();
                Near(f.View.orthographicSize,15f,"Shrinking map updates automatic size");
                Near(f.View.transform.position.x,15f,"Shrinking map centers immediately");
            }
            var source = AssetDatabase.LoadAssetAtPath<LevelDefinition>("Assets/Runtime/Levels/Level_New_15.asset");
            Check(source != null, "Current level source was not found");
            Check(source.CameraSettings.AutoSize,"Existing level must use automatic sizing after import");
            using (var f = new Fixture())
            {
                // The fixture's fallback is six; the level source must take priority.
                f.Region.Source = source;
                f.Rig.SetRegion(f.Region);
                float expected = Mathf.Min(Mathf.Max(f.Region.WorldBounds.size.x,f.Region.WorldBounds.size.y) * 0.5f,12f);
                Near(f.View.orthographicSize, expected, "Region binding uses automatic size");
                f.View.orthographicSize = 6f;
                f.Step();
                Near(f.View.orthographicSize, expected, "Following keeps automatic size");
            }
        }

        private static void DeadZone()
        {
            using (var f = new Fixture())
            {
                Vector3 start = f.View.transform.position;
                f.Position(30.5f, 30.5f);
                f.Step(90);
                Near(f.View.transform.position.x, start.x, "Small horizontal motion");
                Near(f.View.transform.position.y, start.y, "Ordinary jump motion");
                f.Position(55f, 50f);
                f.Step();
                Check(f.View.transform.position.x > start.x && f.View.transform.position.x < 45f, "Follow must be smooth");
                f.Step(240);
                Near(f.View.transform.position.x, 55f - 12f * f.View.aspect * 0.1f, "Horizontal dead-zone boundary");
                Near(f.View.transform.position.y, 51.5f - 12f * 0.15f, "Vertical dead-zone boundary");
            }
        }

        private static void Edges()
        {
            using (var f = new Fixture(80, 60))
            {
                f.Region.transform.position = new Vector3(-100f, -70f, 4f);
                foreach (Vector2 corner in new[] { new Vector2(-200,-200), new Vector2(100,100), new Vector2(-200,100), new Vector2(100,-200) })
                {
                    f.Position(corner.x, corner.y);
                    f.Rig.SnapToPlayer();
                    Bounds b = f.Region.WorldBounds;
                    Vector3 p = f.View.transform.position;
                    Check(p.x - 6f * f.View.aspect >= b.min.x - 0.01f && p.x + 6f * f.View.aspect <= b.max.x + 0.01f, "Horizontal edge leaked");
                    Check(p.y - 6f >= b.min.y - 0.01f && p.y + 6f <= b.max.y + 0.01f, "Vertical edge leaked");
                    Near(p.z, -6f, "Region depth");
                }
                f.Position(-60f,-40f);
                f.Step(240);
                Check(f.View.transform.position.y < 0f, "Old minimumY must not clamp maps below the origin");
            }
        }

        private static void Corridors()
        {
            using (var f = new Fixture(120, 4))
            {
                f.Position(30f,2f); f.Rig.SnapToPlayer();
                f.Position(80f,3f); f.Step(240);
                Near(f.View.transform.position.y,2f,"Horizontal corridor height");
                Check(f.View.transform.position.x > 70f,"Long horizontal map did not follow");
            }
            using (var f = new Fixture(8, 120))
            {
                f.Position(4f,20f); f.Rig.SnapToPlayer();
                f.Position(5f,90f); f.Step(240);
                Near(f.View.transform.position.x,4f,"Vertical corridor width");
                Check(f.View.transform.position.y > 80f,"Long vertical map did not follow");
            }
        }

        private static void AspectRatio()
        {
            using (var f = new Fixture(30, 50))
            {
                f.Position(100f,25f); f.Rig.SnapToPlayer();
                Near(f.View.transform.position.x,30f - 6f * f.View.aspect,"Widescreen edge");
                f.View.aspect = 3f; f.Step();
                Near(f.View.transform.position.x,15f,"Viewport wider than room");
                f.View.aspect = 1f; f.Position(25f,25f); f.Step(240);
                Check(f.View.transform.position.x > 20f && f.View.transform.position.x <= 24.01f,"Narrow viewport failed to resume following");
            }
        }

        private static void Settings()
        {
            using (var f = new Fixture())
            {
                f.Region.CameraSettings.ViewSize = 4f;
                f.Region.CameraSettings.Offset = new Vector2(2f,3f);
                f.Region.CameraSettings.FollowVertical = false;
                f.Position(20f,20f); f.Rig.SnapToPlayer();
                Near(f.View.orthographicSize,4f,"Per-level view size");
                Near(f.View.transform.position.x,22f,"Per-level offset");
                Near(f.View.transform.position.y,50f,"Locked vertical axis");
                f.Position(60f,80f); f.Step(240);
                Near(f.View.transform.position.y,50f,"Vertical axis moved");
            }
            var source = ScriptableObject.CreateInstance<LevelDefinition>();
            using (var f = new Fixture())
            {
                source.CameraSettings.ViewSize = 3f;
                source.CameraSettings.AutoSize = false;
                f.Region.Source = source;
                f.Rig.SnapToPlayer();
                Near(f.View.orthographicSize,3f,"Source layout settings were ignored");
                var loaded = ScriptableObject.CreateInstance<LevelDefinition>();
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), loaded);
                Near(loaded.CameraSettings.ViewSize,3f,"View settings did not serialize");
                UnityEngine.Object.DestroyImmediate(loaded);
            }
            UnityEngine.Object.DestroyImmediate(source);
        }

        private static void ScaledHierarchy()
        {
            using (var f = new Fixture())
            {
                f.Region.UnitSize = 0.5f;
                f.Region.transform.localScale = Vector3.one * 3f;
                f.View.transform.localPosition = new Vector3(2f,-3f,1f);
                f.Position(60f,60f); f.Rig.SnapToPlayer();
                Near(f.View.orthographicSize,9f,"Scaled view size");
                Near(f.View.transform.position.x,60f,"Authored child horizontal offset");
                Near(f.View.transform.position.y,62.25f,"Scaled target offset");
                Near(f.View.transform.position.z,-10f,"Authored child depth offset");
                f.Position(85f,75f); f.Step(240);
                Check(f.View.transform.position.x > 70f,"Scaled hierarchy failed to follow");
                Near(f.View.transform.localPosition.x,2f,"Authored camera offset changed");
            }
        }

        private static void Rewind()
        {
            using (var f = new Fixture())
            {
                f.Position(65f,60f); f.Step(10);
                f.Position(20f,18f); f.Step(1,true);
                Near(f.View.transform.position.x,20f,"Rewind horizontal lag");
                Near(f.View.transform.position.y,19.5f,"Rewind vertical lag");
                f.Step();
                Near(f.View.transform.position.x,20f,"Velocity survived rewind");
                Near(f.View.transform.position.y,19.5f,"Vertical velocity survived rewind");
            }
        }

        private static void Pause()
        {
            using (var f = new Fixture())
            {
                Vector3 start = f.View.transform.position;
                f.Position(65f,60f);
                Tick.Invoke(f.Rig,new object[] { 0f,false });
                Near(f.View.transform.position.x,start.x,"Paused camera moved");
                f.Step(60);
                Check(!float.IsNaN(f.View.transform.position.x) && !float.IsNaN(f.View.transform.position.y),
                    "Zero deltaTime poisoned the smoothing velocity");
                Check(f.View.transform.position.x > 50f,"Camera did not resume");
            }
        }

        private static void Return()
        {
            using (var normal = new Fixture())
            using (var returning = new Fixture())
            {
                normal.Position(65f,60f); returning.Position(65f,60f);
                returning.Body.IsReturning = true;
                normal.Step(6); returning.Step(6);
                Check(returning.View.transform.position.x > normal.View.transform.position.x + 5f,"Return follow was not faster");
            }
        }

        private static void Irregular()
        {
            var source = ScriptableObject.CreateInstance<LevelDefinition>();
            source.CameraSettings.ViewSize = 6f;
            source.CameraSettings.AutoSize = false;
            source.UseCustomBoundary = true;
            for (int y = 0; y < 40; y++)
                for (int x = 0; x < 40; x++)
                    if (x < 12 || y < 12) source.BoundaryCells.Add(new Vector2Int(x,y));
            using (var f = new Fixture(40,40))
            {
                f.Region.Source = source;
                f.Position(10f,20f); f.Rig.SnapToPlayer();
                Near(f.View.transform.position.x,6f * f.View.aspect,"Only enclosing rectangle should constrain");
                Check(f.View.transform.position.x + 6f * f.View.aspect > 12f,"Cutout was unexpectedly confined");
            }
            UnityEngine.Object.DestroyImmediate(source);
        }

        private static void ExistingScenes()
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { LevelTestSceneBuilder.TestSceneFolder }))
            {
                Scene scene = EditorSceneManager.OpenScene(AssetDatabase.GUIDToAssetPath(guid), OpenSceneMode.Additive);
                try
                {
                    LevelSceneRuntime runtime = null;
                    foreach (GameObject root in scene.GetRootGameObjects())
                        if (runtime == null) runtime = root.GetComponentInChildren<LevelSceneRuntime>(true);
                    if (runtime == null || runtime.CameraRig == null) continue;
                    DemoCamera rig = runtime.CameraRig;
                    Check(rig.Region != null,"Old scene lost its serialized fixedRegion override: " + scene.path);
                    rig.SnapToPlayer();
                    Camera view = rig.GetComponentInChildren<Camera>(true);
                    float units = rig.Region.UnitSize * Mathf.Abs(rig.Region.transform.lossyScale.y);
                    float expected = rig.Region.CameraSettings.AutoSize
                        ? Mathf.Min(Mathf.Max(rig.Region.WorldBounds.size.x,rig.Region.WorldBounds.size.y) * 0.5f,12f * units)
                        : rig.Region.CameraSettings.ViewSize * units;
                    Near(view.orthographicSize,expected,"Old scene view size");
                    count++;
                }
                finally { EditorSceneManager.CloseScene(scene,true); }
            }
            Check(count >= 10,"Not enough existing scenes checked: " + count);
            results.Add("INFO: " + count + " existing test scenes retained their camera region");
        }

        private static void GeneratedScene()
        {
            var level = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Levels/Demo_HighJump.prefab");
            Check(level != null,"Fixture level is missing");
            LevelDefinition source = level.GetComponent<LevelRegion>().Source;
            source.CameraSettings.ViewSize = 4.5f;
            source.CameraSettings.AutoSize = false;
            EditorUtility.SetDirty(source);
            AssetDatabase.SaveAssetIfDirty(source);
            string path = LevelTestSceneBuilder.Generate(level,null,false);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Scene scene = EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                LevelSceneRuntime runtime = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (runtime == null) runtime = root.GetComponentInChildren<LevelSceneRuntime>(true);
                Check(runtime != null && runtime.CameraRig.Region != null,"Generated scene did not bind a region");
                Camera view = runtime.CameraRig.GetComponentInChildren<Camera>();
                Near(view.orthographicSize,4.5f,"Builder overwrote per-level size with global defaults");
                typeof(LevelSceneRuntime).GetMethod("Start",BindingFlags.Instance | BindingFlags.NonPublic).Invoke(runtime,null);
                Near(view.orthographicSize,4.5f,"Runtime startup overwrote per-level size");
            }
            finally { EditorSceneManager.CloseScene(scene,true); }
        }
    }
}
