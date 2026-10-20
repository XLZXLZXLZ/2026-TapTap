using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Hints.Editor
{
    // Run in an isolated COPY of the project. This harness never opens or saves an authored scene.
    [InitializeOnLoad]
    public static class HintReplayRegressionTests
    {
        private const string Pending = "TapTap.Hints.Regression";
        private const string Folder = "Assets/HintReplayValidation";
        private static string Output => Path.GetFullPath("Logs/HintReplays");
        private static int step, lastFrame;
        private static double started, deadline;
        private static float pausedTime;
        private static PlayerController player;
        private static HintReplayRecorder recorder;
        private static HintReplayDirector director;
        private static HintReplayPlayer playback;
        private static HintReplayClip clipA, clipB;
        private static HintReplayReturnTrigger triggerA, triggerB;
        private static LevelRegion region;
        private static WorldMapProgression map;
        private static float deathRecordingTime;
        private static Vector3 worldRecordingStart;

        static HintReplayRegressionTests()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Pending, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    step = 0; lastFrame = -1;
                    deadline = EditorApplication.timeSinceStartup + 90;
                    started = EditorApplication.timeSinceStartup;
                    EditorApplication.update += Tick;
                }
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Pending + ".Saved", false))
                {
                    try
                    {
                        var saved = AssetDatabase.LoadAssetAtPath<HintReplayClip>(Folder + "/Recorded.asset");
                        Check(saved != null && saved.Validate(out _) && saved.Duration > 0.1f, "Saved recording survives leaving Play Mode");
                        var draft = ScriptableObject.CreateInstance<HintReplayClip>();
                        EditorJsonUtility.FromJsonOverwrite(File.ReadAllText("Library/HintReplay/RecordingDraft.json"), draft);
                        Check(draft.Validate(out _) && Mathf.Abs(draft.Duration - saved.Duration) < 0.001f, "Editor draft survives domain reload and leaving Play Mode");
                        var world = AssetDatabase.LoadAssetAtPath<HintReplayClip>(Folder + "/WorldReturn.asset");
                        Check(world != null && world.UsesWorldSpace && world.Validate(out _), "World-space return metadata survives saving and leaving Play Mode");
                        Object.DestroyImmediate(draft);
                        SessionState.SetBool(Pending, false);
                        File.AppendAllText(Path.Combine(Output, "results.txt"), "PASS: all hint replay checks completed\n");
                        EditorApplication.Exit(0);
                    }
                    catch (Exception error) { Fail(error); }
                }
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode copy of this project.");
            try
            {
                Directory.CreateDirectory(Output);
                File.WriteAllText(Path.Combine(Output, "results.txt"), "");
                Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Player.prefab");
                Check(prefab != null, "Real player prefab exists");
                var character = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                character.GetComponent<PlayerInput>().UseKeyboard = false;
                character.GetComponent<PlayerController>().TeleportJoined(new Vector2(2f, 2f));
                var floor = new GameObject("Test Floor");
                floor.transform.position = new Vector3(20f, -0.5f, 0f);
                floor.AddComponent<BoxCollider2D>().size = new Vector2(200f, 1f);
                floor.AddComponent<WorldSurface>();
                var level = ScriptableObject.CreateInstance<LevelDefinition>();
                AssetDatabase.CreateAsset(level, Folder + "/Level.asset");
                var levelRegion = new GameObject("Test Region").AddComponent<LevelRegion>();
                levelRegion.Source = level;
                var owner = new GameObject("Hint Replay System");
                owner.AddComponent<HintReplayDirector>().UseKeyboard = false;
                owner.AddComponent<HintReplayRecorder>();
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Folder + "/Flow.unity");
                SessionState.SetBool(Pending + ".Saved", false);
                SessionState.SetBool(Pending, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Fail(error); }
        }

        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || Time.frameCount == lastFrame || Time.frameCount < 5) return;
                lastFrame = Time.frameCount;
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Hint replay tests timed out at step " + step);
                double elapsed = EditorApplication.timeSinceStartup - started;
                if (step == 0)
                {
                    player = Object.FindObjectOfType<PlayerController>();
                    recorder = Object.FindObjectOfType<HintReplayRecorder>();
                    director = Object.FindObjectOfType<HintReplayDirector>();
                    playback = director.GetComponent<HintReplayPlayer>();
                    region = Object.FindObjectOfType<LevelRegion>();
                    Check(player != null && player.Body.Visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0,
                        "Tests use the actual skinned player model");
                    if (RewindManager.Current != null) RewindManager.Current.UseKeyboard = false;
                    recorder.Player = player; recorder.ReferenceFrame = region.transform;
                    Check(recorder.BeginRecording(), "Recording begins without changing the player");
                    player.GetComponent<PlayerInput>().SetExternalInput(1f, true);
                    Next();
                }
                else if (step == 1 && elapsed > 0.7)
                {
                    pausedTime = recorder.Duration; Time.timeScale = 0f; Next();
                }
                else if (step == 2 && elapsed > 0.15)
                {
                    Check(Mathf.Abs(pausedTime - recorder.Duration) < 0.001f, "Recording excludes paused time");
                    Time.timeScale = 1f;
                    player.GetComponent<PlayerInput>().SetExternalInput(0f, false); Next();
                }
                else if (step == 3 && elapsed > 0.6)
                {
                    recorder.StopRecording(); clipA = recorder.CreateClip();
                    Check(clipA != null && clipA.Validate(out _), "Recording produces a valid pose timeline");
                    Check(clipA.Frames.Any(frame => frame.Connected), "Timeline includes head/body separation and its connection");
                    Check(clipA.Frames.Any(frame => frame.SpaceHeld && frame.Horizontal > 0f), "Timeline observes inputs without consuming them");
                    Check(Enumerable.Range(0, clipA.Nodes.Length).Where(index => clipA.Nodes[index].Parent >= 0)
                        .Any(index => clipA.Frames.Any(frame => Quaternion.Angle(frame.Poses[index].Rotation, clipA.Frames[0].Poses[index].Rotation) > 0.1f)),
                        "Timeline captures animated child and bone poses");
                    AssetDatabase.CreateAsset(clipA, Folder + "/Recorded.asset"); AssetDatabase.SaveAssets();
                    StoreWindowDraft();
                    clipB = Object.Instantiate(clipA); clipB.name = "Second Hint";
                    ValidateDataAndGhost();
                    var catalog = ScriptableObject.CreateInstance<HintReplayCatalog>();
                    catalog.Entries.Add(new HintReplayCatalog.Entry { Level = region.Source, Clip = clipA });
                    catalog.Entries.Add(new HintReplayCatalog.Entry { Level = region.Source, ModuleId = "override", Clip = clipB });
                    Check(catalog.Resolve(region.Source, "override") == clipB && catalog.Resolve(region.Source, "other") == clipA,
                        "Module-specific entries override the normal level mapping");
                    director.Catalog = catalog;
                    Check(director.CurrentClip == clipA && director.RequestHint(), "Forward flow resolves and plays the current level");
                    GameObject previous = playback.GhostRoot;
                    Check(director.RequestHint() && !previous.activeSelf, "Repeated requests replace the prior ghost immediately");
                    pausedTime = playback.Playhead; Time.timeScale = 0f; Next();
                }
                else if (step == 4 && elapsed > 0.15)
                {
                    Check(playback.IsPlaying && Mathf.Abs(playback.Playhead - pausedTime) < 0.001f, "Playback freezes during pause");
                    Time.timeScale = 1f; Next();
                }
                else if (step == 5 && elapsed > 0.15)
                {
                    Check(playback.Playhead > pausedTime, "Playback resumes after pause");
                    Check(recorder.BeginRecording(), "A new recording can replace a previous take");
                    Check(playback.TryPlay(clipA, region.transform, player.Body.Visual, player.Head.Visual), "Low-level replay is active for the rewind interruption check");
                    // Keep the first draft while exercising death as an intentional part of a solution.
                    player.GetComponent<PlayerInput>().SetExternalInput(0f, false);
                    deathRecordingTime = recorder.Duration;
                    player.GetComponent<RespawnService>().BeginSingleReturn(player.Head);
                    GoTo(14);
                }
                else if (step == 6 && elapsed > 0.25)
                {
                    Check(RewindManager.Current != null && RewindManager.Current.BeginRewind(), "Existing Z rewind can start during recording");
                    Next();
                }
                else if (step == 7 && elapsed > 0.05)
                {
                    Check(!recorder.IsRecording && recorder.CreateClip() != null, "Z rewind ends recording and retains the take");
                    Check(!playback.IsPlaying, "Z rewind removes playback");
                    RewindManager.Current.EndRewind();
                    player.GetComponent<PlayerInput>().SetExternalInput(0f, false);
                    BuildWorld();
                    Check(director.CurrentClip == clipA && director.RequestHint(), "World flow uses the selected module");
                    var restored = new WorldMapProgression.Snapshot { CurrentIndex = 1 };
                    map.RestoreState(in restored);
                    Check(director.CurrentClip == clipB && !playback.IsPlaying, "Silent rewind restoration selects the correct level hint");
                    restored.CurrentIndex = 0; map.RestoreState(in restored);
                    triggerA = CreateTrigger("Return A", clipA, new Vector3(-100f, 0f, 0f));
                    triggerB = CreateTrigger("Return B", clipB, new Vector3(100f, 0f, 0f));
                    Check(!triggerA.IsArmed && !triggerA.GetComponent<BoxCollider2D>().enabled, "Return triggers have no active collider in forward flow");
                    Scene other = SceneManager.CreateScene("Other Hint Scene");
                    EventManager.Publish(new GameStageChanged(other, GameStage.Outbound, GameStage.Returning));
                    Check(!triggerA.IsArmed && director.CurrentClip == clipA, "Stage notifications from other scenes are ignored");
                    Check(GameStageProgression.ForScene(player.gameObject.scene).BeginReturn(), "Actual return-stage transition succeeds");
                    Check(triggerA.IsArmed && director.CurrentClip == null, "Entering return arms triggers and clears the forward hint");
                    triggerA.transform.position = player.Body.Motor.Position + player.Body.Motor.CenterOffset;
                    Next();
                }
                else if (step == 8 && elapsed > 0.1)
                {
                    Check(director.CurrentClip == clipA && !playback.IsPlaying, "Touching a return trigger selects its clip without autoplay");
                    Check(director.RequestHint(), "Player can request the selected return hint");
                    triggerA.transform.position = new Vector3(-100f, 0f, 0f);
                    triggerB.transform.position = player.Body.Motor.Position + player.Body.Motor.CenterOffset;
                    Next();
                }
                else if (step == 9 && elapsed > 0.1)
                {
                    Check(director.CurrentClip == clipB && !playback.IsPlaying, "A different trigger replaces the hint and stops the previous playback");
                    triggerB.transform.position = new Vector3(100f, 0f, 0f);
                    triggerA.transform.position = player.Body.Motor.Position + player.Body.Motor.CenterOffset;
                    Next();
                }
                else if (step == 10 && elapsed > 0.1)
                {
                    Check(director.CurrentClip == clipA, "Re-entering an earlier trigger restores its hint");
                    triggerA.transform.position = new Vector3(-100f, 0f, 0f);
                    var late = CreateTrigger("Late Return", clipB, player.Body.Motor.Position + player.Body.Motor.CenterOffset);
                    Check(late.IsArmed && late.GetComponent<BoxCollider2D>().enabled, "Triggers loaded after entering return are armed immediately");
                    ValidateSweptBox(); Next();
                }
                else if (step == 11 && elapsed > 0.1)
                {
                    Check(director.CurrentClip == clipB && director.RequestHint(), "Late-loaded trigger selects its preset");
                    GameplayActionLocks.Acquire(director, GameplayAction.PlayerControls); Next();
                }
                else if (step == 12 && elapsed > 0.05)
                {
                    Check(!playback.IsPlaying && !director.RequestHint(), "Transition/control locks stop and block hints");
                    GameplayActionLocks.Release(director);
                    Check(director.RequestHint(), "Playback works after transition locks are released");
                    playback.Seek(clipB.Duration); Next();
                }
                else if (step == 13 && elapsed > 0.05)
                {
                    Check(!playback.IsPlaying, "Playback ends and disposes its ghost");
                    recorder.ReferenceFrame = triggerA.transform;
                    worldRecordingStart = player.Body.Visual.position;
                    Check(recorder.BeginRecording() && recorder.RecordsWorldSpace && recorder.RecordingReferenceFrame == null,
                        "Return recording automatically uses world coordinates despite a configured reference point");
                    player.GetComponent<PlayerInput>().SetExternalInput(1f, false);
                    GoTo(23);
                }
                else if (step == 14 && elapsed > 0.08)
                {
                    Check(player.Head.IsReturning && recorder.IsRecording && recorder.Duration > deathRecordingTime,
                        "Head-only death keeps recording and advancing its timeline");
                    Check(playback.IsPlaying && playback.Playhead > 0f, "The real player's death does not pause an existing ghost");
                    Next();
                }
                else if (step == 15 && !player.Head.IsReturning)
                {
                    Check(recorder.IsRecording, "Recording continues after head respawn");
                    player.GetComponent<RespawnService>().BeginSingleReturn(player.Body);
                    Next();
                }
                else if (step == 16 && elapsed > 0.08)
                {
                    Check(player.Body.IsReturning && recorder.IsRecording, "Body-only death keeps recording");
                    Next();
                }
                else if (step == 17 && !player.Body.IsReturning)
                {
                    Check(recorder.IsRecording, "Recording continues after body respawn");
                    player.GetComponent<RespawnService>().BeginGroupReturn(player.Body, player.Head, player.Config.ToWorld(player.Config.JoinedOffset));
                    Next();
                }
                else if (step == 18 && elapsed > 0.08)
                {
                    Check(player.Body.IsReturning && player.Head.IsReturning && recorder.IsRecording, "Paired death keeps both parts on the same recording");
                    Next();
                }
                else if (step == 19 && !player.Body.IsReturning && !player.Head.IsReturning)
                {
                    Check(recorder.IsRecording, "Recording continues after paired respawn");
                    player.GetComponent<PlayerInput>().SetExternalInput(1f, false);
                    Next();
                }
                else if (step == 20 && elapsed > 0.2)
                {
                    Check(recorder.IsRecording && recorder.Duration > deathRecordingTime + 0.5f,
                        "Death and subsequent actions remain on one uninterrupted timeline");
                    recorder.StopRecording();
                    HintReplayClip deathClip = recorder.CreateClip();
                    ValidateDeathClip(deathClip);
                    AssetDatabase.CreateAsset(deathClip, Folder + "/Death.asset"); AssetDatabase.SaveAssets();
                    player.GetComponent<PlayerInput>().SetExternalInput(0f, false);
                    player.GetComponent<RespawnService>().BeginGroupReturn(player.Body, player.Head, player.Config.ToWorld(player.Config.JoinedOffset));
                    Next();
                }
                else if (step == 21 && elapsed > 0.15)
                {
                    Check(player.Body.IsReturning && director.RequestHint(), "Hints can be requested while the real player is respawning");
                    Check(recorder.BeginRecording(), "Recording can start in the middle of a respawn");
                    Next();
                }
                else if (step == 22 && !player.Body.IsReturning && !player.Head.IsReturning)
                {
                    Check(recorder.IsRecording && recorder.FrameCount > 1, "Recording started during respawn continues afterwards");
                    Check(playback.TryPlay(clipA, region.transform, player.Body.Visual, player.Head.Visual), "Replay is active for the rewind interruption check after respawn");
                    player.GetComponent<PlayerInput>().SetExternalInput(-1f, false);
                    GoTo(6);
                }
                else if (step == 23 && elapsed > 0.2)
                {
                    recorder.StopRecording();
                    HintReplayClip world = recorder.CreateClip();
                    Check(world != null && world.UsesWorldSpace && world.Validate(out _), "Return recording produces a valid world-space clip");
                    Check(Vector3.Distance(world.Frames[0].Poses[0].Position, worldRecordingStart) < 0.001f,
                        "Return poses store the actual recorded world position");
                    AssetDatabase.CreateAsset(world, Folder + "/WorldReturn.asset"); AssetDatabase.SaveAssets();
                    ValidateWorldSpaceReturn(world);
                    SessionState.SetBool(Pending + ".Saved", true);
                    EditorApplication.update -= Tick;
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception error) { Fail(error); }
        }

        private static void ValidateDataAndGhost()
        {
            var copy = ScriptableObject.CreateInstance<HintReplayClip>();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(clipA), copy);
            Check(copy.Validate(out _) && copy.Nodes.Length == clipA.Nodes.Length, "Recording serializes without runtime object references");
            copy.Sample(copy.Duration * 0.51f, out HintReplayFrame from, out HintReplayFrame to, out float blend);
            Check(from.Time <= copy.Duration * 0.51f && to.Time >= copy.Duration * 0.51f && blend >= 0f && blend <= 1f,
                "Timeline sampling brackets the requested time");
            var invalid = ScriptableObject.CreateInstance<HintReplayClip>();
            invalid.SetRecording(copy.Nodes, new[] { copy.Frames[0], copy.Frames[0] });
            Check(!invalid.Validate(out _), "Malformed or zero-duration timelines are rejected");
            int entities = MovableEntity.ActiveEntities.Count;
            Vector2 bodyPosition = player.Body.Motor.Position;
            PlayerPhase phase = player.Phase;
            Check(playback.TryPlay(copy, region.transform, player.Body.Visual, player.Head.Visual), "Pose replay accepts the actual player hierarchy");
            playback.Seek(copy.Duration * 0.51f);
            GameObject ghost = playback.GhostRoot;
            Check(ghost.GetComponentsInChildren<MonoBehaviour>(true).Length == 0
                && ghost.GetComponentsInChildren<Collider2D>(true).Length == 0 && ghost.GetComponentsInChildren<Rigidbody2D>(true).Length == 0
                && ghost.GetComponentsInChildren<Collider>(true).Length == 0 && ghost.GetComponentsInChildren<Rigidbody>(true).Length == 0
                && ghost.GetComponentsInChildren<Animator>(true).Length == 0 && ghost.GetComponentsInChildren<AudioSource>(true).Length == 0
                && ghost.GetComponentsInChildren<Light>(true).Length == 0, "Ghost contains rendering components only");
            Check(MovableEntity.ActiveEntities.Count == entities && player.Body.Motor.Position == bodyPosition && player.Phase == phase,
                "Creating and seeking a ghost does not change the real simulation");
            Transform body = ghost.transform.GetChild(0);
            Vector3 expected = region.transform.TransformPoint(Vector3.Lerp(from.Poses[0].Position, to.Poses[0].Position, blend));
            Check(Vector3.Distance(body.position, expected) < 0.001f, "Root motion uses the recording reference frame");
            var mesh = new Mesh();
            foreach (SkinnedMeshRenderer skin in ghost.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Check(skin.bones.All(bone => bone == null || bone.IsChildOf(ghost.transform)), "Every ghost bone belongs to the isolated hierarchy");
                skin.BakeMesh(mesh);
                Check(mesh.vertexCount > 0, "Recorded bone poses can skin the real character mesh");
            }
            Object.DestroyImmediate(mesh);
            Shader shader = Resources.Load<Shader>("HintReplayGhost");
            Check(shader != null && !ShaderUtil.ShaderHasError(shader), "Bundled ghost shader imports without errors");
            RenderGhost(ghost);
            playback.Stop();
            Vector3 original = region.transform.position;
            region.transform.position += Vector3.right * 20f;
            Check(playback.TryPlay(copy, region.transform, player.Body.Visual, player.Head.Visual), "Moved level accepts its recorded clip");
            playback.Seek(copy.Duration * 0.51f);
            Check(Vector3.Distance(playback.GhostRoot.transform.GetChild(0).position, expected + Vector3.right * 20f) < 0.001f,
                "Moving a level relocates the entire replay consistently");
            playback.Stop(); region.transform.position = original;
            Object.DestroyImmediate(copy); Object.DestroyImmediate(invalid);
        }

        private static void StoreWindowDraft()
        {
            var window = ScriptableObject.CreateInstance<HintReplayWindow>();
            typeof(HintReplayWindow).GetField("recorder", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, recorder);
            typeof(HintReplayWindow).GetMethod("StoreDraft", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
            Object.DestroyImmediate(window);
            Check(File.Exists("Library/HintReplay/RecordingDraft.json"), "Recorder window writes a persistent editor draft");
        }

        private static void ValidateDeathClip(HintReplayClip recording)
        {
            Check(recording != null && recording.Validate(out _), "A recording containing all three respawn cases remains valid");
            int[] roots = Enumerable.Range(0, recording.Nodes.Length).Where(index => recording.Nodes[index].Parent < 0).ToArray();
            foreach (int index in roots)
            {
                float initialScale = recording.Frames[0].Poses[index].Scale.magnitude;
                Check(recording.Frames.Any(frame => frame.Poses[index].Scale.magnitude < initialScale * 0.25f), "Death collapse is recorded for root " + index);
                Check(recording.Frames[recording.Frames.Length - 1].Poses[index].Scale.magnitude > initialScale * 0.5f,
                    "Respawn rebuild is recorded for root " + index);
            }
            HintReplayFrame collapsed = recording.Frames.First(frame => frame.Poses[0].Scale.magnitude < recording.Frames[0].Poses[0].Scale.magnitude * 0.25f);
            Vector2 realPosition = player.Body.Motor.Position;
            Check(playback.TryPlay(recording, region.transform, player.Body.Visual, player.Head.Visual), "A death-containing recording plays as an isolated ghost");
            playback.Seek(collapsed.Time);
            Check(Vector3.Distance(playback.GhostRoot.transform.GetChild(0).localScale, collapsed.Poses[0].Scale) < 0.001f
                && player.Body.Motor.Position == realPosition, "Seeking the death pose reproduces collapse without respawning the real player");
            playback.Seek(recording.Duration);
            Check(playback.GhostRoot.transform.GetChild(0).localScale.magnitude > recording.Frames[0].Poses[0].Scale.magnitude * 0.5f,
                "The same ghost rebuilds and reaches the post-respawn pose");
            playback.Stop();
        }

        private static void ValidateWorldSpaceReturn(HintReplayClip world)
        {
            triggerA.Clip = world;
            Check(director.SelectReturnHint(triggerA) && director.CurrentClip == world && director.CurrentReferenceFrame == null,
                "A world-space return trigger selects the clip with no playback anchor");
            Check(director.RequestHint(), "A world-space return hint can be requested normally");
            playback.Seek(world.Duration * 0.5f);
            world.Sample(world.Duration * 0.5f, out HintReplayFrame from, out HintReplayFrame to, out float blend);
            Vector3 expected = Vector3.Lerp(from.Poses[0].Position, to.Poses[0].Position, blend);
            GameObject ghost = playback.GhostRoot;
            Check(ghost.transform.parent == null && Vector3.Distance(ghost.transform.GetChild(0).position, expected) < 0.001f,
                "World-space ghost renders at recorded positions without parenting to a trigger");
            triggerA.transform.position += new Vector3(30f, 10f, 0f);
            triggerA.transform.rotation = Quaternion.Euler(0f, 0f, 40f);
            triggerA.transform.localScale = Vector3.one * 3f;
            Check(director.CurrentClip == world && playback.GhostRoot == ghost
                && Vector3.Distance(ghost.transform.GetChild(0).position, expected) < 0.001f,
                "Moving rotating or scaling the trigger does not move or stop a world-space replay");
            Check(playback.TryPlay(world, triggerA.transform, player.Body.Visual, player.Head.Visual),
                "Low-level preview accepts a world-space clip even when an old reference is supplied");
            playback.Seek(world.Duration * 0.5f);
            Check(Vector3.Distance(playback.GhostRoot.transform.GetChild(0).position, expected) < 0.001f,
                "World-space previews ignore the supplied reference transform");
            Check(director.SelectReturnHint(triggerB) && director.CurrentReferenceFrame == region.transform && director.RequestHint(),
                "Existing relative return recordings retain their authored reference-frame behavior");
            playback.Stop();
        }

        private static void BuildWorld()
        {
            var source = ScriptableObject.CreateInstance<WorldMapDefinition>();
            source.name = "HintReplayValidation"; source.ProgressId = Guid.NewGuid().ToString("N");
            var secondLevel = Object.Instantiate(region.Source);
            source.Modules.Add(new WorldMapModule { Level = region.Source });
            source.Modules.Add(new WorldMapModule { Level = secondLevel, Position = new Vector2Int(50, 0) });
            var owner = new GameObject("Test World"); owner.SetActive(false);
            var root = owner.AddComponent<WorldMapRoot>(); root.Source = source;
            var generated = new GameObject("Generated"); generated.transform.SetParent(owner.transform, false);
            region.transform.SetParent(generated.transform, false); region.WorldModuleId = source.Modules[0].Id;
            var second = new GameObject("Second Region").AddComponent<LevelRegion>();
            second.transform.SetParent(generated.transform, false); second.transform.localPosition = new Vector3(50f, 0f, 0f);
            second.Source = secondLevel; second.WorldModuleId = source.Modules[1].Id;
            owner.SetActive(true);
            map = owner.GetComponent<WorldMapProgression>();
            if (map == null) map = owner.AddComponent<WorldMapProgression>();
            map.UseKeyboard = false;
            Check(map.Initialize(root, player, null), "World progression initializes using its normal API");
            director.Catalog.Entries.Add(new HintReplayCatalog.Entry { Level = secondLevel, Clip = clipB });
        }

        private static HintReplayReturnTrigger CreateTrigger(string name, HintReplayClip recording, Vector3 position)
        {
            var owner = new GameObject(name); owner.SetActive(false); owner.transform.position = position;
            var trigger = owner.AddComponent<HintReplayReturnTrigger>();
            trigger.Clip = recording; trigger.Player = player; trigger.Director = director; trigger.PlaybackOrigin = region.transform;
            trigger.GetComponent<BoxCollider2D>().size = new Vector2(2f, 3f);
            owner.SetActive(true); return trigger;
        }

        private static void ValidateSweptBox()
        {
            var trigger = CreateTrigger("Sweep Test", clipA, Vector3.zero);
            trigger.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            trigger.GetComponent<BoxCollider2D>().size = new Vector2(0.2f, 4f);
            MethodInfo method = typeof(HintReplayReturnTrigger).GetMethod("Intersects", BindingFlags.Instance | BindingFlags.NonPublic);
            bool crossed = (bool)method.Invoke(trigger, new object[] { new Vector2(-3f, 0f), new Vector2(3f, 0f), new Vector2(0.1f, 0.1f) });
            bool outside = (bool)method.Invoke(trigger, new object[] { new Vector2(1.4f, 1.4f), new Vector2(1.4f, 1.4f), new Vector2(0.1f, 0.1f) });
            Check(crossed && !outside, "Swept trigger geometry handles fast crossings and rejects rotated-box AABB false positives");
            Object.DestroyImmediate(trigger.gameObject);
        }

        private static void RenderGhost(GameObject ghost)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var owner = new GameObject("Hint Capture Camera");
            var camera = owner.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.025f, 0.04f, 0.07f);
            var bounds = new Bounds(ghost.transform.GetChild(0).position, Vector3.zero);
            foreach (Renderer renderer in ghost.GetComponentsInChildren<Renderer>())
                if (renderer.enabled) bounds.Encapsulate(renderer.bounds);
            foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>())
                if (renderer.enabled && !(renderer is LineRenderer) && !(renderer is ParticleSystemRenderer)) bounds.Encapsulate(renderer.bounds);
            camera.orthographicSize = Mathf.Max(2f, bounds.extents.y * 1.2f, bounds.extents.x * 0.9f) + 0.5f;
            owner.transform.position = new Vector3(bounds.center.x, bounds.center.y, -15f);
            var target = new RenderTexture(800, 600, 24); target.Create(); camera.targetTexture = target;
            camera.Render();
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(800, 600, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 800, 600), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output, "ghost-preview.png"), image.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null; target.Release();
            Object.DestroyImmediate(target); Object.DestroyImmediate(image); Object.DestroyImmediate(owner);
        }

        private static void Next() { step++; started = EditorApplication.timeSinceStartup; }
        private static void GoTo(int next) { step = next; started = EditorApplication.timeSinceStartup; }
        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            File.AppendAllText(Path.Combine(Output, "results.txt"), "PASS: " + description + "\n");
        }
        private static void Fail(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Pending, false);
            Directory.CreateDirectory(Output); File.AppendAllText(Path.Combine(Output, "results.txt"), "FAIL at step " + step + ": " + error + "\n");
            Debug.LogException(error); EditorApplication.Exit(1);
        }
    }
}
