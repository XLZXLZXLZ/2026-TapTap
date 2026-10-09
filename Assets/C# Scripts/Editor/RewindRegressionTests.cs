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
    // Run in an isolated project with -batchmode -executeMethod TapTap.Editor.RewindRegressionTests.RunBatch.
    // Uses the actual play-mode components and Physics2D queries, without a test-framework dependency.
    public static class RewindRegressionTests
    {
        private const string PendingKey = "TapTap.RewindRegression.Pending";
        private static readonly List<string> results = new List<string>();
        private static readonly Vector2 Origin = new Vector2(10000f, 10000f);
        private static RewindManager previousSceneManager;

        [InitializeOnLoadMethod]
        private static void Subscribe()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/RewindRegressionScene.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/RewindRegressionScene.unity", true) };
            SessionState.SetBool(PendingKey, true);
            Subscribe();
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
                EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            SessionState.SetBool(PendingKey, false);
            results.Clear();
            try
            {
                Test("History wraps, stops at oldest, and discards the old future", HistoryBranches);
                Test("Spring velocity and one-way suppression restore", SpringAndOneWay);
                Test("Extension replays identically and a historical hold can release", ExtensionAndInput);
                Test("Pulling and pickup continue from restored action progress", PullAndPickup);
                Test("Switch undo restores phase and position without events or ejection", SwitchAndPhase);
                Test("Detached death restores tasks and can complete twice", DetachedDeath);
                Test("Grouped death, independent head death, and checkpoint undo", OtherReturnsAndCheckpoint);
                Test("Rewind acceleration is frame-rate independent, capped, adjustable, and resets", Acceleration);
                Test("Rewind visuals animate during undo, settle at oldest, and fade after release", RewindVisualLifecycle);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(LevelTestSceneBuilder.RuntimePrefabPath) != null)
                    Test("Runtime prefab exposes rewind settings and binds the camera and shader", RuntimePrefabWiring);
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                    Test("Rewind shader renders through the camera and fully clears after release", RewindRendering);
                else results.Add("SKIP: Rewind shader rendering requires a graphics device");
                SceneReload();
            }
            catch (Exception exception)
            {
                Finish(exception);
            }
        }

        private static void Finish(Exception exception = null)
        {
            if (exception != null) { results.Add("FAIL: " + exception); Debug.LogException(exception); }
            File.WriteAllLines(Path.Combine(Application.dataPath, "../rewind-regression-results.txt"), results);
            EditorApplication.Exit(exception == null ? 0 : 1);
        }

        private static void Test(string name, Action test)
        {
            test();
            results.Add("PASS: " + name);
            Debug.Log("REWIND PASS: " + name);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Near(Vector2 actual, Vector2 expected, string message)
        {
            Check(Vector2.Distance(actual, expected) < 0.002f, message + " actual=" + actual + " expected=" + expected);
        }

        private sealed class Fixture : IDisposable
        {
            private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
            public readonly RewindManager Manager;
            public PlayerController Player;
            public PlayerInput Input;
            public RespawnService Respawn;
            public MovableEntity Body, Head;
            public PlayerConfig Config;
            public Vector2 Spawn => Origin + Vector2.up * 0.36f;

            public Fixture(float historySeconds = 20f)
            {
                if (RewindManager.Current != null) UnityEngine.Object.DestroyImmediate(RewindManager.Current.gameObject);
                EffectManager.Instance.ClearTransientEffects();
                WorldPhaseState.Instance.RestoreState(false);
                GameObject managerObject = NewObject("Test Rewind", Vector2.zero, false);
                Manager = managerObject.AddComponent<RewindManager>();
                typeof(RewindManager).GetField("historySeconds", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(Manager, historySeconds);
                managerObject.SetActive(true);
                Manager.UseKeyboard = false;
            }

            public GameObject NewObject(string name, Vector2 position, bool active = true)
            {
                var item = new GameObject(name);
                item.SetActive(false);
                item.transform.position = position;
                owned.Add(item);
                if (active) item.SetActive(true);
                return item;
            }

            public GameObject Box(string name, Vector2 position, Vector2 size, bool trigger = false)
            {
                GameObject item = NewObject(name, position);
                var box = item.AddComponent<BoxCollider2D>();
                box.size = size;
                box.isTrigger = trigger;
                return item;
            }

            public void Floor() => Box("Floor", Origin + Vector2.down * 0.5f, new Vector2(100f, 1f))
                .AddComponent<WorldSurface>();

            public MovableEntity Actor(string name, Vector2 position, bool external)
            {
                GameObject item = NewObject(name, position);
                MovableEntity entity = item.AddComponent<MovableEntity>();
                entity.Motor.Collider.size = Vector2.one * 0.7f;
                entity.ControlledExternally = external;
                var visual = new GameObject("Visual");
                visual.transform.SetParent(item.transform, false);
                entity.Visual = visual.transform;
                return entity;
            }

            public void CreatePlayer(bool presentation = false)
            {
                Config = ScriptableObject.CreateInstance<PlayerConfig>();
                owned.Add(Config);
                Config.DeathBoundary = Origin.y - 15f;
                GameObject root = NewObject("Player", Origin);
                Input = root.AddComponent<PlayerInput>();
                Input.SetExternalInput(0f, false);
                Respawn = root.AddComponent<RespawnService>();
                Body = Actor("Body", Spawn, true);
                Head = Actor("Head", Spawn + Vector2.up * Config.JoinedOffset, true);
                PlayerView view = null;
                if (presentation)
                {
                    view = root.AddComponent<PlayerView>();
                    RespawnView returnsView = root.AddComponent<RespawnView>();
                    GameObject lower = new GameObject("Body Orb");
                    GameObject upper = new GameObject("Head Orb");
                    lower.transform.SetParent(root.transform, false);
                    upper.transform.SetParent(root.transform, false);
                    lower.AddComponent<TrailRenderer>();
                    upper.AddComponent<TrailRenderer>();
                    returnsView.ConfigureOrbs(lower.transform, upper.transform);
                }
                Player = root.AddComponent<PlayerController>();
                Player.Configure(Input, Config, Body, Head, Respawn, view);
                Respawn.SetCheckpoint(Spawn);
            }

            public void Detach(Vector2 bodyPosition, Vector2 headPosition)
            {
                PlayerController.Snapshot state = Player.CaptureState();
                state.Phase = PlayerPhase.Detached;
                Player.RestoreState(in state);
                Body.Motor.Teleport(bodyPosition);
                Head.Motor.Teleport(headPosition);
                Body.Velocity = Head.Velocity = Vector2.zero;
                Head.SpringEnabled = true;
            }

            public void Step(int count = 1)
            {
                Physics2D.SyncTransforms();
                for (int i = 0; i < count; i++) Manager.SimulateStep(Time.fixedDeltaTime);
            }

            public void Back(long steps)
            {
                Check(Manager.BeginRewind(), "Could not begin rewind");
                Manager.RewindBy(steps * Time.fixedDeltaTime);
            }

            public void FinishReturn()
            {
                for (int i = 0; i < 200 && Respawn.Journeys.Count > 0; i++) Step();
                Check(Respawn.Journeys.Count == 0, "Return did not finish");
            }

            public void Dispose()
            {
                Manager.EndRewind();
                for (int i = owned.Count - 1; i >= 0; i--)
                    if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            }
        }

        private static void HistoryBranches()
        {
            using (var f = new Fixture(0.1f))
            {
                MovableEntity actor = f.Actor("Recorded actor", Origin, true);
                for (int i = 1; i <= 10; i++) { actor.Motor.Teleport(Origin + Vector2.right * i); f.Step(); }
                Check(f.Manager.OldestTick == 5 && f.Manager.NewestTick == 10, "Ring capacity is incorrect");
                f.Back(2);
                Near(actor.Motor.Position, Origin + Vector2.right * 8f, "Wrong wrapped snapshot");
                long tick = f.Manager.CurrentTick;
                f.Manager.SimulateStep(Time.fixedDeltaTime);
                Check(f.Manager.CurrentTick == tick, "Forward simulation ran while rewinding");
                f.Manager.EndRewind();
                actor.Motor.Teleport(Origin + Vector2.right * 99f);
                f.Step();
                Check(f.Manager.NewestTick == 9, "Old future was retained");
                f.Back(1);
                Near(actor.Motor.Position, Origin + Vector2.right * 8f, "Past branch was overwritten");
                f.Manager.EndRewind();
                f.Step();
                Near(actor.Motor.Position, Origin + Vector2.right * 8f, "Old future replayed");
                f.Back(100);
                Check(f.Manager.CurrentTick == f.Manager.OldestTick, "Did not stop at oldest history");
                Near(actor.Motor.Position, Origin + Vector2.right * 5f, "Wrong oldest snapshot");
            }
        }

        private static void SpringAndOneWay()
        {
            using (var f = new Fixture())
            {
                GameObject springObject = f.Box("Spring", Origin + Vector2.down * 0.5f, new Vector2(10f, 1f));
                springObject.AddComponent<WorldSurface>();
                springObject.AddComponent<SpringPad>();
                f.CreatePlayer();
                f.Body.Velocity = f.Head.Velocity = Vector2.down;
                f.Step();
                Vector2 bouncePosition = f.Body.Motor.Position;
                float bounceSpeed = f.Body.Velocity.y;
                Check(bounceSpeed > 10f, "Spring did not bounce the joined player");
                f.Step(3);
                f.Back(3);
                Near(f.Body.Motor.Position, bouncePosition, "Spring position did not restore");
                Check(Mathf.Abs(f.Body.Velocity.y - bounceSpeed) < 0.0001f, "Rewind reversed or lost spring velocity");
                f.Manager.EndRewind();
                f.Body.Motor.IgnoreOneWayFor(0.3f);
                f.Step();
                float remaining = f.Body.Motor.IgnoreOneWayRemaining;
                f.Step(4);
                f.Back(4);
                Check(Mathf.Abs(f.Body.Motor.IgnoreOneWayRemaining - remaining) < 0.0001f, "Drop-through time was lost");
            }
        }

        private static void ExtensionAndInput()
        {
            using (var f = new Fixture())
            {
                f.Floor();
                f.Box("Head platform", Origin + Vector2.up * 2f, new Vector2(4f, 0.2f))
                    .AddComponent<WorldSurface>().Kind = SurfaceKind.OneWay;
                f.CreatePlayer();
                f.Input.SetExternalInput(0f, true);
                f.Step(8);
                Vector2 middleHead = f.Head.Motor.Position;
                f.Step(8);
                Vector2 expectedHead = f.Head.Motor.Position;
                PlayerController.Snapshot expected = f.Player.CaptureState();
                f.Back(8);
                Near(f.Head.Motor.Position, middleHead, "Mid-extension did not restore");
                f.Manager.EndRewind();
                f.Step(8);
                Near(f.Head.Motor.Position, expectedHead, "Extension diverged after restore");
                Check(f.Player.Phase == expected.Phase, "Extension phase diverged");
                f.Step(10);
                Check(f.Player.Phase == PlayerPhase.Holding, "Head did not reach Holding");
                f.Input.SetExternalInput(0f, false);
                f.Step(2);
                f.Back(2);
                Check(f.Player.Phase == PlayerPhase.Holding && f.Input.SpaceHeld, "Historical hold was not restored");
                f.Manager.EndRewind();
                f.Step();
                Check(f.Player.Phase == PlayerPhase.Retracting, "Historical hold got stuck without Release");
                f.Step(25);
                Check(f.Player.Phase == PlayerPhase.Detached, "Head failed to detach on platform");
            }
            using (var f = new Fixture())
            {
                f.Floor();
                f.CreatePlayer();
                f.Input.SetExternalInput(0f, true);
                f.Step();
                f.Back(1);
                Check(f.Player.Phase == PlayerPhase.Joined && !f.Input.SpaceHeld, "Initial snapshot captured unconsumed input");
                f.Manager.EndRewind();
                f.Step();
                Check(f.Player.Phase == PlayerPhase.Joined, "Holding real Space generated an accidental new action");
                f.Input.SetExternalInput(0f, false);
                f.Step();
                f.Input.SetExternalInput(0f, true);
                f.Step();
                Check(f.Player.Phase == PlayerPhase.Extending, "Space did not rearm after release");
            }
        }

        private static void PullAndPickup()
        {
            using (var f = new Fixture())
            {
                f.Floor();
                f.Box("Recall platform", Origin + Vector2.up * 4f, new Vector2(4f, 0.2f))
                    .AddComponent<WorldSurface>().Kind = SurfaceKind.OneWay;
                f.CreatePlayer();
                f.Detach(f.Spawn, Origin + Vector2.up * 4.46f);
                f.Input.SetExternalInput(0f, true);
                f.Step();
                f.Input.SetExternalInput(0f, false);
                f.Step(8);
                Check(f.Player.Phase == PlayerPhase.Pulling, "Recall did not begin");
                f.Step(8);
                Vector2 expectedBody = f.Body.Motor.Position;
                float expectedTime = f.Player.CaptureState().ActionTime;
                f.Back(8);
                f.Manager.EndRewind();
                f.Step(8);
                Near(f.Body.Motor.Position, expectedBody, "Pull target or progress diverged");
                Check(Mathf.Abs(f.Player.CaptureState().ActionTime - expectedTime) < 0.0001f, "Pull timer diverged");
            }
            using (var f = new Fixture())
            {
                f.Floor();
                f.CreatePlayer();
                f.Detach(f.Spawn, f.Spawn + Vector2.right * 0.8f);
                f.Input.SetExternalInput(1f, false);
                for (int i = 0; i < 10 && f.Player.Phase != PlayerPhase.PickingUp; i++) f.Step();
                Check(f.Player.Phase == PlayerPhase.PickingUp, "Side contact did not begin pickup");
                f.Step(3);
                f.Step(3);
                Vector2 expectedHead = f.Head.Motor.Position;
                Vector2 expectedBody = f.Body.Motor.Position;
                f.Back(3);
                Check(f.Player.Phase == PlayerPhase.PickingUp, "Mid-pickup phase did not restore");
                f.Manager.EndRewind();
                f.Step(3);
                Near(f.Head.Motor.Position, expectedHead, "Pickup offset or progress diverged");
                Near(f.Body.Motor.Position, expectedBody, "Pickup body motion diverged");
            }
        }

        private static void SwitchAndPhase()
        {
            using (var f = new Fixture())
            {
                f.Floor();
                f.CreatePlayer();
                GameObject blockObject = f.Box("Phase block", Origin + Vector2.up * 0.8f, new Vector2(1f, 2f));
                PhaseBlock block = blockObject.AddComponent<PhaseBlock>();
                MechanismSwitch button = f.Box("Switch", f.Spawn, new Vector2(1f, 1f), true).AddComponent<MechanismSwitch>();
                int changes = 0;
                Action<bool> changed = value => changes++;
                WorldPhaseState.Instance.Changed += changed;
                try
                {
                    Vector2 before = f.Body.Motor.Position;
                    f.Step();
                    Check(WorldPhaseState.Instance.Active && block.IsSolid, "Switch did not materialize block");
                    Check(Vector2.Distance(before, f.Body.Motor.Position) > 0.1f, "Materialization did not eject player");
                    Check(button.CaptureState().Occupied, "Switch did not remember occupancy");
                    f.Back(1);
                    Check(!WorldPhaseState.Instance.Active && !block.IsSolid, "Phase did not undo");
                    Near(f.Body.Motor.Position, before, "Restore ran materialization ejection");
                    Check(!button.CaptureState().Occupied && changes == 1, "Restore replayed switch events");
                    f.Manager.EndRewind();
                    f.Step();
                    Check(WorldPhaseState.Instance.Active && changes == 2, "Switch could not trigger after undo");
                }
                finally { WorldPhaseState.Instance.Changed -= changed; }
            }
        }

        private static void DetachedDeath()
        {
            using (var f = new Fixture())
            {
                f.Floor();
                f.Box("Head support", Origin + new Vector2(3f, 2f), new Vector2(2f, 0.2f)).AddComponent<WorldSurface>();
                f.CreatePlayer(true);
                Vector2 deathPosition = f.Spawn + Vector2.right * 8f;
                f.Detach(deathPosition, Origin + new Vector2(3f, 2.46f));
                f.Box("Spikes", deathPosition, Vector2.one * 0.5f, true).AddComponent<LethalZone>();
                int deaths = 0;
                f.Player.Effect += effect => { if (effect == PlayerEffect.Death) deaths++; };
                f.Step();
                Check(f.Body.IsReturning && !f.Head.IsReturning && f.Respawn.Journeys.Count == 1, "Detached death returned the head");
                Vector2 historicalHead = f.Head.Motor.Position;
                f.Step(25);
                float elapsed = f.Respawn.Journeys[0].Elapsed;
                f.Step(4);
                f.Back(4);
                Check(Mathf.Abs(f.Respawn.Journeys[0].Elapsed - elapsed) < 0.0001f, "Journey progress did not restore");
                f.Manager.EndRewind();
                f.FinishReturn();
                Check(!f.Body.IsReturning && f.Body.Motor.CollisionsEnabled && f.Player.Phase == PlayerPhase.Detached,
                    "Single return did not release the body or incorrectly joined the head");
                f.Back(f.Manager.CurrentTick - 1);
                Check(f.Respawn.Journeys.Count == 1 && f.Body.IsReturning && !f.Body.Motor.CollisionsEnabled,
                    "Completed journey could not be restored");
                Near(f.Head.Motor.Position, historicalHead, "Head history did not restore");
                Check(deaths == 1, "Rewind replayed death effects");
                f.Manager.EndRewind();
                f.FinishReturn();
                Check(!f.Body.IsReturning, "Restored journey got stuck");
                f.Back(f.Manager.CurrentTick);
                Check(f.Respawn.Journeys.Count == 0 && !f.Body.IsReturning && f.Body.Motor.CollisionsEnabled,
                    "Pre-death state retained a future journey");
                Near(f.Body.Motor.Position, deathPosition, "Pre-death position did not restore");
            }
        }

        private static void OtherReturnsAndCheckpoint()
        {
            using (var f = new Fixture())
            {
                f.Floor();
                f.CreatePlayer();
                Vector2 checkpoint = f.Respawn.Checkpoint;
                f.Step();
                f.Respawn.SetCheckpoint(f.Spawn + Vector2.right * 5f);
                f.Step();
                f.Back(1);
                Near(f.Respawn.Checkpoint, checkpoint, "Checkpoint did not undo");
                f.Manager.EndRewind();
                f.Box("Grouped spikes", f.Spawn, Vector2.one * 0.5f, true).AddComponent<LethalZone>();
                f.Step();
                Check(f.Body.IsReturning && f.Head.IsReturning && f.Respawn.Journeys[0].Companion == f.Head,
                    "Joined death did not create a grouped journey");
                f.Back(1);
                Check(!f.Body.IsReturning && !f.Head.IsReturning && f.Respawn.Journeys.Count == 0,
                    "Grouped death did not undo");
            }
            using (var f = new Fixture())
            {
                f.Floor();
                f.CreatePlayer();
                Vector2 headPosition = f.Spawn + Vector2.right * 6f;
                f.Detach(f.Spawn, headPosition);
                f.Box("Head spikes", headPosition, Vector2.one * 0.5f, true).AddComponent<LethalZone>();
                f.Step();
                Check(!f.Body.IsReturning && f.Head.IsReturning, "Head-only death returned the body");
                f.Back(1);
                Check(!f.Body.IsReturning && !f.Head.IsReturning && f.Respawn.Journeys.Count == 0,
                    "Head-only death did not undo");
            }
        }

        private static void SceneReload()
        {
            if (RewindManager.Current == null) new GameObject("Old scene rewind").AddComponent<RewindManager>();
            previousSceneManager = RewindManager.Current;
            previousSceneManager.UseKeyboard = false;
            previousSceneManager.SimulateStep(Time.fixedDeltaTime);
            previousSceneManager.SimulateStep(Time.fixedDeltaTime);
            Check(previousSceneManager.NewestTick == 2, "Old scene did not contain history");
            previousSceneManager.BeginRewind();
            previousSceneManager.SimulateRewind(0.01f);
            EffectManager.Instance.Simulate(0.2f);
            SceneManager.sceneLoaded += OnTestSceneLoaded;
            SceneManager.LoadScene(0, LoadSceneMode.Single);
        }

        private static void OnTestSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnTestSceneLoaded;
            try
            {
                Check(previousSceneManager == null && RewindManager.Current != null && RewindManager.Current.NewestTick == 0,
                    "Scene transition retained old history");
                Check(RewindManager.Current.RewindHeldSeconds == 0d && !RewindManager.Current.IsRewinding
                    && EffectManager.Instance.RewindVisualStrength == 0f, "Scene transition retained rewind presentation or acceleration");
                results.Add("PASS: Scene reload starts a fresh rewind manager");
                Debug.Log("REWIND PASS: Scene reload starts a fresh rewind manager");
                Finish();
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static void Acceleration()
        {
            long? referenceSteps = null;
            foreach (int frameRate in new[] { 30, 60, 144 })
            {
                using (var f = new Fixture())
                {
                    f.Step(950);
                    Check(f.Manager.BeginRewind(), "Acceleration could not begin");
                    Check(Mathf.Approximately(f.Manager.CurrentRewindSpeed, 1f), "Starting speed is incorrect");
                    float previousScale = Time.timeScale;
                    try
                    {
                        Time.timeScale = 0.25f;
                        for (int frame = 0; frame < frameRate * 5; frame++) f.Manager.SimulateRewind(1f / frameRate);
                        Check(Mathf.Abs(f.Manager.CurrentRewindSpeed - 2f) < 0.0001f, "Speed did not reach its cap");
                        long steps = 950 - f.Manager.CurrentTick;
                        Check(Math.Abs(steps * Time.fixedDeltaTime - 7.5f) <= Time.fixedDeltaTime,
                            "Ramp did not integrate to 7.5 seconds");
                        if (referenceSteps.HasValue) Check(Math.Abs(steps - referenceSteps.Value) <= 1, "Frame rates produced different histories");
                        referenceSteps = steps;
                        double held = f.Manager.RewindHeldSeconds;
                        f.Manager.BeginRewind();
                        Check(f.Manager.RewindHeldSeconds == held, "Repeated key polling reset acceleration");
                        f.Manager.SimulateRewind(0.1f);
                        Check(Mathf.Abs((950 - f.Manager.CurrentTick) * Time.fixedDeltaTime - 7.7f) <= Time.fixedDeltaTime,
                            "Speed did not remain capped");
                    }
                    finally { Time.timeScale = previousScale; }
                    f.Manager.EndRewind();
                    Check(!f.Manager.IsRewinding && f.Manager.RewindHeldSeconds == 0d, "Release retained the hold timer");
                    f.Manager.BeginRewind();
                    Check(Mathf.Approximately(f.Manager.CurrentRewindSpeed, 1f), "Repress did not restart at one-times speed");
                    f.Manager.ClearHistory();
                    Check(!f.Manager.IsRewinding && f.Manager.RewindHeldSeconds == 0d, "Clearing history retained acceleration");
                }
            }
            using (var f = new Fixture())
            {
                f.Step(950);
                f.Manager.BeginRewind();
                f.Manager.SimulateRewind(5.1f);
                Check(Mathf.Abs((950 - f.Manager.CurrentTick) * Time.fixedDeltaTime - 7.7f) <= Time.fixedDeltaTime,
                    "A frame crossing the cap was integrated incorrectly");
                f.Manager.EndRewind();
                SetField(f.Manager, "startSpeed", 2f);
                SetField(f.Manager, "maxSpeed", 4f);
                SetField(f.Manager, "accelerationDuration", 1f);
                f.Manager.BeginRewind();
                long before = f.Manager.CurrentTick;
                f.Manager.SimulateRewind(0.5f);
                Check(Mathf.Approximately(f.Manager.CurrentRewindSpeed, 3f)
                    && Mathf.Abs((before - f.Manager.CurrentTick) * Time.fixedDeltaTime - 1.25f) <= Time.fixedDeltaTime,
                    "Custom speed parameters did not apply");
                f.Manager.EndRewind();
                SetField(f.Manager, "accelerationDuration", 0f);
                f.Manager.BeginRewind();
                Check(Mathf.Approximately(f.Manager.CurrentRewindSpeed, 4f), "Zero-duration acceleration did not use the maximum speed");
            }
        }

        private static void RewindVisualLifecycle()
        {
            using (var f = new Fixture())
            {
                EffectManager effects = EffectManager.Instance;
                effects.SetRewindVisual(false);
                effects.Simulate(1f);
                f.Step(20);
                f.Manager.BeginRewind();
                effects.Simulate(0.2f);
                Check(effects.RewindVisualStrength == 1f && effects.RewindMotionStrength == 1f,
                    "Visual update was blocked during rewind");
                f.Manager.RewindBy(100f);
                effects.Simulate(0.2f);
                Check(f.Manager.AtOldestHistory && effects.RewindMotionStrength <= 0.151f
                    && effects.RewindVisualStrength == 1f, "Oldest history did not settle dynamic interference");
                f.Manager.EndRewind();
                f.Step();
                Check(f.Manager.NewestTick == 1 && effects.RewindVisualStrength > 0f,
                    "Visual fade delayed resuming simulation");
                effects.Simulate(0.1f);
                Check(effects.RewindVisualStrength > 0f && effects.RewindVisualStrength < 1f, "Release did not fade the effect");
                effects.Simulate(0.2f);
                Check(effects.RewindVisualStrength == 0f, "Release left a persistent effect");
                f.Manager.BeginRewind();
                effects.Simulate(0.2f);
                typeof(RewindManager).GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(f.Manager, new object[] { false });
                effects.Simulate(0.2f);
                Check(!f.Manager.IsRewinding && effects.RewindVisualStrength == 0f, "Focus loss retained rewind visuals");
            }
        }

        private static void RewindRendering()
        {
            using (var f = new Fixture())
            {
                EffectManager effects = EffectManager.Instance;
                Shader shader = effects.RewindShader;
                Check(shader != null && shader.isSupported, "Rewind shader is missing or unsupported");
                var texture = new Texture2D(1, 1);
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
                var target = new RenderTexture(960, 540, 24);
                var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    void Rectangle(string name, Vector2 center, Vector2 size, Color color)
                    {
                        GameObject item = f.NewObject(name, Origin + center);
                        item.transform.localScale = size;
                        SpriteRenderer renderer = item.AddComponent<SpriteRenderer>();
                        renderer.sprite = sprite;
                        renderer.color = color;
                    }
                    Rectangle("Floor", new Vector2(0f, -2.6f), new Vector2(12f, 0.5f), new Color(0.25f, 0.34f, 0.4f));
                    Rectangle("Platform", new Vector2(1.3f, -0.6f), new Vector2(3f, 0.3f), new Color(0.3f, 0.7f, 0.65f));
                    Rectangle("Player", new Vector2(-1.5f, -1.9f), new Vector2(0.7f, 0.9f), new Color(1f, 0.32f, 0.16f));
                    Rectangle("Head", new Vector2(-1.5f, -1.2f), new Vector2(0.7f, 0.5f), new Color(1f, 0.8f, 0.2f));
                    Rectangle("Block", new Vector2(3f, 0.4f), new Vector2(0.8f, 1.8f), new Color(0.4f, 0.6f, 0.95f));
                    for (int i = 0; i < 12; i++)
                        Rectangle("Grid", new Vector2(i - 5.5f, 0f), new Vector2(0.015f, 6f), new Color(0.12f, 0.18f, 0.24f));
                    GameObject cameraObject = f.NewObject("Rewind camera", Origin);
                    cameraObject.transform.position += Vector3.back * 10f;
                    Camera camera = cameraObject.AddComponent<Camera>();
                    camera.enabled = false;
                    camera.orthographic = true;
                    camera.orthographicSize = 3f;
                    camera.aspect = 16f / 9f;
                    camera.backgroundColor = new Color(0.06f, 0.09f, 0.14f);
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.targetTexture = target;
                    cameraObject.AddComponent<DemoCamera>().Configure(null, camera, null);
                    Check(cameraObject.GetComponent<RewindScreenEffect>() != null, "Runtime camera did not attach the image effect");
                    Color[] Capture(string filename)
                    {
                        camera.Render();
                        RenderTexture.active = target;
                        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                        image.Apply();
                        File.WriteAllBytes(Path.Combine(Application.dataPath, "../" + filename), image.EncodeToPNG());
                        return image.GetPixels();
                    }
                    effects.SetRewindVisual(false);
                    effects.Simulate(1f);
                    Color[] baseline = Capture("rewind-before.png");
                    effects.SetRewindVisual(true, 0f);
                    effects.Simulate(0.2f);
                    SetField(effects, "rewindSignalClock", 4.1f);
                    Color[] rewound = Capture("rewind-active.png");
                    effects.SetRewindVisual(true, 1f);
                    effects.Simulate(0.2f);
                    SetField(effects, "rewindSignalClock", 4.1f);
                    Capture("rewind-fast.png");
                    effects.SetRewindVisual(false);
                    effects.Simulate(0.3f);
                    Color[] released = Capture("rewind-released.png");
                    float difference = 0f, residue = 0f;
                    for (int i = 0; i < baseline.Length; i++)
                    {
                        difference += Mathf.Abs(baseline[i].r - rewound[i].r) + Mathf.Abs(baseline[i].g - rewound[i].g);
                        residue += Mathf.Abs(baseline[i].r - released[i].r) + Mathf.Abs(baseline[i].g - released[i].g);
                    }
                    Check(difference / baseline.Length > 0.003f, "Shader did not modify the camera output");
                    Check(residue / baseline.Length < 0.0001f, "Shader remained visible after release");
                    foreach (var message in ShaderUtil.GetShaderMessages(shader))
                        Check(message.severity.ToString() != "Error", "Shader compilation failed: " + message.message);
                }
                finally
                {
                    effects.SetRewindVisual(false);
                    effects.Simulate(1f);
                    RenderTexture.active = previous;
                    UnityEngine.Object.DestroyImmediate(target);
                    UnityEngine.Object.DestroyImmediate(image);
                    UnityEngine.Object.DestroyImmediate(sprite);
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        private static void RuntimePrefabWiring()
        {
            GameObject prefab = LevelTestSceneBuilder.EnsureRuntimePrefab();
            RewindManager rewind = prefab.GetComponentInChildren<RewindManager>(true);
            EffectManager effects = prefab.GetComponentInChildren<EffectManager>(true);
            Camera camera = prefab.GetComponentInChildren<Camera>(true);
            Check(rewind != null && effects != null && camera != null
                && camera.GetComponent<RewindScreenEffect>() != null, "Runtime prefab did not wire rewind components");
            var settings = new SerializedObject(rewind);
            Check(settings.FindProperty("startSpeed").floatValue == 1f
                && settings.FindProperty("maxSpeed").floatValue == 2f
                && settings.FindProperty("accelerationDuration").floatValue == 5f, "Runtime prefab has incorrect speed defaults");
            Check(effects.RewindShader != null && Array.IndexOf(AssetDatabase.GetDependencies(LevelTestSceneBuilder.RuntimePrefabPath),
                AssetDatabase.GetAssetPath(effects.RewindShader)) >= 0, "Runtime prefab does not reference the rewind shader");
        }
    }
}
