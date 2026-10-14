using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TapTap.Editor
{
    // Focused checks only. Run in an isolated copy: -batchmode -executeMethod TapTap.Editor.CrossRegionRegressionTests.RunBatch
    public static class CrossRegionRegressionTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly List<string> results = new List<string>();

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project.");
            int exit = 0;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Test("Concave footprints, transformed coordinates and boundary buffer", Footprints);
                Test("Resident hidden regions, body-only switching and same-step checkpoint", RegionSwitching);
                Test("Cross-region rewind retains discovery and restores checkpoint", Rewind);
                Test("Death returns keep their region and flag collisions do not select checkpoints", DeathAndFlags);
                Test("Camera moves between rooms without snapping; pause and rewind remain valid", CameraTransition);
                Test("Fade preserves live sprite colors and reveals real rendered pixels", Fade);
                Test("Square platform geometry, one-way collision and animation notification interfaces", Square);
                LevelEditorAssets.EnsureSquarePlatformInPalette(AssetDatabase.LoadAssetAtPath<LevelPalette>(LevelEditorAssets.DefaultPalettePath));
                AssetDatabase.SaveAssets();
            }
            catch (Exception exception) { results.Add("FAIL: " + exception); Debug.LogException(exception); exit = 1; }
            File.WriteAllLines(Path.Combine(Application.dataPath, "../cross-region-results.txt"), results);
            EditorApplication.Exit(exit);
        }

        public static void RunDiscoveryResetBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated project.");
            int exit = 0;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Test("Visited regions survive leaving but reset to the selected entry after world reset", DiscoveryReset);
                Test("Rewind retains this run's discovery and restores checkpoint", Rewind);
            }
            catch (Exception exception) { results.Add("FAIL: " + exception); Debug.LogException(exception); exit = 1; }
            File.WriteAllLines(Path.Combine(Application.dataPath, "../discovery-reset-results.txt"), results);
            EditorApplication.Exit(exit);
        }

        private static void DiscoveryReset()
        {
            using (var f = new Fixture())
            {
                WorldMapModule a = f.Definition.Modules[0], b = f.Definition.Modules[1];
                Check(f.Flow.HasDiscovered(a) && !f.Flow.HasDiscovered(b) && f.A.GetComponent<RegionRevealView>().Opacity == 1f,
                    "Initial entry was not immediately visible, or neighbors were discovered");
                f.Player.TeleportJoined(new Vector2(5, 2)); f.Flow.UpdatePlayerRegion(f.Player);
                Call(f.B.GetComponent<RegionRevealView>(), "UpdateFade", 1f);
                f.Player.TeleportJoined(new Vector2(2, 2)); f.Flow.UpdatePlayerRegion(f.Player);
                Check(f.Flow.HasDiscovered(b) && !f.B.GetComponentInChildren<SpriteRenderer>().forceRenderingOff,
                    "Leaving a visited region hid it");
                // Legacy saved discovery must not affect a fresh run; completed/unlocked progress remains independent.
                string key = "TapTap.WorldProgress." + f.Definition.ProgressId;
                PlayerPrefs.SetString(key, "{\"Discovered\":[\"" + a.Id + "\",\"" + b.Id
                    + "\"],\"Completed\":[\"" + b.Id + "\"],\"Unlocked\":[\"" + b.Id + "\"]}");
                Call(f.Flow, "Activate", 0, true);
                Check(f.Flow.HasDiscovered(a) && !f.Flow.HasDiscovered(b) && f.B.GetComponentInChildren<SpriteRenderer>().forceRenderingOff,
                    "World reset retained visited or legacy saved discovery");
                Check(WorldMapProgressStore.Completed(f.Definition).Contains(b.Id) && WorldMapProgressStore.IsUnlocked(f.Definition, b),
                    "Reset visibility erased completion or unlock progress");
                Call(f.Flow, "Activate", 1, true);
                Check(f.Flow.HasDiscovered(b) && !f.Flow.HasDiscovered(a) && f.B.GetComponent<RegionRevealView>().Opacity == 1f
                    && f.A.GetComponentInChildren<SpriteRenderer>().forceRenderingOff,
                    "Reset to a different entry did not reveal only that entry");
            }
        }

        private static void Test(string name, Action run) { run(); results.Add("PASS: " + name); Debug.Log("CROSS REGION PASS: " + name); }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Near(float a, float b, string message) => Check(Mathf.Abs(a - b) < 0.015f, message + ": " + a + " / " + b);
        private static GameObject Child(string name, Transform parent)
        { var child = new GameObject(name); child.transform.SetParent(parent, false); return child; }
        private static void Call(object target, string method, params object[] args)
            => target.GetType().GetMethod(method, Private).Invoke(target, args);

        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Root = new GameObject("Cross-region fixture");
            public readonly WorldMapDefinition Definition = ScriptableObject.CreateInstance<WorldMapDefinition>();
            public readonly PlayerConfig Config = ScriptableObject.CreateInstance<PlayerConfig>();
            public readonly WorldMapRoot World;
            public readonly WorldMapProgression Flow;
            public readonly PlayerController Player;
            public readonly RespawnService Respawn;
            public readonly RewindManager Rewind;
            public readonly DemoCamera Rig;
            public readonly Camera Camera;
            public readonly LevelRegion A, B;
            public Fixture()
            {
                Definition.ProgressId = Guid.NewGuid().ToString("N");
                World = Child("World", Root.transform).AddComponent<WorldMapRoot>();
                World.Source = Definition;
                Transform generated = Child("Generated", World.transform).transform;
                A = Region("A", 0, generated); B = Region("B", 4, generated);
                WorldMapProgressStore.Clear(Definition);
                Config.Gravity = 0.1f; Config.DeathBoundary = -100;
                GameObject actor = Child("Player", Root.transform);
                PlayerInput input = actor.AddComponent<PlayerInput>(); input.UseKeyboard = false;
                Respawn = actor.AddComponent<RespawnService>();
                MovableEntity body = Child("Body", actor.transform).AddComponent<MovableEntity>();
                MovableEntity head = Child("Head", actor.transform).AddComponent<MovableEntity>();
                body.GetComponent<BoxCollider2D>().size = new Vector2(0.6f, 0.8f);
                head.GetComponent<BoxCollider2D>().size = new Vector2(0.6f, 0.6f);
                Player = actor.AddComponent<PlayerController>();
                Player.Configure(input, Config, body, head, Respawn, null);
                GameObject rig = Child("Rig", Root.transform);
                Camera = Child("Camera", rig.transform).AddComponent<Camera>();
                Camera.enabled = false; Camera.aspect = 2f;
                Rig = rig.AddComponent<DemoCamera>(); Rig.Configure(Player, Camera, null);
                LevelSceneRuntime runtime = Child("Runtime", Root.transform).AddComponent<LevelSceneRuntime>();
                runtime.ConfigureResources(null, Config, Rig, null);
                Rewind = Child("Rewind", Root.transform).AddComponent<RewindManager>();
                Rewind.UseKeyboard = false; Call(Rewind, "Awake");
                Flow = World.gameObject.AddComponent<WorldMapProgression>(); Flow.UseKeyboard = false;
                Check(Flow.Initialize(World, Player, runtime), "World initialization failed");
                Physics2D.SyncTransforms();
            }
            private LevelRegion Region(string name, int x, Transform parent)
            {
                LevelDefinition layout = ScriptableObject.CreateInstance<LevelDefinition>(); layout.Size = new Vector2Int(4, 4);
                WorldMapModule module = new WorldMapModule { Level = layout, Position = new Vector2Int(x, 0) };
                Definition.Modules.Add(module);
                LevelRegion region = Child(name, parent).AddComponent<LevelRegion>();
                region.Source = layout; region.Size = layout.Size; region.WorldModuleId = module.Id;
                region.transform.localPosition = new Vector3(x, 0, 0);
                region.EntryCheckpoint = Child("Entry", region.transform).AddComponent<CheckpointFlag>();
                region.EntryCheckpoint.transform.localPosition = new Vector3(1, 1, 0);
                SpriteRenderer fill = Child("Fill", region.transform).AddComponent<SpriteRenderer>();
                fill.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Sprite/Prototype/Square.png");
                fill.transform.localPosition = new Vector3(2, 2, 0); fill.color = Color.red;
                return region;
            }
            public void Dispose()
            {
                WorldMapProgressStore.Clear(Definition);
                UnityEngine.Object.DestroyImmediate(Root);
                foreach (WorldMapModule module in Definition.Modules) UnityEngine.Object.DestroyImmediate(module.Level);
                UnityEngine.Object.DestroyImmediate(Definition); UnityEngine.Object.DestroyImmediate(Config);
                if (WorldPhaseState.Existing != null) UnityEngine.Object.DestroyImmediate(WorldPhaseState.Existing.gameObject);
                if (EffectManager.Existing != null) UnityEngine.Object.DestroyImmediate(EffectManager.Existing.gameObject);
            }
        }

        private static void Footprints()
        {
            GameObject root = new GameObject("Footprint");
            LevelDefinition layout = ScriptableObject.CreateInstance<LevelDefinition>();
            try
            {
                layout.UseCustomBoundary = true;
                layout.BoundaryCells.AddRange(new[] { new Vector2Int(-1, 0), Vector2Int.zero, Vector2Int.up });
                LevelRegion region = root.AddComponent<LevelRegion>(); region.Source = layout; region.UnitSize = 2f;
                root.transform.position = new Vector3(-10, 8); root.transform.localScale = Vector3.one * 2f;
                Vector2 World(Vector2 p) => root.transform.TransformPoint(p * region.UnitSize);
                Check(region.ContainsWorldPoint(World(new Vector2(-0.5f, 0.5f))), "Negative occupied cell missed");
                Check(!region.ContainsWorldPoint(World(new Vector2(-0.5f, 1.5f))), "Concavity treated as part of region");
                Check(region.ContainsWorldPoint(World(new Vector2(1.08f, 0.5f)), 0.12f), "Buffer lost under scaling");
                Check(!region.ContainsWorldPoint(World(new Vector2(1.2f, 0.5f)), 0.12f), "Buffer too large");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(layout); }
        }

        private static void RegionSwitching()
        {
            using (var f = new Fixture())
            {
                SpriteRenderer neighbor = f.B.GetComponentInChildren<SpriteRenderer>();
                Check(f.A.gameObject.activeInHierarchy && f.B.gameObject.activeInHierarchy && neighbor.forceRenderingOff,
                    "Hidden region is disabled or visible");
                Check(f.B.EntryCheckpoint.GetComponent<Collider2D>().enabled, "Hidden region lost collision");
                WorldPhaseState state = WorldPhaseState.Instance;
                state.transform.SetParent(f.Root.transform, true);
                PhaseBlockOutline outline = Child("Global ghost outline", state.transform).AddComponent<PhaseBlockOutline>();
                MeshFilter mesh = outline.gameObject.AddComponent<MeshFilter>();
                outline.ConfigureRenderer(mesh, outline.gameObject.AddComponent<MeshRenderer>());
                Call(state, "Awake");
                PhaseBlock ghost = Child("Unvisited ghost", f.B.transform).AddComponent<PhaseBlock>();
                Call(state, "Register", ghost); Call(outline, "LateUpdate");
                Check(mesh.sharedMesh.vertexCount == 0, "Global ghost outline leaked an unvisited region");
                f.Player.Head.Motor.Teleport(new Vector2(6, 2)); f.Flow.UpdatePlayerRegion(f.Player);
                Check(f.Flow.CurrentRegion == f.A, "Head changed region");
                f.Player.Body.Motor.Teleport(new Vector2(4.05f, 2)); f.Flow.UpdatePlayerRegion(f.Player);
                Check(f.Flow.CurrentRegion == f.A, "Boundary buffer was ignored");
                f.Player.Body.Motor.Teleport(new Vector2(4.2f, 2));
                f.Player.Body.Velocity = new Vector2(2, 3);
                PlayerController.Snapshot detached = f.Player.CaptureState(); detached.Phase = PlayerPhase.Detached;
                f.Player.RestoreState(in detached);
                f.Flow.UpdatePlayerRegion(f.Player);
                Check(f.Flow.CurrentRegion == f.B && f.Respawn.Checkpoint == f.B.EntrySpawnPosition, "Region/checkpoint not synchronized");
                Check(f.Player.Phase == PlayerPhase.Detached && f.Player.Body.Velocity == new Vector2(2, 3), "Crossing reset player state");
                Check(f.Flow.HasDiscovered(f.Definition.Modules[1]) && WorldMapProgressStore.IsUnlocked(f.Definition, f.Definition.Modules[1]),
                    "Run discovery or selection unlock missing");
                Check(f.Flow.CompletedCount == 0, "Discovery counted as completion");
                Call(f.B.GetComponent<RegionRevealView>(), "UpdateFade", 1f); Call(outline, "LateUpdate");
                Check(mesh.sharedMesh.vertexCount > 0, "Discovered ghost outline stayed hidden");
                f.Player.Body.Motor.Teleport(new Vector2(3.95f, 2)); f.Flow.UpdatePlayerRegion(f.Player);
                Check(f.Flow.CurrentRegion == f.B, "Reverse boundary buffer was ignored");
            }
            using (var f = new Fixture())
            {
                f.World.transform.position = new Vector3(0, -120, 0);
                f.Player.TeleportJoined(new Vector2(5, -118)); Physics2D.SyncTransforms();
                f.Player.SimulateStep(Time.fixedDeltaTime);
                Check(f.Flow.CurrentRegion == f.B && !f.Player.Body.IsReturning, "Negative-coordinate room triggered global fall death");
            }
        }

        private static void Rewind()
        {
            using (var f = new Fixture())
            {
                f.Rewind.SimulateStep(Time.fixedDeltaTime);
                f.Player.TeleportJoined(new Vector2(5, 2)); f.Rewind.SimulateStep(Time.fixedDeltaTime);
                Check(f.Flow.CurrentRegion == f.B, "Forward simulation did not switch region");
                Check(f.Rewind.BeginRewind(), "Cannot rewind crossing"); f.Rewind.RewindBy(Time.fixedDeltaTime);
                Check(f.Flow.CurrentRegion == f.A && f.Rig.Region == f.A && f.Respawn.Checkpoint == f.A.EntrySpawnPosition,
                    "Rewind did not restore current region/checkpoint/camera");
                Check(f.Flow.HasDiscovered(f.Definition.Modules[1]) && f.B.GetComponent<RegionRevealView>().Discovered,
                    "Rewind removed discovery");
                f.Rewind.EndRewind(); f.Rewind.SimulateStep(Time.fixedDeltaTime);
            }
        }

        private static void DeathAndFlags()
        {
            using (var f = new Fixture())
            {
                CheckpointFlag decoy = Child("Decoy", f.Root.transform).AddComponent<CheckpointFlag>();
                decoy.transform.position = new Vector3(2, 2); decoy.SpawnOffset = new Vector2(99, 99);
                f.Player.TeleportJoined(new Vector2(2, 2)); Physics2D.SyncTransforms(); f.Player.SimulateStep(Time.fixedDeltaTime);
                Check(f.Respawn.Checkpoint == f.A.EntrySpawnPosition, "Touching flag selected a checkpoint");
                LethalZone hazard = Child("Hazard", f.Root.transform).AddComponent<LethalZone>();
                hazard.transform.position = new Vector3(5, 2); hazard.GetComponent<BoxCollider2D>().size = Vector2.one;
                f.Player.TeleportJoined(new Vector2(5, 2)); Physics2D.SyncTransforms(); f.Player.SimulateStep(Time.fixedDeltaTime);
                Check(f.Flow.CurrentRegion == f.B && f.Player.Body.IsReturning && f.Respawn.Journeys.Count == 1,
                    "Death on crossing did not start a return in target region");
                Check(f.Respawn.Journeys[0].Destination == f.B.EntrySpawnPosition, "Death used stale checkpoint");
                f.Player.Body.Motor.Teleport(new Vector2(2, 2)); f.Flow.UpdatePlayerRegion(f.Player);
                Check(f.Flow.CurrentRegion == f.B, "Return path changed region");
            }
        }

        private static void CameraTransition()
        {
            using (var f = new Fixture())
            {
                float before = f.Camera.transform.position.x;
                f.Player.TeleportJoined(new Vector2(5, 2)); f.Flow.UpdatePlayerRegion(f.Player);
                Near(f.Camera.transform.position.x, before, "SetRegion snapped camera");
                Call(f.Rig, "UpdateFollow", 1f / 60f, false);
                Check(f.Camera.transform.position.x > before && f.Camera.transform.position.x < 6f, "Transition did not move smoothly");
                Vector3 paused = f.Camera.transform.position; Call(f.Rig, "UpdateFollow", 0f, false);
                Check(f.Camera.transform.position == paused, "Paused camera moved");
                for (int i = 0; i < 160; i++) Call(f.Rig, "UpdateFollow", 1f / 60f, false);
                Near(f.Camera.transform.position.x, 6f, "Camera failed to settle");
                f.Rig.SetRegion(f.A, false); Call(f.Rig, "UpdateFollow", 1f / 60f, true);
                Near(f.Camera.transform.position.x, 2f, "Rewind camera failed to snap");
            }
        }

        private static void Fade()
        {
            using (var f = new Fixture())
            {
                RegionRevealView reveal = f.B.GetComponent<RegionRevealView>();
                SpriteRenderer sprite = f.B.GetComponentInChildren<SpriteRenderer>();
                f.Camera.transform.position = new Vector3(6, 2, -10); f.Camera.backgroundColor = Color.black;
                f.Camera.clearFlags = CameraClearFlags.SolidColor; f.Camera.orthographicSize = 1;
                var target = new RenderTexture(64, 64, 0); target.Create(); f.Camera.targetTexture = target;
                RenderTexture previous = RenderTexture.active;
                var pixels = new Texture2D(64, 64, TextureFormat.RGB24, false);
                try
                {
                    Color Read()
                    {
                        f.Camera.Render(); RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply(); return pixels.GetPixel(32, 32);
                    }
                    Check(Read().maxColorComponent < 0.05f, "Unvisited region leaked into render");
                    reveal.SetDiscovered(true); Call(reveal, "UpdateFade", 0.225f);
                    sprite.color = new Color(0, 1, 0, 0.4f); // Simulates an offscreen phase change during discovery.
                    Color halfway = Read();
                    Check(halfway.g > 0.1f && halfway.g < 0.8f && halfway.r < 0.05f, "Fade ignored live colors/opacity");
                    Call(reveal, "UpdateFade", 1f);
                    Check(!sprite.forceRenderingOff && sprite.color.a == 0.4f && Read().g > halfway.g, "Fade damaged mechanism tint");
                    reveal.SetDiscovered(true); Near(reveal.Opacity, 1f, "Repeated entry restarted fade");
                }
                finally
                {
                    f.Camera.targetTexture = null; RenderTexture.active = previous;
                    UnityEngine.Object.DestroyImmediate(pixels); target.Release(); UnityEngine.Object.DestroyImmediate(target);
                }
            }
        }

        private static void Square()
        {
            LevelBrush brush = LevelEditorAssets.EnsureSquarePlatformBrush();
            GameObject block = UnityEngine.Object.Instantiate(brush.Prefab);
            GameObject actor = new GameObject("Head collision probe");
            try
            {
                SquarePlatform platform = block.GetComponent<SquarePlatform>();
                BoxCollider2D box = block.GetComponent<BoxCollider2D>();
                Check(box.size == Vector2.one && box.offset == Vector2.zero && brush.PreviewRect == new Rect(0, 0, 1, 1), "Square geometry incomplete");
                Check(block.GetComponent<WorldSurface>().Kind == SurfaceKind.OneWay, "Square is not a platform");
                MovableEntity head = actor.AddComponent<MovableEntity>(); head.Configure(EntityPart.Head, false, 24);
                head.GetComponent<BoxCollider2D>().size = Vector2.one * 0.6f;
                head.Motor.Teleport(new Vector2(0, -1.5f)); Physics2D.SyncTransforms();
                Check(!head.Motor.Sweep(Vector2.up * 3).Blocked, "Square blocked upward head movement");
                head.Motor.Teleport(new Vector2(0, 1.5f)); Physics2D.SyncTransforms();
                SweepResult landing = head.Motor.Sweep(Vector2.down * 3);
                Check(landing.Blocked && landing.Normal.y > 0.5f, "Square did not support falling head");
                int passed = 0, landed = 0;
                platform.HeadPassedUpward += entity => passed++; platform.HeadLanded += entity => landed++;
                platform.NotifyHeadPassedUpward(head); platform.NotifyHeadLanded(head);
                head.Part = EntityPart.Body; platform.NotifyHeadLanded(head);
                Check(passed == 1 && landed == 1, "Animation interfaces failed to filter body contacts");
            }
            finally { UnityEngine.Object.DestroyImmediate(actor); UnityEngine.Object.DestroyImmediate(block); }
        }
    }
}
