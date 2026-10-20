using System;
using System.IO;
using System.Reflection;
using DG.Tweening;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TapTap.Hints.Editor
{
    // Authors and exercises the HUD only in an isolated batch-mode project copy.
    [InitializeOnLoad]
    public static class HintReplayUiRegressionTests
    {
        private const string Pending = "TapTap.Hints.UIRegression";
        private const string Folder = "Assets/HintUiValidation";
        private static string Output => Path.GetFullPath("Logs/HintUI");
        private static int step, lastFrame;
        private static double started, deadline;
        private static PauseMenu pause;
        private static HintReplayPanel view;
        private static HintReplayDirector director;
        private static HintReplayPlayer playback;
        private static HintReplayCatalog catalog;
        private static HintReplayClip clip;
        private static GameObject firstGhost;

        static HintReplayUiRegressionTests()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
                step = 0; lastFrame = -1; started = EditorApplication.timeSinceStartup;
                deadline = started + 90;
                EditorApplication.update += Tick;
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project copy.");
            try
            {
                Directory.CreateDirectory(Output);
                File.WriteAllText(Path.Combine(Output, "results.txt"), "");
                HintReplayUiAssets.Build();
                Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var character = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Player.prefab"));
                character.GetComponent<PlayerInput>().UseKeyboard = false;
                character.GetComponent<PlayerController>().TeleportJoined(new Vector2(2f, 2f));
                var floor = new GameObject("Test Floor");
                floor.transform.position = new Vector3(20f, -0.5f, 0f);
                floor.AddComponent<BoxCollider2D>().size = new Vector2(200f, 1f);
                floor.AddComponent<WorldSurface>();
                var source = AssetDatabase.LoadAssetAtPath<HintReplayCatalog>("Assets/Tints/World_Character_HintCatalog.asset");
                Check(source != null && source.Entries.Count == 19, "Real 19-level hint catalog is available");
                new GameObject("Test Region").AddComponent<LevelRegion>().Source = source.Entries[0].Level;
                var owner = new GameObject("Hint Replay System").AddComponent<HintReplayDirector>();
                owner.UseKeyboard = false; owner.Catalog = source;
                PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HintReplayUiAssets.PausePrefabPath));
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Folder + "/Flow.unity");
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
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("UI tests timed out at step " + step);
                double elapsed = EditorApplication.timeSinceStartup - started;
                if (step == 0)
                {
                    pause = Object.FindObjectOfType<PauseMenu>(); view = Object.FindObjectOfType<HintReplayPanel>();
                    director = Object.FindObjectOfType<HintReplayDirector>(); playback = director.GetComponent<HintReplayPlayer>();
                    catalog = director.Catalog; clip = director.CurrentClip;
                    Check(director.CanRequestHint && clip != null, "Current real recording can be requested");
                    Check(!view.PanelRoot.activeSelf && !view.PanelGroup.blocksRaycasts, "Prompt starts hidden and does not intercept clicks");
                    var hintRect = (RectTransform)view.OpenButton.transform;
                    var pauseRect = (RectTransform)pause.OpenButton.transform;
                    Check(hintRect.anchoredPosition.x < pauseRect.anchoredPosition.x && hintRect.anchoredPosition.y == pauseRect.anchoredPosition.y,
                        "Hint icon sits immediately to the left of pause on the same row");
                    foreach (Button button in new[] { view.OpenButton, pause.OpenButton })
                        Check(button.GetComponent<PauseIconHover>() != null && button.GetComponent<PauseIconOutline>() != null,
                            button.name + " has the fading white outline");
                    director.Catalog = null;
                    Check(!view.TryOpen() && !view.PanelRoot.activeSelf, "Missing hint never opens the confirmation UI");
                    director.Catalog = catalog;
                    view.OpenButton.onClick.Invoke();
                    Check(view.IsOpen && !playback.IsPlaying && Time.timeScale == 1f, "Icon opens confirmation without starting replay or pausing gameplay");
                    SeekFade(view, "fade", view.FadeDuration * 0.5f);
                    Check(view.PanelGroup.alpha > 0f && view.PanelGroup.alpha < 1f, "Prompt fades in through an intermediate alpha");
                    SeekFade(view, "fade", 99f);
                    var caption = view.PanelRoot.GetComponentInChildren<TextMeshProUGUI>();
                    caption.ForceMeshUpdate();
                    Check(caption.text == HintReplayUiAssets.Question && caption.font == AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/text(SMALL) SDF.asset"),
                        "Prompt uses the exact requested question and text(SMALL) SDF font");
                    foreach (char letter in HintReplayUiAssets.Question)
                        Check(caption.font.HasCharacter(letter, true, true), "Question glyph available: " + letter);
                    Capture(1920, 1080, "prompt-1920x1080"); Capture(1280, 1024, "prompt-1280x1024");
                    var pointer = new PointerEventData(EventSystem.current);
                    view.OpenButton.GetComponent<PauseIconHover>().OnPointerEnter(pointer);
                    pause.OpenButton.GetComponent<PauseIconHover>().OnPointerEnter(pointer);
                    Next();
                }
                else if (step == 1 && elapsed > 0.2)
                {
                    Check(view.OpenButton.GetComponent<PauseIconOutline>().effectColor.a > 0.99f
                        && pause.OpenButton.GetComponent<PauseIconOutline>().effectColor.a > 0.99f, "Both icons fade to fully visible white outlines on hover");
                    Capture(1920, 1080, "hover-1920x1080");
                    var pointer = new PointerEventData(EventSystem.current);
                    view.OpenButton.GetComponent<PauseIconHover>().OnPointerExit(pointer);
                    pause.OpenButton.GetComponent<PauseIconHover>().OnPointerExit(pointer);
                    view.CancelButton.onClick.Invoke();
                    SeekFade(view, "fade", view.FadeDuration * 0.5f);
                    Check(view.PanelGroup.alpha > 0f && view.PanelGroup.alpha < 1f && !view.PanelGroup.interactable, "Closing fades out and prevents repeated submission");
                    SeekFade(view, "fade", 99f);
                    Check(!view.PanelRoot.activeSelf && !view.PanelGroup.blocksRaycasts && !playback.IsPlaying, "Cancel closes without playing and releases UI raycasts");
                    Check(view.TryOpen(), "Prompt reopens after cancel"); SeekFade(view, "fade", 99f);
                    view.DismissButton.onClick.Invoke(); SeekFade(view, "fade", 99f);
                    Check(!view.IsOpen && !playback.IsPlaying, "Clicking outside dismisses the prompt");
                    Check(view.TryOpen(), "Prompt can open before confirmation"); SeekFade(view, "fade", 99f);
                    view.ConfirmButton.onClick.Invoke();
                    Check(view.IsClosing && !playback.IsPlaying, "Confirmation finishes the fade before starting the replay");
                    SeekFade(view, "fade", 99f);
                    Check(playback.IsPlaying && playback.PlayingClip == clip && !view.PanelRoot.activeSelf, "Confirm starts the selected real recording through the existing replay director");
                    firstGhost = playback.GhostRoot;
                    view.ConfirmButton.onClick.Invoke();
                    Check(playback.GhostRoot == firstGhost, "Repeated confirm does not restart playback");
                    director.StopHint();
                    Check(view.TryOpen(), "Prompt opens for stale-selection check"); SeekFade(view, "fade", 99f);
                    view.Confirm(); director.Catalog = null; SeekFade(view, "fade", 99f);
                    Check(!playback.IsPlaying, "A changed selection during close never plays the stale recording");
                    director.Catalog = catalog;
                    Check(view.TryOpen(), "Prompt opens before pause"); SeekFade(view, "fade", 99f);
                    Check(pause.Open(), "Existing pause menu opens while the hint prompt is visible");
                    Next();
                }
                else if (step == 2 && elapsed > 0.25)
                {
                    Check(!view.IsOpen && !view.PanelRoot.activeSelf && !view.PanelGroup.blocksRaycasts && !view.OpenButton.gameObject.activeSelf,
                        "Pause fades away the hint prompt, hides its icon, and owns the UI");
                    Check(!view.TryOpen() && !playback.IsPlaying, "Paused gameplay cannot open or start a hint");
                    SeekFade(pause, "transition", 99f); pause.Resume(); SeekFade(pause, "transition", 99f);
                    Next();
                }
                else if (step == 3 && elapsed > 0.1)
                {
                    Check(!PauseMenu.IsOpen && Time.timeScale == 1f && view.OpenButton.gameObject.activeSelf,
                        "Resume restores gameplay and both HUD icons");
                    Check(view.OpenButton.GetComponent<PauseIconOutline>().effectColor.a < 0.01f
                        && pause.OpenButton.GetComponent<PauseIconOutline>().effectColor.a < 0.01f, "Hover outlines disappear after pointer exit or hiding");
                    Check(director.RequestHint() && playback.PlayingClip == clip, "Direct H-key playback entry point remains usable");
                    director.StopHint(); director.enabled = false;
                    Next();
                }
                else if (step == 4 && elapsed > 0.1)
                {
                    Check(!view.TryOpen(), "Disabled hint system cannot open a prompt");
                    Object.DestroyImmediate(director.gameObject); Next();
                }
                else if (step == 5 && elapsed > 0.1)
                {
                    Check(!view.OpenButton.gameObject.activeSelf && pause.OpenButton.gameObject.activeSelf,
                        "Scenes without a replay director keep pause and hide the hint icon");
                    EditorApplication.update -= Tick; SessionState.SetBool(Pending, false);
                    File.AppendAllText(Path.Combine(Output, "results.txt"), "PASS: all hint HUD checks completed\n");
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception error) { Fail(error); }
        }

        private static void SeekFade(object owner, string field, float position)
        {
            var tween = (Tween)owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
            if (tween != null && tween.IsActive()) tween.Goto(position, false);
        }
        private static void Capture(int width, int height, string name)
        {
            var cameraObject = new GameObject("Hint HUD Preview Camera");
            Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.12f, 0.15f, 0.2f);
            camera.cullingMask = 1 << 5;
            var target = new RenderTexture(width, height, 24); target.Create(); camera.targetTexture = target;
            Canvas canvas = pause.GetComponent<Canvas>(); CanvasScaler scaler = pause.GetComponent<CanvasScaler>();
            float scale = canvas.scaleFactor; scaler.enabled = false;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
            canvas.scaleFactor = Mathf.Min(width / 1920f, height / 1080f);
            Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Output, name + ".png"), pixels.EncodeToPNG());
            RenderTexture.active = previous; canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
            canvas.scaleFactor = scale; scaler.enabled = true; camera.targetTexture = null; target.Release();
            Object.DestroyImmediate(target); Object.DestroyImmediate(pixels); Object.DestroyImmediate(cameraObject);
        }
        private static void Next() { step++; started = EditorApplication.timeSinceStartup; }
        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            File.AppendAllText(Path.Combine(Output, "results.txt"), "PASS: " + description + "\n");
        }
        private static void Fail(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Pending, false);
            Directory.CreateDirectory(Output);
            File.AppendAllText(Path.Combine(Output, "results.txt"), "FAIL at step " + step + ": " + error + "\n");
            Debug.LogException(error); EditorApplication.Exit(1);
        }
    }
}
