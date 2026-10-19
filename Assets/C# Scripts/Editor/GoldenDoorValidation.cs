using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    [InitializeOnLoad]
    public static class GoldenDoorValidation
    {
        private const string Pending = "TapTap.GoldenDoorValidation";
        private static string Output => Path.GetFullPath("Logs/GoldenDoor");
        private static int step, frame, openedEvents, sceneHandle;
        private static double deadline, since;
        private static float pausedProgress;
        private static Vector3 pausedKey, keyScale;

        static GoldenDoorValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
                step = frame = openedEvents = 0;
                deadline = EditorApplication.timeSinceStartup + 60;
                EditorApplication.update += Tick;
            };
        }

        public static void CreatePreview()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GoldenDoorAssets.PreviewPath) != null)
            {
                EditorSceneManager.OpenScene(GoldenDoorAssets.PreviewPath);
                return;
            }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var door = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ReturnJourneyAssets.DoorPath)))
                .GetComponent<ReturnJourneyDoor>();
            door.transform.position = new Vector3(0f, 0.5f, 0f);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Player.prefab"));
            player.transform.position = new Vector3(4f, 0.6f, 0f);
            var key = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ReturnJourneyAssets.KeyPath));
            key.transform.position = new Vector3(2.6f, 0.7f, 0f);
            var trigger = new GameObject("Begin Return (preview only)").AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(12f, 5f);
            trigger.gameObject.AddComponent<ReturnJourneyTrigger>().Cue = ReturnJourneyCue.BeginReturn;
            new GameObject("Progress").AddComponent<GameStageProgression>();
            var floor = new GameObject("Floor");
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.AddComponent<BoxCollider2D>().size = new Vector2(14f, 1f);
            floor.AddComponent<WorldSurface>();
            GameObject brick = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Terrain/Grid/Styles/Models/GridBrickModel_Region3_Yellow.prefab");
            for (int i = -6; i <= 6; i++)
            {
                var block = (GameObject)PrefabUtility.InstantiatePrefab(brick);
                block.transform.SetParent(floor.transform, false);
                block.transform.localPosition = new Vector3(i, 0f, 0f);
            }
            for (int i = 0; i < 3; i++)
            {
                var block = (GameObject)PrefabUtility.InstantiatePrefab(brick);
                block.transform.position = new Vector3(-1f, 0.5f + i, 0f);
            }
            Camera camera = new GameObject("Door Preview Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(4.5f, 3.4f, -12f);
            camera.transform.LookAt(new Vector3(0.2f, 1.3f, 0.5f));
            camera.orthographic = true;
            camera.orthographicSize = 3.2f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.075f, 0.095f);
            camera.allowHDR = true;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            camera.gameObject.AddComponent<AudioListener>();
            var light = new GameObject("Preview Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.8f;
            light.transform.rotation = Quaternion.Euler(40f, -25f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.gray * 0.8f;
            string volumePath = GoldenDoorAssets.MaterialFolder + "/GoldenDoorPreviewVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(volumePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                var bloom = profile.Add<Bloom>();
                bloom.intensity.Override(0.5f);
                bloom.threshold.Override(1f);
                bloom.scatter.Override(0.65f);
                AssetDatabase.CreateAsset(profile, volumePath);
                AssetDatabase.AddObjectToAsset(bloom, profile);
            }
            var volume = new GameObject("Golden edge Bloom (preview only)").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, GoldenDoorAssets.PreviewPath);
        }

        public static void ValidateAndCapture()
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "asset-results.txt"), "");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(GoldenDoorAssets.ShaderPath);
            Check(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Unlit dissolve shader did not compile");
            var door = Object.FindObjectOfType<ReturnJourneyDoor>();
            var barrier = door.GetComponent<BoxCollider2D>();
            Check(barrier.size == new Vector2(1, 3) && barrier.offset == Vector2.up, "Door footprint / bottom-cell anchor is incorrect");
            Check(door.KeySocket.localPosition == new Vector3(0, 1, -0.08f), "Key target is not at central lock");
            Bounds bounds = new Bounds();
            bool first = true;
            foreach (Renderer renderer in door.DoorVisual.Renderers)
            {
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
                Check(renderer.sharedMaterial.shader == shader, "Door uses a lit material");
                Check(renderer.shadowCastingMode == ShadowCastingMode.Off && !renderer.receiveShadows, "Door uses lighting shadows");
                Check(renderer.GetComponent<MeshFilter>().sharedMesh.colors32.Length > 0, "Baked face colours missing");
            }
            Check(Mathf.Abs(bounds.size.x - 1) < 0.02f && Mathf.Abs(bounds.size.y - 3) < 0.02f && bounds.size.z < 1.02f,
                "FBX dimensions / axes are incorrect: " + bounds);
            Check(bounds.min.z >= -0.04f && bounds.max.z > 0.9f, "Door front must face the camera at Z=0: " + bounds);
            Camera camera = Camera.main;
            Object.FindObjectOfType<PlayerController>().gameObject.SetActive(false);
            Object.FindObjectOfType<ProgressKey>().gameObject.SetActive(false);
            camera.orthographicSize = 2.25f;
            camera.transform.position = new Vector3(3.5f, 2.7f, -11f);
            camera.transform.LookAt(new Vector3(-0.3f, 1.45f, 0.45f));
            door.DoorVisual.ResetClosed();
            Capture(camera, "door-closed");
            door.DoorVisual.SetProgress(0.35f);
            Capture(camera, "door-dissolve-35");
            door.DoorVisual.SetProgress(0.7f);
            Capture(camera, "door-dissolve-70");
            door.DoorVisual.SetProgress(1f);
            Check(door.DoorVisual.Renderers.All(r => !r.enabled), "Dissolve endpoint leaves geometry behind");
            Capture(camera, "door-open");
            door.DoorVisual.ResetClosed();
            // Isolate the door, then verify a 100x light-intensity change cannot alter its pixels.
            foreach (Transform item in door.GetComponentsInChildren<Transform>()) item.gameObject.layer = 30;
            int mask = camera.cullingMask;
            camera.cullingMask = 1 << 30;
            Light light = Object.FindObjectOfType<Light>();
            light.intensity = 0f;
            byte[] dark = Capture(camera, "unlit-zero-light");
            light.intensity = 100f;
            byte[] bright = Capture(camera, "unlit-bright-light");
            Check(dark.SequenceEqual(bright), "Door rendering changes with scene lights");
            camera.cullingMask = mask;
            Check(!ShaderUtil.ShaderHasError(shader), "Shader errors after rendering");
            File.AppendAllText(Path.Combine(Output, "asset-results.txt"),
                "PASS: FBX 1x3 footprint, bottom-cell anchor, central lock target, baked colours, Unlit-only materials, no shadows.\n"
                + "PASS: Shader compilation and actual URP render at 0%, 35%, 70%, 100% dissolve.\n"
                + "PASS: Door pixels identical at directional light intensity 0 and 100.\n");
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            try
            {
                Directory.CreateDirectory(Output);
                File.WriteAllText(Path.Combine(Output, "play-results.txt"), "");
                EditorSceneManager.OpenScene(GoldenDoorAssets.PreviewPath);
                SessionState.SetBool(Pending, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Fail(error); }
        }

        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || Time.frameCount == frame || Time.frameCount < 5) return;
                frame = Time.frameCount;
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Door test step " + step);
                var door = Object.FindObjectOfType<ReturnJourneyDoor>();
                var key = Object.FindObjectOfType<ProgressKey>();
                var player = Object.FindObjectOfType<PlayerController>();
                var stage = GameStageProgression.ForScene(door.gameObject.scene);
                player.GetComponent<PlayerInput>().UseKeyboard = false;
                RewindManager.Current.UseKeyboard = false;
                door.Player = player;
                door.Proximity.enabled = false;
                double elapsed = EditorApplication.timeSinceStartup - since;
                if (step == 0)
                {
                    Check(!door.TryOpen(), "Keyless door opens");
                    if (!stage.HasProgressed) Check(stage.BeginReturn(), "Cannot enter return");
                    Check(key.TryCollect(player), "Cannot collect key");
                    Next();
                }
                else if (step == 1 && stage.HasKey)
                {
                    keyScale = key.CarryVisual.localScale;
                    Check(door.TryOpen(), "Carried key cannot unlock door");
                    Check(!RewindManager.Current.BeginRewind(), "Key flight does not lock rewind");
                    Next();
                }
                else if (step == 2 && elapsed > 0.15)
                {
                    door.gameObject.SetActive(false);
                    Check(stage.HasKey && key.CarryVisual != null && key.CarryVisual.localScale == keyScale,
                        "Pre-insertion interruption consumes/deforms key");
                    Check(!GameplayActionLocks.IsLocked(GameplayAction.Input, door.gameObject.scene), "Interrupted flight leaks input lock");
                    door.gameObject.SetActive(true);
                    door.OnOpened.AddListener(() => openedEvents++);
                    Check(door.TryOpen(), "Interrupted door cannot retry");
                    Next();
                }
                else if (step == 3 && elapsed > 0.15)
                {
                    pausedKey = key.CarryVisual.position;
                    Time.timeScale = 0;
                    Next();
                }
                else if (step == 4 && elapsed > 0.2)
                {
                    Check(key.CarryVisual.position == pausedKey, "Pause does not freeze key flight");
                    Time.timeScale = 1;
                    Next();
                }
                else if (step == 5 && door.DoorVisual.Progress > 0.25f)
                {
                    Check(stage.Stage == GameStage.DoorOpened && key.CarryVisual == null, "Key was not consumed on insertion");
                    Check(door.GetComponent<BoxCollider2D>().enabled && door.IsOpening, "Barrier opens before dissolve completes");
                    Check(GameplayActionLocks.IsLocked(GameplayAction.Input | GameplayAction.Rewind, door.gameObject.scene), "Dissolve lacks action lock");
                    pausedProgress = door.DoorVisual.Progress;
                    Time.timeScale = 0;
                    Capture(Camera.main, "door-play-dissolve");
                    Next();
                }
                else if (step == 6 && elapsed > 0.2)
                {
                    Check(door.DoorVisual.Progress == pausedProgress, "Pause does not freeze dissolve");
                    Time.timeScale = 1;
                    Next();
                }
                else if (step == 7 && !door.IsOpening)
                {
                    Check(door.IsOpen && !door.GetComponent<BoxCollider2D>().enabled && openedEvents == 1, "Door completion/event is incorrect");
                    Check(door.DoorVisual.Progress == 1 && door.DoorVisual.Renderers.All(r => !r.enabled), "Door mesh remains visible");
                    Check(!GameplayActionLocks.IsLocked(GameplayAction.Input | GameplayAction.Rewind, door.gameObject.scene), "Completed door leaks locks");
                    Check(!door.TryOpen(), "Door reopens");
                    door.gameObject.SetActive(false);
                    door.gameObject.SetActive(true);
                    Check(!door.GetComponent<BoxCollider2D>().enabled && door.DoorVisual.Progress == 1, "Reactivation restores closed door");
                    sceneHandle = door.gameObject.scene.handle;
                    Next();
                    EditorSceneManager.LoadSceneAsyncInPlayMode(GoldenDoorAssets.PreviewPath, new LoadSceneParameters(LoadSceneMode.Single));
                }
                else if (step == 8 && door.gameObject.scene.handle != sceneHandle)
                {
                    Check(stage.Stage == GameStage.DoorOpened && door.IsOpen && !door.GetComponent<BoxCollider2D>().enabled,
                        "Scene reload lost open state");
                    Check(door.DoorVisual.Progress == 1 && key.CarryVisual == null, "Scene reload restored dissolved door or spent key");
                    File.AppendAllText(Path.Combine(Output, "play-results.txt"),
                        "PASS: Keyless rejection, carried-key insertion, pre-insertion interruption + retry, exact key scale restoration.\n"
                        + "PASS: Pause freezes key flight and dissolve; collision remains until completion; key consumed once; action locks released.\n"
                        + "PASS: One opened event, no replay, hidden mesh and disabled collision after reactivation and scene reload.\n");
                    SessionState.SetBool(Pending, false);
                    EditorApplication.update -= Tick;
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception error) { Fail(error); }
        }

        private static void Next() { step++; since = EditorApplication.timeSinceStartup; }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Fail(Exception error)
        {
            Directory.CreateDirectory(Output);
            File.AppendAllText(Path.Combine(Output, "play-results.txt"), "FAIL step " + step + ": " + error + "\n");
            Debug.LogException(error);
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(1);
        }

        private static byte[] Capture(Camera camera, string name)
        {
            var target = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            var previous = RenderTexture.active;
            var image = new Texture2D(1280, 960, TextureFormat.RGB24, false);
            try
            {
                target.Create();
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0);
                image.Apply();
                byte[] bytes = image.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(Output, name + ".png"), bytes);
                return bytes;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                target.Release();
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(target);
            }
        }
    }
}
