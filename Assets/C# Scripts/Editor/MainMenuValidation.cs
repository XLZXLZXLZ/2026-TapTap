using System;
using System.IO;
using System.Reflection;
using ScreenFilters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    [InitializeOnLoad]
    public static class MainMenuValidation
    {
        private const string Pending = "TapTap.MainMenu.Validation";
        private const string Results = "Logs/MainMenu/validation.txt";
        private static int stage;
        private static double deadline, readyAt;
        private static Vector3 cameraPosition;
        private static Quaternion cameraRotation;

        static MainMenuValidation()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                {
                    stage = 0;
                    readyAt = EditorApplication.timeSinceStartup + 3;
                    deadline = EditorApplication.timeSinceStartup + 150;
                }
            };
        }

        [MenuItem("TapTap/UI/Main Menu/Validate %&v")]
        public static void Run()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != MainMenuAssets.ScenePath)
                throw new InvalidOperationException("Open MainMenu in Edit Mode first.");
            Check(Object.FindObjectOfType<MainMenuController>() != null, "Build the menu first.");
            Directory.CreateDirectory("Logs/MainMenu");
            File.WriteAllText(Results, "Unity " + Application.unityVersion + "\n");
            cameraPosition = Camera.main.transform.localPosition;
            cameraRotation = Camera.main.transform.localRotation;
            SessionState.SetString(Pending + ".Pose", JsonUtility.ToJson(new PoseData {
                position = cameraPosition, rotation = cameraRotation }));
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        public static void BuildAndRunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            EditorSceneManager.OpenScene(MainMenuAssets.ScenePath);
            MainMenuAssets.Build();
            Run();
        }

        public static void RunExistingBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            EditorSceneManager.OpenScene(MainMenuAssets.ScenePath);
            Run();
        }

        [Serializable] private sealed class PoseData { public Vector3 position; public Quaternion rotation; }

        private static void Tick()
        {
            if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || deadline == 0) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Main menu validation timed out.");
                if (EditorApplication.timeSinceStartup < readyAt) return;
                if (stage == 0)
                {
                    MainMenuController menu = Object.FindObjectOfType<MainMenuController>();
                    Check(menu != null, "Menu was not instantiated.");
                    Camera camera = Camera.main;
                    MainMenuCameraMotion motion = camera.GetComponent<MainMenuCameraMotion>();
                    MainMenuBlurCamera blur = camera.GetComponent<MainMenuBlurCamera>();
                    Check(motion != null && blur != null, "Camera components are missing.");
                    Check(EventSystem.current != null, "EventSystem is missing.");
                    Check(menu.StartButton.image.sprite.name == "MainMenu_Start", "Start sprite was not sliced.");
                    Check(menu.SelectScenePath.EndsWith("World_Character_Select.unity"), "Incorrect select destination.");

                    // Exercise mouse response and unscaled breathing; disabling restores the authored pose.
                    motion.enabled = false;
                    PoseData pose = JsonUtility.FromJson<PoseData>(SessionState.GetString(Pending + ".Pose", ""));
                    Check(Vector3.Distance(camera.transform.localPosition, pose.position) < 0.0001f,
                        "Camera position did not restore.");
                    Check(Quaternion.Angle(camera.transform.localRotation, pose.rotation) < 0.001f,
                        "Camera rotation did not restore.");
                    motion.enabled = true;
                    MethodInfo apply = typeof(MainMenuCameraMotion).GetMethod("ApplyPose", BindingFlags.Instance | BindingFlags.NonPublic);
                    Time.timeScale = 0f;
                    apply.Invoke(motion, new object[] { 2.5f, Vector2.one, 1f });
                    Check(Quaternion.Angle(camera.transform.localRotation, pose.rotation) > 0.1f,
                        "Unscaled camera motion or mouse parallax did not respond.");
                    Check(Quaternion.Angle(camera.transform.localRotation, pose.rotation) < 2f,
                        "Camera motion exceeds the subtle motion envelope.");
                    motion.enabled = false;
                    Time.timeScale = 1f;
                    Pass("Camera breath and mouse response stay subtle, use unscaled time, and restore the authored pose.");

                    Canvas canvas = menu.GetComponent<Canvas>();
                    canvas.enabled = false;
                    blur.Amount = 0f;
                    Texture2D sharp = CaptureWorld(camera, 1920, 1080);
                    blur.Amount = 1f;
                    Texture2D blurred = CaptureWorld(camera, 1920, 1080);
                    File.WriteAllBytes("Logs/MainMenu/background-sharp.png", sharp.EncodeToPNG());
                    File.WriteAllBytes("Logs/MainMenu/background-blur.png", blurred.EncodeToPNG());
                    float left = Difference(sharp, blurred, new RectInt(80, 100, 400, 850));
                    float right = Difference(sharp, blurred, new RectInt(1250, 100, 550, 850));
                    Check(left > 0.001f, "Left background was not blurred: " + left);
                    Check(right < 0.0002f, "Right background changed unexpectedly: " + right);
                    Pass("Rendered blur affects left only; mean pixel differences left=" + left + ", right=" + right + ".");
                    Object.DestroyImmediate(sharp);
                    Object.DestroyImmediate(blurred);
                    canvas.enabled = true;
                    CaptureMenu(camera, menu, 1920, 1080);
                    CaptureMenu(camera, menu, 1280, 1024);
                    Check(menu.StartButton.navigation.selectOnDown == menu.SettingsButton
                        && menu.QuitButton.navigation.selectOnDown == menu.StartButton, "Keyboard navigation is incomplete.");
                    bool requested = false;
                    menu.SettingsRequested.AddListener(() => requested = true);
                    menu.SettingsButton.onClick.Invoke();
                    Check(requested, "Settings entry did not dispatch its event.");
                    Pass("Sliced artwork, 16:9 and 5:4 layout, keyboard navigation, and reserved settings event.");
                    menu.StartButton.onClick.Invoke();
                    Check(!menu.StartButton.interactable, "Start did not guard repeated clicks.");
                    stage = 1;
                }
                else if (SceneManager.GetActiveScene().name == "World_Character_Select" && !WorldMapSceneRouter.IsLoading)
                {
                    Check(Camera.main.GetComponent<MainMenuBlurCamera>() == null,
                        "Menu blur leaked into the select scene.");
                    Pass("Start button completes the real fade transition to World_Character_Select; menu camera effects are scene-local.");
                    Finish(null);
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private static Texture2D CaptureWorld(Camera camera, int width, int height)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture oldTarget = camera.targetTexture;
            RenderTexture oldActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                return image;
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static void CaptureMenu(Camera world, MainMenuController menu, int width, int height)
        {
            Canvas canvas = menu.GetComponent<Canvas>();
            CanvasScaler scaler = menu.GetComponent<CanvasScaler>();
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var uiTarget = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            GameObject uiObject = new GameObject("Menu validation UI camera", typeof(Camera));
            GameObject background = new GameObject("Validation scene background", typeof(RectTransform), typeof(RawImage));
            background.layer = 5;
            background.transform.SetParent(canvas.transform, false);
            background.transform.SetAsFirstSibling();
            var backgroundRect = (RectTransform)background.transform;
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = backgroundRect.offsetMax = Vector2.zero;
            background.GetComponent<RawImage>().texture = target;
            background.GetComponent<RawImage>().raycastTarget = false;
            Camera ui = uiObject.GetComponent<Camera>();
            ui.enabled = false;
            ui.clearFlags = CameraClearFlags.Depth;
            ui.cullingMask = 1 << 5;
            ui.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            RenderTexture oldTarget = world.targetTexture, oldActive = RenderTexture.active;
            float oldScale = canvas.scaleFactor;
            try
            {
                canvas.enabled = false;
                world.targetTexture = target;
                world.Render();
                canvas.enabled = true;
                scaler.enabled = false;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = ui;
                canvas.planeDistance = 1f;
                canvas.scaleFactor = Mathf.Min(width / 1920f, height / 1080f);
                ui.targetTexture = uiTarget;
                Canvas.ForceUpdateCanvases();
                ui.Render();
                foreach (Button button in new[] { menu.StartButton, menu.SettingsButton, menu.QuitButton })
                {
                    var corners = new Vector3[4];
                    button.GetComponent<RectTransform>().GetWorldCorners(corners);
                    foreach (Vector3 corner in corners)
                    {
                        Vector3 point = ui.WorldToViewportPoint(corner);
                        Check(point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1,
                            button.name + " is clipped at " + width + "x" + height + ": " + point);
                    }
                }
                RenderTexture.active = uiTarget;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes("Logs/MainMenu/menu-" + width + "x" + height + ".png", image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                canvas.scaleFactor = oldScale;
                scaler.enabled = true;
                world.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                Object.DestroyImmediate(uiObject);
                Object.DestroyImmediate(background);
                RenderTexture.ReleaseTemporary(target);
                RenderTexture.ReleaseTemporary(uiTarget);
                Canvas.ForceUpdateCanvases();
            }
        }

        private static float Difference(Texture2D first, Texture2D second, RectInt rect)
        {
            Color32[] a = first.GetPixels32(), b = second.GetPixels32();
            double sum = 0;
            int count = 0;
            for (int y = rect.yMin; y < rect.yMax; y += 3)
                for (int x = rect.xMin; x < rect.xMax; x += 3)
                {
                    int i = y * first.width + x;
                    sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
                    count += 3;
                }
            return (float)(sum / (count * 255.0));
        }

        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static void Pass(string message) => File.AppendAllText(Results, "PASS: " + message + "\n");
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Pending, false);
            if (error != null)
            {
                File.AppendAllText(Results, "FAIL: " + error + "\n");
                Debug.LogException(error);
            }
            else Debug.Log("Main menu validation passed. Captures and results: Logs/MainMenu");
            Time.timeScale = 1f;
            EditorApplication.isPlaying = false;
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
