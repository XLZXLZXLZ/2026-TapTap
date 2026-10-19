using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Run only in an isolated project. Never loads or saves World_Character_Play.
    [InitializeOnLoad]
    public static class ReturnJourneyRegressionTests
    {
        private const string Pending = "TapTap.ReturnJourneyRegression";
        private const string ScenePath = "Assets/ReturnJourneyValidation/Flow.unity";
        private static string Output => Path.GetFullPath("Logs/ReturnJourney");
        private static string ResultsFile => Path.Combine(Output, SessionState.GetBool(Pending + ".World", false) ? "world-results.txt"
            : SessionState.GetBool(Pending + ".Preview", false) ? "preview-results.txt" : "results.txt");
        private static int step, lastFrame, events;
        private static double deadline, stepStarted;
        private static bool capturedTear;
        private static Vector2 platformCollision;
        private static int originalSceneHandle;
        private static float normalBrightness, normalFieldOfView;
        private static bool sawPickupZoom;
        private static double pickupSimulationStart;
        private static bool preparedWarning, sawEntryCollapse, capturedEntryPeak;
        private static int warningsChecked;
        private static float maximumEntryDarkness;
        private static double cueSimulationStart;
        private static bool pausedWarningTest, resumedWarningTest;
        private static double warningPauseStarted;
        private static float pausedWarningDarkness;
        private static readonly List<Action> endpointColorChecks = new List<Action>();
        private static readonly List<Action> externalExitLightChecks = new List<Action>();

        static ReturnJourneyRegressionTests()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
                step = events = 0;
                capturedTear = sawPickupZoom = false;
                preparedWarning = sawEntryCollapse = capturedEntryPeak = false;
                warningsChecked = 0;
                maximumEntryDarkness = 0f;
                pausedWarningTest = resumedWarningTest = false;
                lastFrame = -1;
                deadline = EditorApplication.timeSinceStartup + 90;
                if (SessionState.GetBool(Pending + ".World", false)) EditorApplication.update += TickWorld;
                else if (SessionState.GetBool(Pending + ".Preview", false)) EditorApplication.update += TickPreview;
                else EditorApplication.update += Tick;
            };
        }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            try
            {
                Directory.CreateDirectory(Output);
                File.WriteAllText(Path.Combine(Output, "results.txt"), "");
                SessionState.SetBool(Pending + ".Preview", false);
                SessionState.SetBool(Pending + ".World", false);
                ReturnJourneyAssets.Build();
                LevelPrefabBuilder.EnsureFolder("Assets/ReturnJourneyValidation");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/ReturnJourneyValidation/Bootstrap.unity");
                ReturnJourneyAssets.CreatePreview();
                ValidateAssets();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                LevelPrefabBuilder.EnsureFolder("Assets/ReturnJourneyValidation");
                GameObject player = Instantiate("Assets/Prefabs/Characters/Player.prefab");
                player.GetComponent<PlayerInput>().UseKeyboard = false;
                player.GetComponent<PlayerController>().TeleportJoined(new Vector2(-30f, 1f));
                GameObject floor = new GameObject("Floor");
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                floor.AddComponent<BoxCollider2D>().size = new Vector2(200f, 1f);
                floor.AddComponent<WorldSurface>();
                Instantiate(ReturnJourneyAssets.DeparturePath).transform.position = new Vector3(50f, 0f, 0f);
                Instantiate(ReturnJourneyAssets.KeyPath).transform.position = new Vector3(-10f, 1f, 0f);
                Instantiate(ReturnJourneyAssets.DoorPath).transform.position = new Vector3(-20f, 0.5f, 0f);
                Instantiate(ReturnJourneyAssets.PausePath);
                var light = new GameObject("Guide Light").AddComponent<IndicatorLight>();
                light.StageFadeDuration = 0.3f;
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
                SessionState.SetBool(Pending, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Fail(error); }
        }
        public static void RunPreviewBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            EditorSceneManager.OpenScene(ReturnJourneyAssets.PreviewPath, OpenSceneMode.Single);
            Object.FindObjectOfType<PlayerController>().transform.position = new Vector3(-8f, 0.6f, 0f);
            Object.FindObjectOfType<DemoCamera>().ViewCamera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ReturnJourneyAssets.PreviewPath);
            SessionState.SetBool(Pending + ".Preview", true);
            SessionState.SetBool(Pending + ".World", false);
            File.WriteAllText(ResultsFile, "");
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }
        public static void RunWorldBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            EditorSceneManager.OpenScene(ReturnJourneyAssets.WorldPreviewPath, OpenSceneMode.Single);
            BeginWorldValidation();
        }
        public static void RunCurrentWorldLightsBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            // Test a fresh COPY of the actual map, including lights authored outside the exit prefab.
            const string path = "Assets/ReturnJourneyValidation/CurrentWorld.unity";
            LevelPrefabBuilder.EnsureFolder("Assets/ReturnJourneyValidation");
            File.Copy("Assets/Scenes/WorldMaps/World_Character_Play.unity", path, true);
            AssetDatabase.ImportAsset(path);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Object.FindObjectOfType<WorldMapProgression>().InitialLevelNumber = 20;
            EditorSceneManager.SaveScene(scene, path);
            BeginWorldValidation(true);
        }
        private static void BeginWorldValidation(bool lightsOnly = false)
        {
            SessionState.SetBool(Pending + ".LightsOnly", lightsOnly);
            SessionState.SetBool(Pending + ".World", true);
            SessionState.SetBool(Pending + ".Preview", true);
            Directory.CreateDirectory(Output);
            File.WriteAllText(ResultsFile, "");
            VerifyAtmosphere();
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }
        private static void TickWorld()
        {
            try
            {
                if (!EditorApplication.isPlaying || Time.frameCount == lastFrame || Time.frameCount < 5) return;
                lastFrame = Time.frameCount;
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("World preview timed out at " + step);
                var player = Object.FindObjectOfType<PlayerController>();
                var rig = Object.FindObjectOfType<DemoCamera>();
                var view = Object.FindObjectOfType<ReturnJourneyPresentation>();
                var menu = PauseMenu.Current;
                Check(player != null && rig != null && view != null, "Complete map is missing gameplay components");
                player.GetComponent<PlayerInput>().UseKeyboard = false;
                RewindManager.Current.UseKeyboard = false;
                double elapsed = EditorApplication.timeSinceStartup - stepStarted;
                if (step == 0)
                {
                    Check(WorldMapProgression.Current.CurrentNumber == 20, "Complete map no longer starts at level 20");
                    endpointColorChecks.Clear();
                    externalExitLightChecks.Clear();
                    var endpoints = Object.FindObjectsOfType<LevelEndpoint>();
                    Check(endpoints.Length > 0, "World has no End exits to validate");
                    var lightObject = new GameObject("Endpoint test green light");
                    lightObject.transform.SetParent(endpoints[0].transform, false);
                    var light = lightObject.AddComponent<Light>();
                    light.enabled = false;
                    light.color = new Color(0.2f, 1f, 0.1f, 0.6f);
                    foreach (LevelEndpoint endpoint in endpoints) TrackEndpointColors(endpoint);
                    Check(endpointColorChecks.Count > 1, "No green endpoint materials found");
                    int outsideExitLights = 0, entryLights = 0;
                    foreach (Light portalLight in Object.FindObjectsOfType<Light>(true))
                    {
                        // These legacy names identify the separately authored exit/entry fixtures in the real map.
                        if (!portalLight.name.StartsWith("Start_Portal_Green_Light")) continue;
                        Color original = portalLight.color;
                        float intensity = portalLight.intensity, range = portalLight.range;
                        bool enabled = portalLight.enabled;
                        if (portalLight.name.EndsWith("(2)"))
                        {
                            outsideExitLights++;
                            externalExitLightChecks.Add(() =>
                            {
                                CheckRedTone(portalLight.color, original, portalLight.name + " outside End hierarchy");
                                Check(portalLight.intensity == intensity && portalLight.range == range && portalLight.enabled == enabled,
                                    "External exit light lost its authored intensity / range / enabled state");
                            });
                        }
                        else if (portalLight.name.EndsWith("(1)"))
                        {
                            entryLights++;
                            externalExitLightChecks.Add(() => Check(portalLight.color == original, "Spawn light was incorrectly turned red"));
                        }
                    }
                    Pass("Tracking real external fixtures: " + outsideExitLights + " exit lights and " + entryLights + " entry lights");
                    if (SessionState.GetBool(Pending + ".LightsOnly", false))
                    {
                        step = 4; stepStarted = EditorApplication.timeSinceStartup;
                        return;
                    }
                    Check(PauseMenu.HandlesEscape && menu != null, "Escape would fall through to level selection");
                    menu.Toggle();
                    Check(PauseMenu.IsOpen && Time.timeScale == 0f, "Escape handler did not open pause");
                    Next();
                }
                else if (step == 1 && !menu.IsTransitioning)
                {
                    menu.Toggle();
                    Next();
                }
                else if (step == 2 && !PauseMenu.BlocksInput)
                {
                    Check(Time.timeScale > 0f, "Resume did not restore gameplay speed");
                    cueSimulationStart = RewindManager.Current.SimulationTime;
                    Check(Object.FindObjectsOfType<ReturnJourneyTrigger>().First(t => t.Cue == ReturnJourneyCue.Warning).Trigger(), "Warning did not trigger");
                    Next();
                }
                else if (step == 3 && elapsed >= 0.14 && elapsed < view.FlashDuration)
                {
                    Check(GameplayActionLocks.IsLocked(GameplayAction.PlayerControls, player.gameObject.scene)
                        && !WorldMapProgression.InputBlocked && !RewindManager.Current.BeginRewind()
                        && RewindManager.Current.SimulationTime > cueSimulationStart && Mathf.Approximately(Time.timeScale, 1f),
                        "Warning must lock player controls / Z while keeping time and simulation running");
                    player.GetComponent<PlayerInput>().SetExternalInput(1f, false);
                    Check(player.GetComponent<PlayerInput>().Horizontal == 0f, "Warning accepted player movement");
                    player.GetComponent<PlayerInput>().SetExternalInput(0f, false);
                    VerifyWorldDimming(view, rig.ViewCamera, "world-warning");
                    Check(view.FlashDuration == 0.24f && view.FlashGap == 0.24f && view.WarningDarkness == 0.2f, "Slower warning configuration missing");
                    Next();
                }
                else if (step == 4 && elapsed > 1.1)
                {
                    Check(!GameplayActionLocks.IsLocked(GameplayAction.PlayerControls, player.gameObject.scene), "Warning leaked its control lock");
                    if (!SessionState.GetBool(Pending + ".LightsOnly", false) && warningsChecked == 0)
                    {
                        var second = Object.FindObjectsOfType<ReturnJourneyTrigger>().First(t => t.Cue == ReturnJourneyCue.Warning && !t.HasTriggered);
                        Check(second.Trigger() && second.IsPlaying, "Second warning did not start");
                        warningsChecked++;
                        stepStarted = EditorApplication.timeSinceStartup;
                        return;
                    }
                    Check(Object.FindObjectsOfType<ReturnJourneyTrigger>().Single(t => t.Cue == ReturnJourneyCue.BeginReturn).Trigger(), "Third trigger did not enter return stage");
                    Next();
                }
                else if (step == 5)
                {
                    var begin = Object.FindObjectsOfType<ReturnJourneyTrigger>().Single(t => t.Cue == ReturnJourneyCue.BeginReturn);
                    maximumEntryDarkness = Mathf.Max(maximumEntryDarkness, view.EffectiveDarkness);
                    if (view.IsEntryPlaying)
                    {
                        Check(begin.Platform == null || !begin.Platform.HasCollapsed, "Platform collapsed before darkness rebounded");
                        Check(GameplayActionLocks.IsLocked(GameplayAction.PlayerControls, player.gameObject.scene), "Third cue released control too early");
                        if (!capturedEntryPeak && view.EffectiveDarkness >= view.EntryPeakDarkness * 0.95f)
                        {
                            CaptureCamera(rig.ViewCamera, "world-entry-overdark", true);
                            capturedEntryPeak = true;
                        }
                        return;
                    }
                    if (!sawEntryCollapse && begin.Platform != null && begin.Platform.HasCollapsed)
                    {
                        Check(Mathf.Abs(view.EffectiveDarkness - view.Darkness) < 0.015f, "Collapse started before final darkness settled");
                        Check(EffectManager.Instance.ShakeViewportMultiplier >= 3f, "Collapse did not use stronger cinematic shake");
                        sawEntryCollapse = true;
                    }
                    if (begin.IsPlaying) return;
                    Check(maximumEntryDarkness > view.Darkness + 0.15f && sawEntryCollapse,
                        "Entry did not overshoot darkness and then collapse the platform");
                    Check(!GameplayActionLocks.IsLocked(GameplayAction.PlayerControls | GameplayAction.Rewind, player.gameObject.scene),
                        "Departure cue leaked controls after collapse");
                    Pass("Both warnings lock controls until finished without stopping time; third cue overshoots darkness, rebounds, then collapses with stronger shake and releases controls");
                    foreach (Action verify in externalExitLightChecks) verify();
                    Pass("Real Arts exit point lights turn red; entry lights remain green; light intensity / range / enabled state preserved");
                    foreach (Action verify in endpointColorChecks) verify();
                    var lateEndpoint = Instantiate(GameplayVisualAssets.EndpointPrefabPath).GetComponent<LevelEndpoint>();
                    endpointColorChecks.Clear();
                    TrackEndpointColors(lateEndpoint);
                    lateEndpoint.enabled = false;
                    lateEndpoint.enabled = true;
                    foreach (Action verify in endpointColorChecks) verify();
                    Object.Destroy(lateEndpoint.gameObject);
                    Pass("All active End exits turn green materials, sprite tints and lights red with brightness / alpha preserved; late and re-enabled exits restore red without changing shared materials");
                    if (SessionState.GetBool(Pending + ".LightsOnly", false))
                    {
                        CaptureCamera(rig.ViewCamera, "world-exit-lights-returning", true);
                        Finish(); return;
                    }
                    VerifyWorldDimming(view, rig.ViewCamera, "world-returning");
                    Check(view.Blend > 0.99f, "Third-trigger dimming did not finish");
                    Check(view.Darkness == 0.22f && view.FadeDuration == 0.25f && view.TearPixels == 7f
                        && view.TearFrequency == 2f && view.TearDuration == 0.08f, "Updated return atmosphere configuration missing");
                    Check(view.ChromaticPixels == 2.5f && view.Saturation == 0.85f && view.VignetteStrength == 0.18f
                        && view.VignetteStart == 0.55f, "Return color split, desaturation or vignette configuration missing");
                    normalFieldOfView = rig.ViewCamera.fieldOfView;
                    pickupSimulationStart = RewindManager.Current.SimulationTime;
                    var key = Object.FindObjectsOfType<ProgressKey>().First(k => k.gameObject.scene == player.gameObject.scene);
                    Check(key.TryCollect(player), "Complete-map key pickup failed");
                    Check(!PauseMenu.IsOpen, "Menu opened before pickup animation");
                    Next();
                }
                else if (step == 6)
                {
                    if (GameStageProgression.InputBlocked && rig.ViewCamera.fieldOfView < normalFieldOfView * 0.98f) sawPickupZoom = true;
                    Check(rig.ViewCamera.fieldOfView > normalFieldOfView * 0.90f, "Pickup zoom is still too heavy");
                    if (!PauseMenu.IsOpen)
                    {
                        Check(Mathf.Approximately(Time.timeScale, 1f) && !WorldMapProgression.InputBlocked, "Pickup still freezes or slows gameplay");
                        return;
                    }
                    Check(sawPickupZoom, "Complete-map pickup skipped its zoom");
                    Check(RewindManager.Current.SimulationTime > pickupSimulationStart, "Simulation did not advance during pickup");
                    Check(GameStageProgression.StageIn(player.gameObject.scene) == GameStage.CarryingKey, "Menu opened before key stage completed");
                    Next();
                }
                else if (step == 7 && !menu.GetComponent<ReturnJourneyPausePortrait>().IsRevealing)
                {
                    var portrait = menu.GetComponent<ReturnJourneyPausePortrait>();
                    Check(portrait.HasChanged && portrait.ReturnArt.gameObject.activeInHierarchy, "Blueprint did not replace pause artwork");
                    Pass("Complete level-20 map: Escape pause/resume, 0.22 dimming in 0.25s, sparse stronger tears, gentle pickup zoom with continuous simulation and no slowdown, then pause and blueprint");
                    Finish();
                }
            }
            catch (Exception error) { Fail(error); }
        }
        private static void VerifyWorldDimming(ReturnJourneyPresentation view, Camera camera, string label)
        {
            // Same-frame paired render isolates the filter from new guide lights and moving particles.
            Camera selected = ReturnJourneyPresentation.ForCamera(camera)?.TargetCamera;
            Check(selected == camera, "Return atmosphere did not bind to the actual world camera");
            float filtered = CaptureCamera(camera, label, true);
            // For warnings the flash is independent of Darkness: suppress rendering, without resetting its coroutine.
            var oldTarget = view.TargetCamera;
            var dummy = new GameObject("Unfiltered comparison camera").AddComponent<Camera>();
            dummy.enabled = false;
            view.TargetCamera = dummy;
            float unfiltered = CaptureCamera(camera, label + "-unfiltered", true);
            view.TargetCamera = oldTarget;
            Object.Destroy(dummy.gameObject);
            Check(filtered < unfiltered * 0.97f, "Actual world camera did not visibly dim: " + unfiltered + " -> " + filtered + "; blend=" + view.Blend + "; renders=" + view.RenderCount);
            Pass(label + " same-frame luminance: " + unfiltered + " -> " + filtered + "; blend=" + view.Blend + "; renders=" + view.RenderCount);
        }
        private static void TrackEndpointColors(LevelEndpoint endpoint)
        {
            foreach (Renderer renderer in endpoint.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    if (material == null) continue;
                    foreach (string property in new[] { "_BaseColor", "_Color", "_EmissionColor", "_TintColor" })
                    {
                        if (!material.HasProperty(property)) continue;
                        Color original = material.GetColor(property);
                        if (original.g <= Mathf.Max(original.r, original.b) + 0.01f) continue;
                        int materialSlot = slot;
                        endpointColorChecks.Add(() =>
                        {
                            var block = new MaterialPropertyBlock();
                            renderer.GetPropertyBlock(block, materialSlot);
                            CheckRedTone(block.GetColor(property), original, renderer.name + "/" + property);
                            Check(material.GetColor(property) == original && renderer.sharedMaterials[materialSlot] == material,
                                "Exit modified or replaced a shared material");
                        });
                    }
                }
                if (renderer is SpriteRenderer sprite)
                {
                    Color original = sprite.color;
                    if (original.g > Mathf.Max(original.r, original.b) + 0.01f)
                        endpointColorChecks.Add(() => CheckRedTone(sprite.color, original, sprite.name));
                }
            }
            foreach (Light light in endpoint.GetComponentsInChildren<Light>(true))
            {
                Color original = light.color;
                if (original.g > Mathf.Max(original.r, original.b) + 0.01f)
                    endpointColorChecks.Add(() => CheckRedTone(light.color, original, light.name));
            }
        }
        private static void CheckRedTone(Color actual, Color original, string label)
        {
            Check(actual.r > actual.g && actual.r > actual.b, "Exit did not turn red: " + label);
            Check(Mathf.Abs(actual.maxColorComponent - original.maxColorComponent) < 0.01f
                && Mathf.Abs(actual.a - original.a) < 0.001f, "Exit lost its original brightness or alpha: " + label);
        }
        private static void TickPreview()
        {
            try
            {
                if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Preview timed out at " + step);
                var player = Object.FindObjectOfType<PlayerController>();
                var rig = Object.FindObjectOfType<DemoCamera>();
                if (player == null || rig == null || RewindManager.Current == null) return;
                player.GetComponent<PlayerInput>().UseKeyboard = false;
                RewindManager.Current.UseKeyboard = false;
                double elapsed = EditorApplication.timeSinceStartup - stepStarted;
                if (step == 0)
                {
                    Check(Object.FindObjectOfType<GameStageProgression>().Stage == GameStage.Outbound, "Preview spawned inside the third trigger");
                    normalFieldOfView = rig.ViewCamera.fieldOfView;
                    normalBrightness = CaptureCamera(rig.ViewCamera, "preview-outbound");
                    Check(Object.FindObjectsOfType<ReturnJourneyTrigger>().Single(t => t.Cue == ReturnJourneyCue.BeginReturn).Trigger(), "Preview could not enter return stage");
                    Next();
                }
                else if (step == 1 && elapsed > 0.8
                    && !Object.FindObjectsOfType<ReturnJourneyTrigger>().Single(t => t.Cue == ReturnJourneyCue.BeginReturn).IsPlaying)
                {
                    Check(ReturnJourneyPresentation.ForCamera(rig.ViewCamera) != null, "Atmosphere not bound to gameplay camera");
                    float brightness = CaptureCamera(rig.ViewCamera, "preview-returning");
                    var atmosphere = ReturnJourneyPresentation.ForCamera(rig.ViewCamera);
                    Check(brightness < normalBrightness * 0.99f, "Actual URP camera did not dim: " + normalBrightness + " -> " + brightness
                        + "; blend=" + atmosphere.Blend + "; renders=" + atmosphere.RenderCount);
                    Check(Object.FindObjectOfType<ProgressKey>().TryCollect(player), "Preview key pickup failed");
                    pickupSimulationStart = RewindManager.Current.SimulationTime;
                    Next();
                }
                else if (step == 2)
                {
                    if (GameStageProgression.InputBlocked && rig.ViewCamera.fieldOfView < normalFieldOfView * 0.98f)
                        sawPickupZoom = true;
                    if (!PauseMenu.IsOpen)
                    {
                        Check(Mathf.Approximately(Time.timeScale, 1f) && !WorldMapProgression.InputBlocked, "Pickup still freezes or slows gameplay");
                        return;
                    }
                    Check(sawPickupZoom, "Actual gameplay camera did not play pickup zoom");
                    Check(RewindManager.Current.SimulationTime > pickupSimulationStart, "Preview simulation froze during pickup");
                    Next();
                }
                else if (step == 3 && !PauseMenu.Current.GetComponent<ReturnJourneyPausePortrait>().IsRevealing)
                {
                    Pass("Standalone preview loads; actual URP gameplay camera renders return atmosphere and gentle pickup zoom with continuous simulation before the blueprint pause");
                    Finish();
                }
            }
            catch (Exception error) { Fail(error); }
        }
        private static float CaptureCamera(Camera camera, string label, bool wholeFrame = false)
        {
            var target = new RenderTexture(1280, 720, 24);
            target.Create();
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output, label + ".png"), image.EncodeToPNG());
            // Compare a fixed background patch. New guide lights and explosion particles legitimately brighten other pixels.
            float mean = (wholeFrame ? image.GetPixels() : image.GetPixels(16, 16, 96, 96)).Average(c => c.grayscale);
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            Object.DestroyImmediate(image); target.Release(); Object.DestroyImmediate(target);
            return mean;
        }
        private static GameObject Instantiate(string path) => (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        private static void ValidateAssets()
        {
            foreach (string resource in new[] { "Shaders/ReturnJourneySignal", "Shaders/ReturnPortraitTear" })
            {
                Shader shader = Resources.Load<Shader>(resource);
                Check(shader != null && !ShaderUtil.ShaderHasError(shader) && shader.isSupported, "Shader failed: " + resource);
            }
            GameObject departure = AssetDatabase.LoadAssetAtPath<GameObject>(ReturnJourneyAssets.DeparturePath);
            var triggers = departure.GetComponentsInChildren<ReturnJourneyTrigger>();
            Check(triggers.Length == 3 && triggers.Count(t => t.Cue == ReturnJourneyCue.BeginReturn) == 1, "Departure triggers missing");
            Check(triggers.Select(t => t.CueId).Distinct().Count() == 3, "Duplicate trigger IDs");
            var platform = departure.GetComponentInChildren<ReturnJourneyPlatform>();
            Check(Mathf.Abs(platform.View.localPosition.y - platform.FallenLocalPosition.y - 1f) < 0.001f, "Platform must start one grid above fixed collision");
            var pause = AssetDatabase.LoadAssetAtPath<GameObject>(ReturnJourneyAssets.PausePath).GetComponent<ReturnJourneyPausePortrait>();
            Check(pause != null && pause.OriginalArt != null && pause.ReturnArt != null && !pause.ReturnArt.gameObject.activeSelf, "Original / hidden pause image binding missing");
            Check(pause.ReturnArt.gameObject.layer == 5 && pause.ReturnArt.rectTransform.sizeDelta.x < 960f, "Blueprint must occupy the left artwork area on the UI layer");
            Check(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(pause.ReturnArt.sprite)) == "74830c13907d429d965607a4a3e04ae7", "Wrong replacement image");
            var key = AssetDatabase.LoadAssetAtPath<GameObject>(ReturnJourneyAssets.KeyPath).GetComponent<ProgressKey>();
            Check(key.PickupFeedbackPrefab != null && key.PickupFeedbackPrefab != key.PickupBurstPrefab, "Weak pickup / strong unlock particles must be separate");
            VerifyAtmosphere();
            Pass("Prefabs, unique trigger IDs, visual-only platform offset, weak pickup particles, blueprint slot and both shaders");
        }
        private static void VerifyAtmosphere()
        {
            var input = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            input.SetPixels(Enumerable.Repeat(Color.white, 4096).ToArray()); input.Apply();
            var target = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create();
            var material = new Material(Resources.Load<Shader>("Shaders/ReturnJourneySignal"));
            material.SetVector("_JourneyParams", new Vector4(0.22f, 0f, 0f, 0f));
            Graphics.Blit(input, target, material);
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var readback = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); readback.Apply();
            float value = readback.GetPixel(32, 32).r;
            Check(Mathf.Abs(value - 0.78f) < 0.025f, "Atmosphere shader did not produce 22% dimming: " + value);
            var stripes = Enumerable.Range(0, 4096).Select(i => (i % 64 / 8) % 2 == 0 ? Color.white : Color.black).ToArray();
            input.SetPixels(stripes); input.Apply();
            material.SetVector("_JourneyTearTiming", new Vector4(2f, 0.08f, 0f, 0f));
            foreach (bool burst in new[] { false, true })
            {
                material.SetVector("_JourneyParams", new Vector4(0f, 7f, 0f, burst ? 0.04f : 0.3f));
                Graphics.Blit(input, target, material);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); readback.Apply();
                float difference = readback.GetPixels().Select((pixel, i) => Mathf.Abs(pixel.r - stripes[i].r)).Average();
                Check(burst ? difference > 0.005f : difference < 0.002f,
                    "Tears must visibly displace pixels during bursts and stay still between bursts: " + burst + ", " + difference);
            }
            float quietFringe = 0f;
            foreach (bool burst in new[] { false, true })
            {
                material.SetVector("_JourneyParams", new Vector4(0f, 0f, 2.5f, burst ? 0.04f : 0.3f));
                Graphics.Blit(input, target, material);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); readback.Apply();
                float fringe = readback.GetPixels().Average(pixel => Mathf.Max(pixel.r, pixel.g, pixel.b) - Mathf.Min(pixel.r, pixel.g, pixel.b));
                if (!burst) quietFringe = fringe;
                else Check(fringe > quietFringe * 1.5f, "Color separation did not strengthen during tear bursts: " + quietFringe + " -> " + fringe);
            }
            input.SetPixels(Enumerable.Repeat(new Color(0.8f, 0.3f, 0.1f, 1f), 4096).ToArray()); input.Apply();
            material.SetVector("_JourneyParams", Vector4.zero);
            material.SetVector("_JourneyColor", new Vector4(0.85f, 0.18f, 0.55f, 0f));
            Graphics.Blit(input, target, material);
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); readback.Apply();
            Color center = readback.GetPixel(32, 32);
            Check(Mathf.Abs((center.r - center.b) - 0.7f * 0.85f) < 0.015f, "15% desaturation did not apply at the center");
            Check(Mathf.Abs(center.grayscale - new Color(0.8f, 0.3f, 0.1f).grayscale) < 0.025f, "Vignette darkened the center");
            Check(readback.GetPixel(1, 1).grayscale < center.grayscale * 0.86f, "Vignette did not gently darken the edges");
            RenderTexture.active = previous;
            Object.DestroyImmediate(input); Object.DestroyImmediate(readback); Object.DestroyImmediate(material);
            target.Release(); Object.DestroyImmediate(target);
            Pass("GPU: 22% dimming, visible stronger tear bursts and stable pixels between bursts");
            Pass("GPU: stronger color separation during bursts, 15% desaturation and soft vignette with a clear center");
        }
        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || Time.frameCount == lastFrame) return;
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Step " + step + " timed out");
                lastFrame = Time.frameCount;
                var player = Object.FindObjectOfType<PlayerController>();
                var stage = Object.FindObjectOfType<GameStageProgression>();
                var key = Object.FindObjectOfType<ProgressKey>();
                var door = Object.FindObjectOfType<ReturnJourneyDoor>();
                var platform = Object.FindObjectOfType<ReturnJourneyPlatform>();
                var light = Object.FindObjectOfType<IndicatorLight>();
                var menu = PauseMenu.Current;
                var rewind = RewindManager.Current;
                if (player == null || menu == null || rewind == null) return;
                rewind.UseKeyboard = false;
                double elapsed = EditorApplication.timeSinceStartup - stepStarted;
                if (step == 0)
                {
                    if (!preparedWarning)
                    {
                        Check(stage.Stage == GameStage.Outbound && !key.IsCollected && !light.IsOn, "Initial stage incorrect");
                        Check(!key.TryCollect(player), "Key can be collected before the third trigger");
                        Check(!door.TryOpen(), "Door opened without a key");
                        EventManager.Subscribe<GameStageChanged>(message => { if (message.Scene.path == ScenePath) events++; });
                        var headArea = new GameObject("Head-only sensor").AddComponent<BoxCollider2D>();
                        headArea.isTrigger = true; headArea.size = Vector2.one * 0.05f;
                        headArea.transform.position = player.Head.Motor.Position + player.Head.Motor.CenterOffset;
                        Physics2D.SyncTransforms();
                        Check(!ReturnJourneyContact.TouchesBody(headArea, player), "Head-only contact triggered a body sensor");
                        Object.Destroy(headArea.gameObject);
                        var cancelled = new GameObject("Cancelled warning").AddComponent<ReturnJourneyTrigger>();
                        cancelled.CueId = "cancelled-regression-warning";
                        cancelled.Presentation = Object.FindObjectOfType<ReturnJourneyPresentation>();
                        Check(cancelled.Trigger(), "Cancellation fixture did not start");
                        cancelled.enabled = false;
                        Check(!GameplayActionLocks.IsLocked(GameplayAction.PlayerControls | GameplayAction.Rewind, player.gameObject.scene),
                            "Disabled warning leaked controls");
                        Object.Destroy(cancelled.gameObject);
                        var warnings = Object.FindObjectsOfType<ReturnJourneyTrigger>().Where(t => t.Cue == ReturnJourneyCue.Warning && t.isActiveAndEnabled).ToArray();
                        Check(warnings[0].Trigger() && !warnings[0].Trigger(), "Warning replayed");
                        Check(stage.Stage == GameStage.Outbound, "Warning started return stage");
                        platformCollision = platform.GetComponent<BoxCollider2D>().offset;
                        preparedWarning = true;
                        return;
                    }
                    if (Object.FindObjectsOfType<ReturnJourneyTrigger>().Any(t => t.Cue == ReturnJourneyCue.Warning && t.IsPlaying))
                    {
                        var presentation = Object.FindObjectOfType<ReturnJourneyPresentation>();
                        if (!pausedWarningTest && presentation.EffectiveDarkness > 0f)
                        {
                            pausedWarningDarkness = presentation.EffectiveDarkness;
                            warningPauseStarted = EditorApplication.timeSinceStartup;
                            Check(menu.Open(), "Cannot pause a warning cue");
                            pausedWarningTest = true;
                        }
                        else if (pausedWarningTest && !resumedWarningTest && EditorApplication.timeSinceStartup - warningPauseStarted > 0.15)
                        {
                            Check(Time.timeScale == 0f && Mathf.Abs(presentation.EffectiveDarkness - pausedWarningDarkness) < 0.001f
                                && GameplayActionLocks.IsLocked(GameplayAction.PlayerControls, player.gameObject.scene),
                                "Pause advanced warning or released controls early");
                            menu.Resume();
                            resumedWarningTest = true;
                        }
                        return;
                    }
                    Check(pausedWarningTest && resumedWarningTest, "Warning pause / resume was not exercised");
                    Pass("Warning pause keeps its control lease and visual progress; resume completes; disabling a warning releases its locks");
                    var effects = EffectManager.Instance;
                    effects.ShakeCamera(0.3f, 0.35f, 3f);
                    effects.Simulate(0.05f);
                    effects.ShakeCamera(0.02f);
                    effects.Simulate(0.2f);
                    Check(effects.ShakeViewportMultiplier >= 3f, "Weaker feedback shortened the collapse shake");
                    effects.ClearTransientEffects();
                    rewind.SimulateStep(Time.fixedDeltaTime);
                    Check(rewind.CurrentTick > 0, "No history before return");
                    var begin = Object.FindObjectsOfType<ReturnJourneyTrigger>().Single(t => t.Cue == ReturnJourneyCue.BeginReturn);
                    Check(begin.Trigger() && !begin.Trigger(), "Return trigger is not one-shot");
                    Check(stage.Stage == GameStage.Returning && events == 1 && rewind.CurrentTick == 0, "Third trigger did not establish rewind boundary exactly once");
                    Check(!key.IsCollected && key.GetComponent<MeshRenderer>().enabled, "Return stage hid the uncollected key");
                    Check(light.IsOn && light.GetComponent<MeshFilter>().sharedMesh.colors.Max(c => c.a) < light.Tint.a * 0.5f, "Guide light did not begin fading");
                    Next();
                }
                else if (step == 1 && elapsed > 0.7
                    && !Object.FindObjectsOfType<ReturnJourneyTrigger>().Single(t => t.Cue == ReturnJourneyCue.BeginReturn).IsPlaying)
                {
                    Check(platform.HasCollapsed && Vector3.Distance(platform.View.localPosition, platform.FallenLocalPosition) < 0.002f, "Platform did not settle");
                    Check(platform.GetComponent<BoxCollider2D>().offset == platformCollision, "Collapse moved collision");
                    Check(light.GetComponent<MeshFilter>().sharedMesh.colors.Max(c => c.a) > light.Tint.a * 0.8f, "Light fade did not finish");
                    SetGameSpeed(2f);
                    pickupSimulationStart = rewind.SimulationTime;
                    Check(key.TryCollect(player), "Key cannot be collected in return stage");
                    Check(!WorldMapProgression.InputBlocked && !rewind.BeginRewind(), "Pickup must keep gameplay running and only lock Z");
                    Check(!PauseMenu.IsOpen, "Pause opened before pickup animation");
                    Next();
                }
                else if (step == 2)
                {
                    if (elapsed < 0.5) Check(!PauseMenu.IsOpen, "Pickup animation was skipped");
                    if (!PauseMenu.IsOpen)
                    {
                        Check(Mathf.Approximately(Time.timeScale, 2f) && !WorldMapProgression.InputBlocked, "Pickup changed 2x game speed or froze simulation");
                        return;
                    }
                    Check(rewind.SimulationTime > pickupSimulationStart, "Pickup simulation did not advance");
                    Check(stage.HasKey && events == 2 && rewind.CurrentTick == 0, "Pickup did not establish second boundary");
                    Check(Time.timeScale == 0f && !GameStageProgression.InputBlocked, "Pause opened before pickup emphasis restored its speed / lock");
                    Check(key.CarryVisual != null && key.CarryVisual.parent == null, "Carried key is still inside region hierarchy");
                    Check(menu.GetComponent<ReturnJourneyPausePortrait>().IsRevealing, "Portrait reveal missing");
                    Next();
                }
                else if (step == 3)
                {
                    var activePortrait = menu.GetComponent<ReturnJourneyPausePortrait>();
                    if (!capturedTear && activePortrait.IsRevealing && activePortrait.OriginalArt.material != null
                        && activePortrait.OriginalArt.material.GetVector("_Tear").x > 0.85f)
                    {
                        Capture(menu, "portrait-tear"); capturedTear = true;
                    }
                    var portrait = menu.GetComponent<ReturnJourneyPausePortrait>();
                    if (portrait.IsRevealing) return;
                    Check(portrait.HasChanged && portrait.ReturnArt.gameObject.activeSelf && !portrait.OriginalArt.enabled, "Portrait did not switch");
                    Capture(menu, "portrait-blueprint");
                    menu.Resume();
                    Next();
                }
                else if (step == 4 && !PauseMenu.IsOpen)
                {
                    Check(Mathf.Abs(Time.timeScale - 2f) < 0.001f, "Pause resumed at the pickup slow-motion speed");
                    SetGameSpeed(1f);
                    player.TeleportJoined(new Vector2(-30f, 1f));
                    Next();
                }
                else if (step == 5 && elapsed > 0.15)
                {
                    Check(Vector3.Distance(key.CarryVisual.position, player.Body.transform.position) < 2f, "Key did not follow a teleport");
                    var holder = new GameObject("Hidden Old Region");
                    key.transform.SetParent(holder.transform);
                    holder.AddComponent<RegionRevealView>().SetDiscovered(false, true);
                    Check(!key.CarryVisual.GetComponent<MeshRenderer>().forceRenderingOff, "Old region hides the carried key");
                    Check(rewind.BeginRewind(), "Z unavailable during return movement");
                    rewind.RewindBy(99f);
                    Check(stage.HasKey && key.CarryVisual != null, "Z removed key ownership");
                    rewind.EndRewind();
                    player.TeleportJoined(new Vector2(-18.7f, 0.6f));
                    Physics2D.SyncTransforms();
                    door.Player = player;
                    Check(door.TryOpen(), "Door cannot use carried key");
                    Check(!rewind.BeginRewind(), "Door animation did not lock Z");
                    Next();
                }
                else if (step == 6 && elapsed > door.KeyTravelDuration + door.OpenDuration + 0.2)
                {
                    Check(stage.Stage == GameStage.DoorOpened && events == 3 && !stage.HasKey, "Unlock did not consume the key / commit stage exactly once");
                    Check(door.IsOpen && !door.GetComponent<BoxCollider2D>().enabled && key.CarryVisual == null, "Door collision or key remained after unlocking");
                    Check(door.DoorVisual != null && door.DoorVisual.Progress == 1f, "Door dissolve did not finish");
                    Check(!WorldMapProgression.InputBlocked && !GameplayActionLocks.IsLocked(GameplayAction.Rewind, stage.gameObject.scene), "Door leaked action locks");
                    Check(!door.TryOpen(), "Open door replayed");
                    rewind.BeginRewind(); rewind.RewindBy(99f); rewind.EndRewind();
                    Check(stage.Stage == GameStage.DoorOpened && door.IsOpen, "Z crossed the door boundary");
                    var lateLight = new GameObject("Late Guide").AddComponent<IndicatorLight>();
                    Check(lateLight.IsOn, "Late light did not recover phase");
                    door.gameObject.SetActive(false); door.gameObject.SetActive(true);
                    Check(door.IsOpen && !door.GetComponent<BoxCollider2D>().enabled, "Door restore lost progress");
                    Pass("Third trigger, independent key state, all three Z boundaries, one-shot events, fixed collision collapse, light fade and late restoration");
                    Pass("Gentle pickup before forced pause, continuous simulation at unchanged 2x speed, tear-to-blueprint, key follows teleport / survives old-region hiding and Z");
                    Pass("Keyless door blocked, carrying key unlocks once, strong consume burst, collision opens, locks released and Z cannot undo door");
                    originalSceneHandle = SceneManager.GetActiveScene().handle;
                    Next();
                    EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
                }
                else if (step == 7 && SceneManager.GetActiveScene().handle != originalSceneHandle && elapsed > 0.3)
                {
                    Check(stage.Stage == GameStage.DoorOpened && events == 3, "Scene reload lost phase or republished stage events");
                    Check(door.IsOpen && !door.GetComponent<BoxCollider2D>().enabled && key.IsCollected && key.CarryVisual == null,
                        "Scene reload restored a consumed key or closed door");
                    Check(menu.GetComponent<ReturnJourneyPausePortrait>().HasChanged && !PauseMenu.IsOpen, "Reload replayed forced pause or lost blueprint");
                    Check(platform.HasCollapsed && light.IsOn, "Reload lost collapse / light state");
                    Check(capturedTear, "Peak portrait distortion was not rendered");
                    Pass("Head-only contact ignored; real scene reload preserves phase, consumed key, open door, fallen platform, lights and blueprint without duplicate events / forced pause");
                    Finish();
                }
            }
            catch (Exception error) { Fail(error); }
        }
        private static void Next() { step++; stepStarted = EditorApplication.timeSinceStartup; }
        private static void SetGameSpeed(float speed)
        {
            var debugTime = Object.FindObjectOfType<GameDebugTime>();
            if (debugTime != null)
                for (int i = 0; i < 4 && !Mathf.Approximately(debugTime.CurrentSpeed, speed); i++) debugTime.CycleSpeed();
            Time.timeScale = speed;
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Pass(string text) { File.AppendAllText(ResultsFile, "PASS: " + text + "\n"); Debug.Log("RETURN JOURNEY PASS: " + text); }
        private static void Finish()
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            EditorApplication.update -= TickPreview;
            EditorApplication.update -= TickWorld;
            EditorApplication.Exit(0);
        }
        private static void Fail(Exception error)
        {
            Directory.CreateDirectory(Output);
            File.AppendAllText(ResultsFile, "FAIL step " + step + ": " + error + "\n");
            Debug.LogException(error);
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            EditorApplication.update -= TickPreview;
            EditorApplication.update -= TickWorld;
            EditorApplication.Exit(1);
        }
        private static void Capture(PauseMenu menu, string label)
        {
            var cameraObject = new GameObject("Portrait capture");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.cullingMask = 1 << 5;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.12f, 0.15f, 0.2f);
            var target = new RenderTexture(1280, 720, 24);
            target.Create(); camera.targetTexture = target;
            Canvas canvas = menu.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output, label + ".png"), image.EncodeToPNG());
            RenderTexture.active = previous;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
            camera.targetTexture = null; target.Release();
            Object.DestroyImmediate(image); Object.DestroyImmediate(target); Object.DestroyImmediate(cameraObject);
        }
    }
}
