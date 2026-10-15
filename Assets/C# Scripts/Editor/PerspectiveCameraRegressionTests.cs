using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TapTap.Editor
{
    // Execute only in an isolated copy: -executeMethod TapTap.Editor.PerspectiveCameraRegressionTests.RunBatch
    public static class PerspectiveCameraRegressionTests
    {
        private static readonly List<string> results = new List<string>();
        private static readonly MethodInfo tick = typeof(DemoCamera).GetMethod("UpdateFollow", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            try { RunChecks(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        public static void RunChecks()
        {
            results.Clear();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Test("Fixed shots preserve hierarchy offsets, follow region transforms and ignore player following", FixedShot);
                Test("Position, rotation and lens blend; interruption, pause and rewind do not jump", Blending);
                Test("Physical focal length, sensor, gate fit and shift are captured and serialized per level", PhysicalLens);
                Test("Unconfigured old levels receive a full-room perspective fallback at multiple aspect ratios", Fallback);
                Test("Region hiding preserves materials, mechanism enabled state and collision; refresh and extension work", Visibility);
            }
            finally
            {
                Directory.CreateDirectory("Logs/PerspectiveCamera");
                File.WriteAllLines("Logs/PerspectiveCamera/Checks.txt", results);
                File.WriteAllLines("camera-regression-results.txt", results);
            }
        }

        private static void Test(string name, Action action)
        {
            try { action(); results.Add("PASS: " + name); Debug.Log("CAMERA PASS: " + name); }
            catch (Exception error) { results.Add("FAIL: " + name + ": " + error); throw; }
        }

        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Root;
            public readonly Camera Camera;
            public readonly DemoCamera Rig;
            public readonly Transform Shake;
            public readonly LevelRegion A, B;
            public readonly LevelDefinition SourceA, SourceB;

            public Fixture()
            {
                Root = new GameObject("Perspective camera fixture");
                var rig = new GameObject("Rig"); rig.transform.SetParent(Root.transform, false);
                Shake = new GameObject("Shake").transform; Shake.SetParent(rig.transform, false);
                Shake.localPosition = new Vector3(0.1f, 0.2f, 0);
                Camera = new GameObject("Camera").AddComponent<Camera>();
                Camera.transform.SetParent(Shake, false); Camera.transform.localPosition = new Vector3(0.3f, -0.1f, -2);
                Camera.aspect = 1.6f;
                SourceA = ScriptableObject.CreateInstance<LevelDefinition>();
                SourceB = ScriptableObject.CreateInstance<LevelDefinition>();
                SourceA.Size = SourceB.Size = new Vector2Int(20, 12);
                A = new GameObject("Region A").AddComponent<LevelRegion>(); A.transform.SetParent(Root.transform, false);
                B = new GameObject("Region B").AddComponent<LevelRegion>(); B.transform.SetParent(Root.transform, false);
                A.Source = SourceA; B.Source = SourceB;
                A.transform.position = new Vector3(40, 30, 0); B.transform.position = new Vector3(80, -10, 0);
                SourceA.CameraSettings.HasPreset = SourceB.CameraSettings.HasPreset = true;
                SourceA.CameraSettings.LocalPosition = new Vector3(10, 6, -25);
                SourceB.CameraSettings.LocalPosition = new Vector3(8, 7, -40);
                SourceA.CameraSettings.LocalEulerAngles = new Vector3(8, -12, 0);
                SourceB.CameraSettings.LocalEulerAngles = new Vector3(4, 9, 0);
                SourceA.CameraSettings.FieldOfView = 38; SourceB.CameraSettings.FieldOfView = 70;
                SourceA.CameraSettings.BlendDuration = SourceB.CameraSettings.BlendDuration = 1;
                SourceA.CameraSettings.BlendCurve = SourceB.CameraSettings.BlendCurve = AnimationCurve.Linear(0, 0, 1, 1);
                Rig = rig.AddComponent<DemoCamera>(); Rig.Configure(null, Camera, Shake); Rig.SetRegion(A);
            }

            public void Tick(float dt, bool rewind = false) => tick.Invoke(Rig, new object[] { dt, rewind });
            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Root);
                UnityEngine.Object.DestroyImmediate(SourceA);
                UnityEngine.Object.DestroyImmediate(SourceB);
            }
        }

        private static void FixedShot()
        {
            using (var f = new Fixture())
            {
                Check(!f.Camera.orthographic, "Camera is still orthographic");
                var shot = LevelCameraShot.Evaluate(f.A, null, 1.6f);
                Near(f.Camera.transform.position, shot.Position, "Initial shot");
                Check(Quaternion.Angle(f.Camera.transform.rotation, shot.Rotation) < 0.001f, "Initial rotation");
                Vector3 child = f.Camera.transform.localPosition;
                for (int i = 0; i < 20; i++) f.Tick(0.02f);
                Near(f.Camera.transform.position, shot.Position, "Fixed camera drifted");
                Near(f.Camera.transform.localPosition, child, "Authored camera offset changed");
                Near(f.Shake.localPosition, new Vector3(0.1f, 0.2f, 0), "Shake origin changed");
                f.A.UnitSize = 2;
                f.A.transform.localScale = Vector3.one * 1.5f;
                f.A.transform.rotation = Quaternion.Euler(0, 0, 15);
                f.A.transform.position += new Vector3(15, -7, 0);
                f.Tick(0.02f);
                Near(f.Camera.transform.position, f.A.transform.TransformPoint(f.SourceA.CameraSettings.LocalPosition * 2), "Region-local pose conversion");
                f.Rig.SetProgressZoom(0.7f); f.Tick(0.02f);
                Check(f.Camera.fieldOfView < 38, "Progress emphasis did not zoom");
                Near(f.SourceA.CameraSettings.FieldOfView, 38, "Zoom overwrote saved lens");
                f.Rig.SetProgressZoom(1); f.Tick(0.02f);
                Near(f.Camera.fieldOfView, 38, "Zoom did not restore lens");
            }
        }

        private static void Blending()
        {
            using (var f = new Fixture())
            {
                var a = LevelCameraShot.Evaluate(f.A, null, 1.6f);
                var b = LevelCameraShot.Evaluate(f.B, null, 1.6f);
                f.Rig.SetRegion(f.B, false);
                Near(f.Camera.transform.position, a.Position, "Transition jumped at entry");
                f.Tick(0.25f);
                Near(f.Camera.transform.position, Vector3.Lerp(a.Position, b.Position, 0.25f), "Position blend");
                Near(f.Camera.fieldOfView, 46, "Lens blend");
                Check(Quaternion.Angle(f.Camera.transform.rotation, Quaternion.Slerp(a.Rotation, b.Rotation, 0.25f)) < 0.001f, "Rotation blend");
                Vector3 middle = f.Camera.transform.position;
                f.Tick(0);
                Near(f.Camera.transform.position, middle, "Pause advanced the transition");
                f.Rig.SetRegion(f.B, false);
                f.Tick(0.25f);
                Near(f.Camera.transform.position, Vector3.Lerp(a.Position, b.Position, 0.5f), "Repeated region binding restarted the transition");
                middle = f.Camera.transform.position;
                float middleFov = f.Camera.fieldOfView;
                f.Rig.SetRegion(f.A, false); f.Tick(0);
                Near(f.Camera.transform.position, middle, "Interrupted transition jumped");
                Near(f.Camera.fieldOfView, middleFov, "Interrupted lens jumped");
                f.Tick(1);
                Near(f.Camera.transform.position, a.Position, "Transition did not finish");
                Check(!f.Rig.IsBlending, "Transition remains active");
                f.Rig.SetRegion(f.B, false); f.Tick(0.1f, true);
                Near(f.Camera.transform.position, b.Position, "Rewind did not restore region shot immediately");
                f.SourceA.CameraSettings.BlendDuration = 0;
                f.Rig.SetRegion(f.A, false);
                Near(f.Camera.transform.position, a.Position, "Zero-duration transition should cut");
            }
        }

        private static void PhysicalLens()
        {
            const string path = "Assets/PerspectiveLensRegression.asset";
            using (var f = new Fixture())
            {
                var settings = f.SourceA.CameraSettings;
                settings.LensMode = CameraLensMode.Physical;
                settings.FocalLength = 75;
                settings.SensorSize = new Vector2(36, 24);
                settings.LensShift = new Vector2(0.1f, -0.08f);
                settings.GateFit = Camera.GateFitMode.Horizontal;
                f.Rig.SnapToPlayer();
                Check(f.Camera.usePhysicalProperties, "Physical camera was not enabled");
                Near(f.Camera.focalLength, 75, "Saved focal length not applied");
                Check(f.Camera.sensorSize == settings.SensorSize && f.Camera.lensShift == settings.LensShift
                    && f.Camera.gateFit == settings.GateFit, "Physical lens framing not applied");
                var captured = ScriptableObject.CreateInstance<LevelDefinition>();
                LevelDefinition previousSource = f.A.Source;
                try
                {
                    f.A.Source = captured;
                    LevelCameraAuthoring.Capture(captured, f.A, f.Camera);
                    AssetDatabase.CreateAsset(captured, path); AssetDatabase.SaveAssetIfDirty(captured);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    var loaded = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
                    Near(loaded.CameraSettings.FocalLength, 75, "Focal length did not serialize");
                    Near(loaded.CameraSettings.LocalPosition, settings.LocalPosition, "Captured local position");
                    Check(loaded.CameraSettings.LensMode == CameraLensMode.Physical
                        && loaded.CameraSettings.SensorSize == settings.SensorSize
                        && loaded.CameraSettings.GateFit == settings.GateFit
                        && loaded.CameraSettings.LensShift == settings.LensShift, "Physical lens metadata did not serialize");
                }
                finally
                {
                    f.A.Source = previousSource;
                    AssetDatabase.DeleteAsset(path);
                }
                f.SourceB.CameraSettings.LensMode = CameraLensMode.Physical;
                f.SourceB.CameraSettings.FocalLength = 35;
                f.Rig.SetRegion(f.B, false); f.Tick(0.5f);
                Check(f.Camera.focalLength > 35 && f.Camera.focalLength < 75, "Physical lens did not blend between focal lengths");
                f.Tick(0.5f); Near(f.Camera.focalLength, 35, "Target focal length not restored");
                f.SourceA.CameraSettings.LensMode = CameraLensMode.FieldOfView;
                f.Rig.SetRegion(f.A, false); f.Tick(1);
                Check(!f.Camera.usePhysicalProperties, "Lens mode did not switch back to FOV");
                Near(f.Camera.fieldOfView, 38, "FOV mode lens not restored");
            }
        }

        private static void Fallback()
        {
            using (var f = new Fixture())
            {
                f.SourceA.CameraSettings.HasPreset = false;
                f.SourceA.Size = new Vector2Int(80, 10);
                f.SourceA.InvalidateFootprint();
                foreach (float aspect in new[] { 1.6f, 4f / 3f })
                {
                    f.Camera.aspect = aspect; f.Rig.SnapToPlayer();
                    Bounds bounds = f.A.WorldBounds;
                    foreach (Vector3 point in new[] { bounds.min, bounds.max, new Vector3(bounds.min.x, bounds.max.y, 0), new Vector3(bounds.max.x, bounds.min.y, 0) })
                    {
                        Vector3 screen = f.Camera.WorldToViewportPoint(point);
                        Check(screen.z > 0 && screen.x >= 0 && screen.x <= 1 && screen.y >= 0 && screen.y <= 1,
                            "Fallback cropped a room corner: " + screen);
                    }
                }
            }
        }

        private static void Visibility()
        {
            var root = new GameObject("Reveal fixture");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(root.transform, false);
            var renderer = cube.GetComponent<MeshRenderer>();
            var material = renderer.sharedMaterial;
            var sprite = new GameObject("Mechanism sprite").AddComponent<SpriteRenderer>(); sprite.transform.SetParent(root.transform, false);
            sprite.enabled = false;
            var originalHidden = new GameObject("Already hidden").AddComponent<MeshRenderer>(); originalHidden.transform.SetParent(root.transform, false);
            originalHidden.forceRenderingOff = true;
            var reveal = root.AddComponent<RegionRevealView>();
            try
            {
                reveal.SetDiscovered(false);
                Check(renderer.forceRenderingOff && sprite.forceRenderingOff && reveal.Opacity == 0, "Undiscovered visuals are visible");
                Check(cube.GetComponent<Collider>().enabled && root.activeSelf, "Hiding disabled gameplay objects or collision");
                sprite.enabled = true; sprite.enabled = false;
                reveal.SetDiscovered(true);
                Check(!renderer.forceRenderingOff && !sprite.enabled && originalHidden.forceRenderingOff && reveal.Opacity == 1,
                    "Revealing overwrote an independent visual state");
                Check(renderer.sharedMaterial == material, "Visibility changed the material");
                reveal.SetDiscovered(false);
                var nested = new GameObject("Spawned mesh").AddComponent<MeshRenderer>(); nested.transform.SetParent(cube.transform, false);
                reveal.RefreshVisuals();
                Check(nested.forceRenderingOff, "Runtime visuals were not registered by refresh");
                reveal.SetDiscovered(true); Check(!nested.forceRenderingOff, "Registered mesh did not reveal");
                reveal.SetDiscovered(false);
                // Edit-mode fixtures do not receive runtime MonoBehaviour lifecycle messages.
                typeof(RegionRevealView).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(reveal, null);
                UnityEngine.Object.DestroyImmediate(reveal);
                Check(!renderer.forceRenderingOff && originalHidden.forceRenderingOff, "Removing the view did not restore original flags");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < 0.002f, message + ": " + actual + " != " + expected);
        private static void Near(Vector3 actual, Vector3 expected, string message) => Check(Vector3.Distance(actual, expected) < 0.002f, message + ": " + actual + " != " + expected);
    }
}
