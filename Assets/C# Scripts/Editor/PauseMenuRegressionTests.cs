using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    [InitializeOnLoad]
    public static class PauseMenuRegressionTests
    {
        private const string Pending = "TapTap.PauseMenuRegression.Pending";
        private static int stage, lastFrame;
        private static double deadline;
        private static string Results => Path.Combine(Application.dataPath, "../Logs/PauseMenu/results.txt");

        static PauseMenuRegressionTests()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
                stage = 0;
                lastFrame = -1;
                deadline = EditorApplication.timeSinceStartup + 90;
                EditorApplication.update += Tick;
            };
        }

        public static void BuildAndRunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
            PauseMenuAssets.Build();
            RunBatch();
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
            Directory.CreateDirectory(Path.GetDirectoryName(Results));
            File.WriteAllText(Results, "");
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PauseMenuAssets.PrefabPath);
                Check(prefab != null, "Prefab missing");
                var menu = prefab.GetComponent<PauseMenu>();
                Check(menu.Content != null && menu.ResumeButton != null && menu.MainMenuButton != null
                    && menu.MusicSlider != null && menu.EffectsSlider != null && menu.BackgroundMusic != null
                    && menu.ClickSound != null, "Prefab references incomplete");
                Check(prefab.GetComponent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay, "Canvas needs a scene camera");
                Check(prefab.GetComponentsInChildren<Camera>(true).Length == 0
                    && prefab.GetComponentsInChildren<EventSystem>(true).Length == 0, "Prefab contains duplicate scene services");
                EditorSceneManager.OpenScene(PauseMenuAssets.ScenePath);
                Check(PrefabUtility.IsPartOfPrefabInstance(Object.FindObjectOfType<PauseMenu>()), "Fqh Canvas is not a prefab instance");
                Pass("Prefab is linked in Fqh, all controls/audio assigned, no camera/EventSystem scene references");
                SavePreference(MenuAudio.MusicPreference, "Music");
                SavePreference(MenuAudio.EffectsPreference, "Effects");
                SessionState.SetBool(Pending, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Pause menu regression timed out");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                PauseMenu menu = PauseMenu.Current;
                if (stage == 0)
                {
                    if (menu == null || MenuAudio.Current == null) return;
                    Check(!PauseMenu.IsOpen && !menu.Content.activeSelf, "Menu did not start closed");
                    Check(MenuAudio.Current.MusicSource.loop && MenuAudio.Current.MusicSource.clip.length > 20f,
                        "Looping BGM missing");
                    Time.timeScale = 2f;
                    EffectManager.Instance.Play(PlayerEffect.Joined, null);
                    float expectedScale = Time.timeScale;
                    menu.Toggle();
                    Check(PauseMenu.IsOpen && menu.Content.activeSelf && Time.timeScale == 0f, "Escape toggle did not pause");
                    EffectManager.Instance.Simulate(2f);
                    Check(Time.timeScale == 0f, "Hit-stop effect overrode pause");
                    var input = new GameObject("Pause Input Test").AddComponent<PlayerInput>();
                    input.SetExternalInput(1f, true);
                    Check(input.Horizontal == 0f && !input.SpaceHeld && !input.TryConsume(out _), "Gameplay input leaked through pause");
                    Check(!SceneResetManager.Instance.ResetCurrentScene(), "R reset was accepted while paused");
                    Object.DestroyImmediate(input.gameObject);
                    menu.MusicSlider.value = 0.23f;
                    menu.EffectsSlider.value = 0.81f;
                    Near(MenuAudio.Current.MusicSource.volume, 0.23f, "Music gain");
                    Near(MenuAudio.Current.EffectsSource.volume, 0.81f, "Effects gain");
                    menu.MusicSlider.value = 0f;
                    Near(MenuAudio.Current.MusicSource.volume, 0f, "Music mute");
                    Near(MenuAudio.Current.EffectsSource.volume, 0.81f, "Music changed effects gain");
                    menu.MusicSlider.value = 0.23f;
                    Capture(menu, 1920, 1080);
                    Capture(menu, 1280, 1024);
                    CheckHit(menu.ResumeButton.GetComponent<RectTransform>(), menu.ResumeButton.gameObject);
                    CheckHit(menu.MainMenuButton.GetComponent<RectTransform>(), menu.MainMenuButton.gameObject);
                    CheckHit(menu.MusicSlider.GetComponent<RectTransform>(), menu.MusicSlider.gameObject);
                    menu.Toggle();
                    Check(!PauseMenu.IsOpen && !menu.Content.activeSelf && PauseMenu.BlocksInput, "Close did not consume the input frame");
                    Near(Time.timeScale, expectedScale, "Restore pre-pause time scale");
                    EffectManager.Instance.Simulate(2f);
                    Near(Time.timeScale, 2f, "Hit stop failed to resume");
                    Near(PlayerPrefs.GetFloat(MenuAudio.MusicPreference), 0.23f, "Music preference");
                    Near(PlayerPrefs.GetFloat(MenuAudio.EffectsPreference), 0.81f, "Effects preference");
                    Pass("Toggle/close, pause time ownership, input/reset blocking, hit areas, separate gains/mute, saved volume, two aspect ratios");
                    stage = 1;
                }
                else if (stage == 1)
                {
                    Check(!PauseMenu.BlocksInput, "Input remained blocked on next frame");
                    menu.Open();
                    menu.ResumeButton.onClick.Invoke();
                    Check(!PauseMenu.IsOpen, "Continue button failed");
                    menu.Open();
                    Object.DestroyImmediate(menu.gameObject);
                    Near(Time.timeScale, 2f, "Destroying an open menu left the game paused");
                    Object.DestroyImmediate(MenuAudio.Current.gameObject);
                    foreach (EventSystem system in Object.FindObjectsOfType<EventSystem>()) Object.DestroyImmediate(system.gameObject);
                    var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PauseMenuAssets.PrefabPath));
                    menu = instance.GetComponent<PauseMenu>();
                    Check(menu.Open() && EventSystem.current != null, "Prefab failed without an existing EventSystem");
                    Check(Object.FindObjectsOfType<EventSystem>().Length == 1, "Duplicate EventSystem");
                    Near(menu.MusicSlider.value, 0.23f, "Music not restored after recreation");
                    Near(menu.EffectsSlider.value, 0.81f, "Effects not restored after recreation");
                    var duplicate = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PauseMenuAssets.PrefabPath));
                    Check(!duplicate.activeSelf && Object.FindObjectsOfType<MenuAudio>().Length == 1, "Duplicate menu/audio not suppressed");
                    Object.DestroyImmediate(duplicate);
                    menu.MainMenuScenePath = "Assets/Scenes/DoesNotExist.unity";
                    menu.ReturnToMainMenu();
                    Check(PauseMenu.IsOpen && !string.IsNullOrEmpty(menu.Notice.text), "Missing main scene did not recover with notice");
                    menu.MainMenuScenePath = "Assets/Scenes/WorldMaps/World_Character_Select.unity";
                    menu.MainMenuButton.onClick.Invoke();
                    Check(WorldMapSceneRouter.IsLoading && !PauseMenu.IsOpen, "Main menu button did not start scene navigation");
                    Pass("Continue button, disable/destroy restoration, portable EventSystem, saved volume reload, duplicate suppression, missing-scene recovery");
                    stage = 2;
                }
                else if (stage == 2)
                {
                    if (WorldMapSceneRouter.IsLoading || WorldLevelMenu.Current == null) return;
                    Check(SceneManager.GetActiveScene().path == "Assets/Scenes/WorldMaps/World_Character_Select.unity"
                        && Time.timeScale > 0f, "Return to selection left wrong scene or paused clock");
                    Check(MenuAudio.Current != null && Object.FindObjectsOfType<MenuAudio>().Length == 1, "BGM did not survive scene change");
                    var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PauseMenuAssets.PrefabPath));
                    menu = instance.GetComponent<PauseMenu>();
                    Check(menu.Open(), "Cannot pause in selection scene");
                    menu.ReturnToMainMenu();
                    Check(!WorldMapSceneRouter.IsLoading && !PauseMenu.IsOpen, "Main-menu button reloaded its current scene");
                    Pass("Real Fqh-to-selection scene navigation restores time, preserves one BGM player, same-scene main-menu action resumes");
                    Finish(null);
                }
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static void CheckHit(RectTransform rect, GameObject expected)
        {
            Canvas.ForceUpdateCanvases();
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Check(hits.Count > 0 && hits[0].gameObject == expected,
                "Wrong click target for " + expected.name + ": " + (hits.Count > 0 ? hits[0].gameObject.name : "none")
                + ", point=" + point + ", screen=" + Screen.width + "x" + Screen.height
                + ", canvas=" + rect.GetComponentInParent<Canvas>().pixelRect
                + ", culled=" + expected.GetComponent<CanvasRenderer>().cull);
        }

        private static void Capture(PauseMenu menu, int width, int height)
        {
            var cameraObject = new GameObject("Pause Menu Preview Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.15f, 0.2f);
            camera.cullingMask = 1 << 5;
            var target = new RenderTexture(width, height, 24);
            target.Create(); camera.targetTexture = target;
            Canvas canvas = menu.GetComponent<Canvas>();
            CanvasScaler scaler = menu.GetComponent<CanvasScaler>();
            float oldScale = canvas.scaleFactor;
            scaler.enabled = false;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1f;
            canvas.scaleFactor = Mathf.Min(width / 1920f, height / 1080f);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Results), "pause-" + width + "x" + height + ".png"), image.EncodeToPNG());
            RenderTexture.active = previous;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
            canvas.scaleFactor = oldScale; scaler.enabled = true;
            Object.DestroyImmediate(image); target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(cameraObject);
            Canvas.ForceUpdateCanvases();
        }

        private static void SavePreference(string key, string suffix)
        {
            SessionState.SetBool(Pending + suffix + "Exists", PlayerPrefs.HasKey(key));
            SessionState.SetFloat(Pending + suffix, PlayerPrefs.GetFloat(key));
        }

        private static void RestorePreference(string key, string suffix)
        {
            if (SessionState.GetBool(Pending + suffix + "Exists", false)) PlayerPrefs.SetFloat(key, SessionState.GetFloat(Pending + suffix, 0f));
            else PlayerPrefs.DeleteKey(key);
        }

        private static void Finish(Exception exception)
        {
            if (SessionState.GetBool(Pending, false))
            {
                RestorePreference(MenuAudio.MusicPreference, "Music");
                RestorePreference(MenuAudio.EffectsPreference, "Effects");
                PlayerPrefs.Save();
            }
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            if (exception != null) { File.AppendAllText(Results, "FAIL: " + exception + "\n"); Debug.LogException(exception); }
            EditorApplication.Exit(exception == null ? 0 : 1);
        }

        private static void Near(float actual, float expected, string name) => Check(Mathf.Abs(actual - expected) < 0.001f, name);
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Pass(string message) { File.AppendAllText(Results, "PASS: " + message + "\n"); Debug.Log(message); }
    }
}
