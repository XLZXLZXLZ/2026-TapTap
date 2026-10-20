using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Run in an isolated project: -batchmode -executeMethod TapTap.Editor.FinalEscapeRegressionTests.RunBatch
    public static class FinalEscapeRegressionTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly List<string> results = new List<string>();

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            int exitCode = 0;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Test("Refresh before escape rebinds the live level and realigns triggers", BeforeEscape);
                Test("Inactive old hierarchy and templates are excluded from new visibility bindings", DuringEscape);
                Test("Destroyed old level can rebind through the render update while paused", DestroyedLevel);
                Test("Missing replacement releases control and recovers when the level appears", DelayedReplacement);
                Test("Disable releases ending/input locks and re-enable can restart", DisableAndRestart);
                Test("Eight steps reach the target with interruption and pause preserving progress", FinalPose);
                Test("All escape steps preserve entry rotation during blends, interruptions and pause", PreserveRotation);
            }
            catch (Exception error) { results.Add("FAIL: " + error); Debug.LogException(error); exitCode = 1; }
            finally { File.WriteAllLines("final-escape-regression-results.txt", results); }
            EditorApplication.Exit(exitCode);
        }

        private static void Test(string name, Action check)
        {
            check();
            results.Add("PASS: " + name);
            Debug.Log("FINAL ESCAPE PASS: " + name);
        }

        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Root;
            public readonly WorldMapRoot World;
            public readonly LevelDefinition Source, OtherSource;
            public readonly WorldMapDefinition Map;
            public readonly FinalEscapeSequence Sequence;
            public readonly DemoCamera Rig;
            public readonly Camera Camera;
            public readonly RegionRevealView Template;
            public GameObject Generated;
            public LevelRegion Region;
            public RegionRevealView Neighbor;

            public Fixture()
            {
                Root = new GameObject("Final escape regression world");
                World = Root.AddComponent<WorldMapRoot>();
                Source = ScriptableObject.CreateInstance<LevelDefinition>();
                OtherSource = ScriptableObject.CreateInstance<LevelDefinition>();
                Map = ScriptableObject.CreateInstance<WorldMapDefinition>();
                World.Source = Map;
                Map.Modules.Add(new WorldMapModule { Level = Source });
                Map.Modules.Add(new WorldMapModule { Level = OtherSource });
                Generated = Child("Generated", Root.transform);
                Region = CreateRegion("Escape level", Source, Map.Modules[0]);
                Neighbor = CreateRegion("Neighbor", OtherSource, Map.Modules[1]).gameObject.AddComponent<RegionRevealView>();
                Set(World, "generated", Generated.transform);
                GameObject template = Child("Inactive initial layout", Root.transform);
                template.SetActive(false);
                Template = template.AddComponent<RegionRevealView>();
                template.AddComponent<LevelRegion>().Source = Source;
                Child("Player", Root.transform).AddComponent<PlayerController>();
                GameObject rig = Child("Camera rig", Root.transform);
                Camera = Child("Camera", rig.transform).AddComponent<Camera>();
                Rig = rig.AddComponent<DemoCamera>();
                Rig.Configure(null, Camera, null);
                Rig.SetRegion(Region);
                Sequence = Child("Escape sequence", Root.transform).AddComponent<FinalEscapeSequence>();
                Sequence.Level = Source;
                Sequence.TriggerRoot = Child("Triggers", Sequence.transform).transform;
                Sequence.CameraTarget = Child("Target", Sequence.transform).AddComponent<FinalCameraTarget>();
                Sequence.CameraTarget.transform.position = new Vector3(26.5f, -38.9f, -227.9f);
                Call(Sequence, "Start");
            }

            private LevelRegion CreateRegion(string name, LevelDefinition source, WorldMapModule module)
            {
                LevelRegion region = Child(name, Generated.transform).AddComponent<LevelRegion>();
                region.Source = source;
                region.WorldModuleId = module.Id;
                region.transform.position = new Vector3(5f, 128f, 0f);
                return region;
            }

            public GameObject ReplaceGenerated(bool destroyOld = false)
            {
                GameObject previous = Generated;
                previous.SetActive(false); // Destroy in RefreshLevels is deferred until the frame ends.
                Generated = Object.Instantiate(previous, Root.transform);
                Generated.name = "Generated";
                Generated.transform.position += new Vector3(3f, 7f, 0f);
                Generated.SetActive(true);
                Set(World, "generated", Generated.transform);
                World.InvalidateRegions();
                Region = World.FindRegion(Map.Modules[0]);
                Neighbor = World.FindRegion(Map.Modules[1]).GetComponent<RegionRevealView>();
                Rig.SetRegion(Region);
                if (destroyOld) Object.DestroyImmediate(previous);
                return previous;
            }

            public void Tick(float dt) => Call(Sequence, "UpdateSequence", dt);
            public void Dispose()
            {
                Object.DestroyImmediate(Root);
                Object.DestroyImmediate(Map);
                Object.DestroyImmediate(Source);
                Object.DestroyImmediate(OtherSource);
            }
        }

        private static void BeforeEscape()
        {
            using (var f = new Fixture())
            {
                f.ReplaceGenerated();
                Call(f.Sequence, "FixedUpdate");
                Check(Get<LevelRegion>(f.Sequence, "region") == f.Region, "Sequence kept its original region");
                Near(f.Sequence.TriggerRoot.position, f.Region.transform.position, "Trigger alignment");
                f.Sequence.Advance(1);
                Check(f.Sequence.CurrentStep == 1, "First trigger failed after refresh");
            }
        }

        private static void DuringEscape()
        {
            using (var f = new Fixture())
            {
                RegionRevealView oldNeighbor = f.Neighbor;
                f.Sequence.Advance(1); f.Tick(1f);
                Check(f.Rig.HasExternalControl && oldNeighbor.VisibleOpacity > 0.99f, "Escape did not acquire control/reveal");
                f.ReplaceGenerated();
                Call(f.Sequence, "FixedUpdate");
                Check(f.Sequence.CurrentStep == 0 && !f.Rig.HasExternalControl, "Refresh kept the previous escape state");
                Check(!GameplayActionLocks.IsLocked(GameplayAction.Rewind, f.Root.scene), "Refresh kept action locks");
                Check(oldNeighbor.VisibleOpacity == 0f, "Old visibility override was not removed");
                f.Sequence.Advance(1); f.Tick(1f);
                Check(f.Neighbor.VisibleOpacity > 0.99f, "New visibility components were not rebound");
                Check(f.Template.VisibleOpacity == 0f && oldNeighbor.VisibleOpacity == 0f, "Inactive hierarchies received an override");
            }
        }

        private static void DestroyedLevel()
        {
            using (var f = new Fixture())
            {
                f.Sequence.Advance(1); f.Tick(0.2f);
                f.ReplaceGenerated(true);
                f.Tick(0f); // Paused: no FixedUpdate, but stale control must still be released.
                Check(Get<LevelRegion>(f.Sequence, "region") == f.Region, "Destroyed region was not rebound");
                Check(f.Sequence.CurrentStep == 0 && !f.Rig.HasExternalControl, "Render update did not reset");
                f.Sequence.Advance(1);
                Check(f.Sequence.CurrentStep == 1, "Destroyed region prevented restart");
            }
        }

        private static void DelayedReplacement()
        {
            using (var f = new Fixture())
            {
                f.Sequence.Advance(1); f.Tick(0.2f);
                f.Generated.SetActive(false);
                f.Tick(0f);
                Check(!f.Sequence.CanTrigger && !f.Rig.HasExternalControl, "Missing level retained control");
                f.Generated.SetActive(true);
                Call(f.Sequence, "FixedUpdate");
                f.Sequence.Advance(1);
                Check(f.Sequence.CurrentStep == 1, "Level reactivation did not recover");
            }
        }

        private static void DisableAndRestart()
        {
            using (var f = new Fixture())
            {
                f.Sequence.Advance(1); f.Tick(1f);
                var ending = f.Sequence.gameObject.AddComponent<FinalEscapeEndingTransition>();
                ending.EndingScreen = Child("Ending screen", f.Root.transform);
                f.Sequence.Ending = ending;
                Set(f.Sequence, "endingStarted", true);
                GameplayActionLocks.Acquire(ending, GameplayAction.Input);
                f.Sequence.enabled = false;
                // In edit mode, lifecycle callbacks are invoked explicitly for non-ExecuteAlways components.
                Call(f.Sequence, "OnDisable");
                Check(!ending.EndingScreen.activeSelf && !GameplayActionLocks.IsLocked(GameplayAction.Input, f.Root.scene), "Ending was not cancelled");
                Check(!f.Rig.HasExternalControl && !f.Sequence.CanTrigger, "Disabled sequence retained control/triggers");
                f.Sequence.enabled = true;
                Call(f.Sequence, "FixedUpdate");
                f.Sequence.Advance(1);
                Check(f.Sequence.CurrentStep == 1, "Re-enabled sequence could not restart");
            }
        }

        private static void FinalPose()
        {
            using (var f = new Fixture())
            {
                f.ReplaceGenerated(true);
                Call(f.Sequence, "FixedUpdate");
                Quaternion entryRotation = f.Camera.transform.rotation;
                f.Sequence.Advance(1); f.Tick(0.1f);
                f.Sequence.Advance(7); f.Tick(0.1f);
                Vector3 beforePause = f.Camera.transform.position;
                f.Tick(0f);
                Near(f.Camera.transform.position, beforePause, "Pause advanced the camera");
                f.Sequence.Advance(3);
                Check(f.Sequence.CurrentStep == 7, "Repeated triggers reversed progress");
                f.Sequence.Advance(8); f.Tick(f.Sequence.CameraStepDuration);
                Near(f.Camera.transform.position, f.Sequence.CameraTarget.transform.position, "Final camera target");
                Check(Quaternion.Angle(f.Camera.transform.rotation, entryRotation) < 0.01f, "Final rotation changed from entry");
                Check(!f.Camera.orthographic, "Escape changed to orthographic projection");
            }
        }

        private static void PreserveRotation()
        {
            using (var f = new Fixture())
            {
                Quaternion entryRotation = Quaternion.Euler(12f, -17f, 6f);
                f.Camera.transform.rotation = entryRotation;
                f.Sequence.CameraTarget.transform.rotation = Quaternion.Euler(-30f, 45f, 20f);
                for (int step = 1; step <= f.Sequence.StepCount; step++)
                {
                    f.Sequence.Advance(step);
                    f.Tick(f.Sequence.CameraStepDuration * 0.25f);
                    Check(Quaternion.Angle(f.Camera.transform.rotation, entryRotation) < 0.01f,
                        "Step " + step + " rotated during the blend");
                    Vector3 beforePause = f.Camera.transform.position;
                    f.Tick(0f);
                    Near(f.Camera.transform.position, beforePause, "Pause advanced step " + step);
                    Check(Quaternion.Angle(f.Camera.transform.rotation, entryRotation) < 0.01f,
                        "Pause rotated step " + step);
                    // Even steps finish; odd steps are interrupted by the following trigger.
                    if (step % 2 == 0)
                    {
                        f.Tick(f.Sequence.CameraStepDuration);
                        Check(Quaternion.Angle(f.Camera.transform.rotation, entryRotation) < 0.01f,
                            "Step " + step + " rotated at completion");
                    }
                }
                Near(f.Camera.transform.position, f.Sequence.CameraTarget.transform.position, "Pullback still reaches target");
            }
        }

        private static GameObject Child(string name, Transform parent)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }
        private static void Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Private).Invoke(owner, args);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static void Near(Vector3 actual, Vector3 expected, string message) => Check(Vector3.Distance(actual, expected) < 0.001f, message + ": " + actual + " != " + expected);
    }
}
