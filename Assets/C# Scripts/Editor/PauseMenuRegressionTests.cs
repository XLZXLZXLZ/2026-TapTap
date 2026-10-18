using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DG.Tweening;
using ScreenFilters;
using System.Linq;
using System.Reflection;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    [InitializeOnLoad]
    public static class PauseMenuRegressionTests
    {
        private const string Pending = "TapTap.PauseMenuRegression.Pending";
        private const string InteractionOnly = Pending + ".InteractionOnly";
        private static int stage, lastFrame, captureFrame;
        private static double deadline, hoverStarted;
        private static string Results => Path.Combine(Application.dataPath, "../Logs/PauseMenu/results.txt");

        static PauseMenuRegressionTests()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
                stage = 0;
                lastFrame = -1;
                deadline = EditorApplication.timeSinceStartup + 90;
                if (SessionState.GetBool(InteractionOnly, false)) EditorApplication.update += TickInteraction;
                else EditorApplication.update += Tick;
            };
        }

        public static void RunInteractionBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
            Directory.CreateDirectory(Path.GetDirectoryName(Results));
            File.WriteAllText(Results, "");
            SavePreference(MenuAudio.MusicPreference, "Music");
            SavePreference(MenuAudio.EffectsPreference, "Effects");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PauseMenu.prefab"));
            SessionState.SetBool(InteractionOnly, true);
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        private static void TickInteraction()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Interaction regression timed out");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                PauseMenu menu = PauseMenu.Current;
                if (menu == null) return;
                if (stage == 0)
                {
                    CheckInteraction(menu.OpenButton, null);
                    Time.timeScale = 2f;
                    Check(menu.Open(), "Menu failed to open");
                    Advance(menu, 99f);
                    Check(EventSystem.current.currentSelectedGameObject == menu.ResumeButton.gameObject, "Keyboard selection lost");
                    foreach (Selectable control in menu.Content.GetComponentsInChildren<Selectable>())
                    {
                        Check(control.transition == Selectable.Transition.None, control.name + " has color transitions");
                        Near(control.GetComponent<UIInteractionScale>().Target.localScale.x, 1f, control.name + " initial scale");
                        CheckInteraction(control, menu.ContentGroup);
                    }
                    menu.ResumeButton.GetComponent<UIInteractionScale>().OnPointerEnter(new PointerEventData(EventSystem.current));
                    hoverStarted = EditorApplication.timeSinceStartup;
                    stage = 1;
                }
                else if (EditorApplication.timeSinceStartup > hoverStarted + 0.2)
                {
                    Near(Time.timeScale, 0f, "Game must remain paused during feedback");
                    Near(menu.ResumeButton.GetComponent<UIInteractionScale>().Target.localScale.x, 1.05f, "Hover tween did not run while paused");
                    Capture(menu, 1920, 1080, "interaction-hover");
                    menu.Resume();
                    foreach (Selectable control in menu.Content.GetComponentsInChildren<Selectable>())
                    {
                        Check(!control.IsInteractable(), control.name + " remains interactive during exit");
                        Check(control.targetGraphic.canvasRenderer.GetColor() == Color.white, control.name + " tinted during exit");
                        var feedback = control.GetComponent<UIInteractionScale>();
                        feedback.SendMessage("LateUpdate");
                        SettleScale(feedback);
                        Near(feedback.Target.localScale.x, 1f, control.name + " exit scale");
                    }
                    Advance(menu, 0.15f);
                    menu.Toggle();
                    Advance(menu, 99f);
                    Check(menu.ContentGroup.interactable && !menu.IsClosing, "Interrupted exit did not restore interaction");
                    menu.Resume();
                    Advance(menu, 99f);
                    Near(Time.timeScale, 2f, "Resume time scale");
                    Check(menu.Open(), "Reopen failed");
                    Advance(menu, 99f);
                    Near(menu.ResumeButton.GetComponent<UIInteractionScale>().Target.localScale.x, 1f, "Reopen retained hover/press scale");
                    Capture(menu, 1920, 1080, "interaction-normal");
                    menu.Resume();
                    Advance(menu, 99f);
                    Pass("Five prefab controls: neutral colors; hover/press/release/drag/disable/reset; unscaled paused tween; actual exit, interrupted exit, reopen and time restoration");
                    Finish(null);
                }
            }
            catch (Exception exception) { Finish(exception); }
        }

        public static void BuildAndRunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
            RunBatch();
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
            SessionState.SetBool(InteractionOnly, false);
            Directory.CreateDirectory(Path.GetDirectoryName(Results));
            File.WriteAllText(Results, "");
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PauseMenu.prefab");
                Check(prefab != null, "Prefab missing");
                var menu = prefab.GetComponent<PauseMenu>();
                Check(menu.Content != null && menu.ResumeButton != null && menu.MainMenuButton != null
                    && menu.MusicSlider != null && menu.EffectsSlider != null && menu.BackgroundMusic != null
                    && menu.ClickSound != null, "Prefab references incomplete");
                Check(prefab.GetComponent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay, "Canvas needs a scene camera");
                Check(prefab.GetComponentsInChildren<Camera>(true).Length == 0
                    && prefab.GetComponentsInChildren<EventSystem>(true).Length == 0, "Prefab contains duplicate scene services");
                Check(menu.OpenButton != null && menu.LeftArt != null && menu.RightRows.Length == 4
                    && menu.ContentGroup != null && menu.PanelGroup != null && menu.BackdropGroup != null, "Animation references missing");
                foreach (Selectable control in prefab.GetComponentsInChildren<Selectable>(true))
                {
                    Check(control.transition == Selectable.Transition.None, control.name + " still uses a color transition");
                    ColorBlock colors = control.colors;
                    Check(colors.normalColor == Color.white && colors.highlightedColor == Color.white
                        && colors.pressedColor == Color.white && colors.selectedColor == Color.white
                        && colors.disabledColor == Color.white, control.name + " still has a state tint");
                    Check(control.GetComponent<UIInteractionScale>()?.Target != null, control.name + " has no scale target");
                }
                Check(menu.OpenButton.GetComponent<PauseIconHover>() == null, "HUD still uses color/outline hover feedback");
                Check(!prefab.GetComponentsInChildren<Text>(true).Any(label => label.text.Contains("ESC")), "Escape hint remains");
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Rendering/Settings/TapTapRenderer.asset");
                var blur = renderer.rendererFeatures.OfType<PauseBlurFeature>().Single();
                Check(blur.BlurShader != null && !ShaderUtil.ShaderHasError(blur.BlurShader), "Blur shader invalid");
                EditorSceneManager.OpenScene("Assets/Scenes/WorldMaps/World_Character_Play.unity");
                Check(Object.FindObjectsOfType<PauseMenu>().Length == 1
                    && PrefabUtility.GetCorrespondingObjectFromSource(Object.FindObjectOfType<PauseMenu>()) == menu,
                    "World_Character_Play needs exactly one linked pause prefab");
                EditorSceneManager.OpenScene("Assets/Art/Sprite/UI/Fqh.unity");
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
                    Check(EventSystem.current != null && menu.OpenButton.gameObject.activeInHierarchy, "HUD cannot receive clicks before Escape");
                    CheckHit(menu.OpenButton.GetComponent<RectTransform>(), menu.OpenButton.gameObject);
                    Check(MenuAudio.Current.MusicSource.loop && MenuAudio.Current.MusicSource.clip.length > 20f,
                        "Looping BGM missing");
                    Time.timeScale = 2f;
                    EffectManager.Instance.Play(PlayerEffect.Joined, null);
                    float expectedScale = Time.timeScale;
                    menu.OpenButton.onClick.Invoke();
                    Check(PauseMenu.IsOpen && menu.Content.activeSelf && Time.timeScale == 0f, "Escape toggle did not pause");
                    Check(menu.LeftArt.anchoredPosition.y > 500f && menu.RightRows.All(row => row.anchoredPosition.x > 500f)
                        && menu.PanelGroup.alpha == 0f && PauseBlurFeature.Amount == 0f, "Entrance did not start offscreen");
                    Advance(menu, 0.15f);
                    Check(menu.RightRows[0].anchoredPosition.x < menu.RightRows[1].anchoredPosition.x
                        && menu.RightRows[1].anchoredPosition.x < menu.RightRows[2].anchoredPosition.x,
                        "Entrance rows are not staggered");
                    Check(menu.PanelGroup.alpha > 0f && menu.PanelGroup.alpha < 1f
                        && PauseBlurFeature.Amount > 0f && PauseBlurFeature.Amount < 1f, "Fades do not interpolate while paused");
                    Advance(menu, 99f);
                    Check(menu.LeftArt.anchoredPosition == Vector2.zero && menu.RightRows.All(row => row.anchoredPosition == Vector2.zero), "Entrance did not settle");
                    Check(EventSystem.current.currentSelectedGameObject == menu.ResumeButton.gameObject,
                        "Initial keyboard selection was removed");
                    Near(menu.ResumeButton.GetComponent<UIInteractionScale>().Target.localScale.x, 1f,
                        "Automatic selection must not enlarge the resume artwork");
                    CheckInteraction(menu.ResumeButton, menu.ContentGroup);
                    CheckInteraction(menu.MainMenuButton, menu.ContentGroup);
                    CheckInteraction(menu.MusicSlider, menu.ContentGroup);
                    CheckInteraction(menu.EffectsSlider, menu.ContentGroup);
                    Pass("Auto-selection preserves color/scale; hover, press, release, pointer exit, group disable and reopen preserve artwork colors and hit areas");
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
                    Check(menu.IsClosing && PauseMenu.BlocksInput && Time.timeScale == 0f && menu.Content.activeSelf,
                        "Exit restored game before animation finished");
                    Advance(menu, 0.15f);
                    Check(menu.LeftArt.anchoredPosition.x < 0f && menu.RightRows[0].anchoredPosition.x > menu.RightRows[1].anchoredPosition.x,
                        "Exit direction/stagger wrong");
                    Vector2 interrupted = menu.LeftArt.anchoredPosition;
                    menu.Toggle();
                    Check(!menu.IsClosing && menu.LeftArt.anchoredPosition == interrupted, "Rapid Escape jumped or lost pause ownership");
                    Advance(menu, 99f);
                    menu.Resume();
                    Advance(menu, 99f);
                    Check(!PauseMenu.IsOpen && !menu.Content.activeSelf && PauseMenu.BlocksInput, "Close did not consume the input frame");
                    Check(PauseBlurFeature.Amount == 0f && menu.OpenButton.gameObject.activeInHierarchy
                        && EventSystem.current != null, "Close left blur or disabled HUD events");
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
                    Advance(menu, 99f);
                    menu.ResumeButton.onClick.Invoke();
                    Advance(menu, 99f);
                    Check(!PauseMenu.IsOpen, "Continue button failed");
                    menu.Open();
                    Object.DestroyImmediate(menu.gameObject);
                    Near(Time.timeScale, 2f, "Destroying an open menu left the game paused");
                    Object.DestroyImmediate(MenuAudio.Current.gameObject);
                    foreach (EventSystem system in Object.FindObjectsOfType<EventSystem>()) Object.DestroyImmediate(system.gameObject);
                    var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PauseMenu.prefab"));
                    menu = instance.GetComponent<PauseMenu>();
                    Check(menu.Open() && EventSystem.current != null, "Prefab failed without an existing EventSystem");
                    Check(Object.FindObjectsOfType<EventSystem>().Length == 1, "Duplicate EventSystem");
                    Near(menu.MusicSlider.value, 0.23f, "Music not restored after recreation");
                    Near(menu.EffectsSlider.value, 0.81f, "Effects not restored after recreation");
                    var duplicate = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PauseMenu.prefab"));
                    Check(!duplicate.activeSelf && Object.FindObjectsOfType<MenuAudio>().Length == 1, "Duplicate menu/audio not suppressed");
                    Object.DestroyImmediate(duplicate);
                    menu.MainMenuScenePath = "Assets/Scenes/DoesNotExist.unity";
                    menu.ReturnToMainMenu();
                    Advance(menu, 99f);
                    Check(PauseMenu.IsOpen && !string.IsNullOrEmpty(menu.Notice.text), "Missing main scene did not recover with notice");
                    menu.MainMenuScenePath = "Assets/Scenes/WorldMaps/World_Character_Select.unity";
                    menu.MainMenuButton.onClick.Invoke();
                    Advance(menu, 99f);
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
                    var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PauseMenu.prefab"));
                    menu = instance.GetComponent<PauseMenu>();
                    Check(menu.Open(), "Cannot pause in selection scene");
                    menu.ReturnToMainMenu();
                    Advance(menu, 99f);
                    Check(!WorldMapSceneRouter.IsLoading && !PauseMenu.IsOpen, "Main-menu button reloaded its current scene");
                    Pass("Real Fqh-to-selection scene navigation restores time, preserves one BGM player, same-scene main-menu action resumes");
                    WorldMapSceneRouter.Instance.Load("Assets/Scenes/WorldMaps/World_Character_Play.unity");
                    stage = 3;
                }
                else if (stage == 3)
                {
                    if (WorldMapSceneRouter.IsLoading || menu == null || SceneManager.GetActiveScene().path != "Assets/Scenes/WorldMaps/World_Character_Play.unity") return;
                    Check(menu.Open(), "World_Character_Play cannot pause");
                    Advance(menu, 99f);
                    menu.Content.SetActive(false);
                    PauseBlurFeature.Amount = 0f;
                    captureFrame = Time.frameCount;
                    stage = 4;
                }
                else if (stage == 4 && Time.frameCount > captureFrame + 3)
                {
                    CaptureWorld(menu, "world-sharp", false);
                    captureFrame = Time.frameCount; stage = 5;
                }
                else if (stage == 5 && Time.frameCount > captureFrame + 3)
                {
                    PauseBlurFeature.Amount = 1f;
                    captureFrame = Time.frameCount; stage = 6;
                }
                else if (stage == 6 && Time.frameCount > captureFrame + 3)
                {
                    CaptureWorld(menu, "world-blurred", false);
                    captureFrame = Time.frameCount; stage = 7;
                }
                else if (stage == 7 && Time.frameCount > captureFrame + 3)
                {
                    ValidateBlurCaptures();
                    menu.Content.SetActive(true);
                    captureFrame = Time.frameCount; stage = 8;
                }
                else if (stage == 8 && Time.frameCount > captureFrame + 3)
                {
                    CaptureWorld(menu, "world-pause", true);
                    menu.Resume();
                    captureFrame = Time.frameCount; stage = 9;
                }
                else if (stage == 9)
                {
                    if (PauseMenu.IsOpen || Time.frameCount <= captureFrame + 3) return;
                    Check(Time.timeScale > 0f && PauseBlurFeature.Amount == 0f, "World resume left blur or paused clock");
                    ExecuteEvents.Execute(menu.OpenButton.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
                    captureFrame = Time.frameCount; stage = 10;
                }
                else if (stage == 10 && Time.frameCount > captureFrame + 12)
                {
                    Near(menu.OpenButton.transform.localScale.x, 1.05f, "HUD hover scale while unscaled");
                    CheckInteraction(menu.OpenButton, null);
                    CaptureWorld(menu, "world-hud-hover", true);
                    captureFrame = Time.frameCount; stage = 11;
                }
                else if (stage == 11 && Time.frameCount > captureFrame + 3)
                {
                    Pass("World_Character_Play linked prefab, real URP sharp/blur/menu/HUD captures, animated resume and icon hover");
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

        private static void CheckInteraction(Selectable control, CanvasGroup group)
        {
            var feedback = control.GetComponent<UIInteractionScale>();
            var hit = (RectTransform)control.transform;
            Vector2 size = hit.sizeDelta, position = hit.anchoredPosition;
            Vector3 center = feedback.Target.position;
            Color color = control.targetGraphic.canvasRenderer.GetColor();
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            feedback.OnPointerEnter(pointer);
            SettleScale(feedback);
            Near(feedback.Target.localScale.x, 1.05f, control.name + " hover");
            Check(feedback.Target.position == center, control.name + " scale moved its visual center");
            feedback.OnPointerDown(pointer);
            SettleScale(feedback);
            Near(feedback.Target.localScale.x, 0.95f, control.name + " press");
            feedback.OnPointerExit(pointer);
            SettleScale(feedback);
            Near(feedback.Target.localScale.x, control is Slider ? 0.95f : 1f, control.name + " drag outside");
            feedback.OnPointerUp(pointer);
            SettleScale(feedback);
            Near(feedback.Target.localScale.x, 1f, control.name + " release outside");
            feedback.OnPointerEnter(pointer);
            feedback.OnPointerDown(pointer);
            feedback.OnPointerUp(pointer);
            SettleScale(feedback);
            Near(feedback.Target.localScale.x, 1.05f, control.name + " release inside");
            if (group != null)
            {
                group.interactable = false;
                feedback.SendMessage("LateUpdate");
                SettleScale(feedback);
                Near(feedback.Target.localScale.x, 1f, control.name + " disabled reset");
                feedback.OnPointerEnter(pointer);
                feedback.OnPointerDown(pointer);
                SettleScale(feedback);
                Near(feedback.Target.localScale.x, 1f, control.name + " disabled input");
                group.interactable = true;
                feedback.SendMessage("LateUpdate");
                SettleScale(feedback);
                Near(feedback.Target.localScale.x, 1f, control.name + " reopened reset");
            }
            feedback.OnPointerExit(pointer);
            SettleScale(feedback);
            Check(hit.sizeDelta == size && hit.anchoredPosition == position, control.name + " changed its hit area");
            Check(control.targetGraphic.canvasRenderer.GetColor() == color, control.name + " changed its artwork tint");
            feedback.OnPointerEnter(pointer);
            SettleScale(feedback);
            control.gameObject.SetActive(false);
            Near(feedback.Target.localScale.x, 1f, control.name + " hidden reset");
            control.gameObject.SetActive(true);
        }

        private static void SettleScale(UIInteractionScale feedback)
        {
            var tween = (Tween)typeof(UIInteractionScale).GetField("tween", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(feedback);
            if (tween != null && tween.IsActive()) tween.Complete();
        }

        private static void Advance(PauseMenu menu, float time)
        {
            var sequence = (Sequence)typeof(PauseMenu).GetField("transition", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
            Check(sequence != null && sequence.IsActive(), "Missing presentation sequence");
            sequence.Goto(Mathf.Min(time, sequence.Duration()), true);
        }

        private static void CaptureWorld(PauseMenu menu, string name, bool includeUI)
        {
            Camera camera = Camera.main;
            Check(camera != null, "World camera missing");
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var target = new RenderTexture(1920, 1080, 24);
            target.Create();
            camera.targetTexture = target;
            camera.Render();
            if (includeUI)
            {
                var background = new GameObject("Validation World Background", typeof(RectTransform), typeof(RawImage));
                background.layer = 5;
                background.transform.SetParent(menu.transform, false);
                background.transform.SetAsFirstSibling();
                var rect = (RectTransform)background.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                background.GetComponent<RawImage>().texture = target;
                Capture(menu, 1920, 1080, name);
                Object.DestroyImmediate(background);
            }
            else
            {
                RenderTexture.active = target;
                var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Results), name + ".png"), image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            target.Release(); Object.DestroyImmediate(target);
        }

        private static void ValidateBlurCaptures()
        {
            var sharp = new Texture2D(2, 2);
            var blurred = new Texture2D(2, 2);
            sharp.LoadImage(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Results), "world-sharp.png")));
            blurred.LoadImage(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Results), "world-blurred.png")));
            Color[] a = sharp.GetPixels(), b = blurred.GetPixels();
            float sharpEdges = 0f, blurEdges = 0f, min = 1f, max = 0f;
            for (int y = 1; y < sharp.height; y += 4)
                for (int x = 1; x < sharp.width; x += 4)
                {
                    int i = y * sharp.width + x;
                    sharpEdges += Mathf.Abs(a[i].grayscale - a[i - 1].grayscale);
                    blurEdges += Mathf.Abs(b[i].grayscale - b[i - 1].grayscale);
                    min = Mathf.Min(min, b[i].grayscale); max = Mathf.Max(max, b[i].grayscale);
                }
            Object.DestroyImmediate(sharp); Object.DestroyImmediate(blurred);
            Check(max - min > 0.15f, "Blur output lost the scene image");
            Check(blurEdges < sharpEdges * 0.85f && blurEdges > 0f, "Blur does not reduce scene edge detail");
            Pass("Rendered blur preserves scene and reduces edge energy: " + sharpEdges.ToString("F2") + " -> " + blurEdges.ToString("F2"));
        }

        private static void Capture(PauseMenu menu, int width, int height, string name = null)
        {
            float blurAmount = PauseBlurFeature.Amount;
            PauseBlurFeature.Amount = 0f; // This helper renders UI on a temporary camera, rather than an overlay.
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
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Results), (name ?? "pause-" + width + "x" + height) + ".png"), image.EncodeToPNG());
            RenderTexture.active = previous;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
            canvas.scaleFactor = oldScale; scaler.enabled = true;
            Object.DestroyImmediate(image); target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(cameraObject);
            Canvas.ForceUpdateCanvases();
            PauseBlurFeature.Amount = blurAmount;
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
            SessionState.SetBool(InteractionOnly, false);
            EditorApplication.update -= Tick;
            EditorApplication.update -= TickInteraction;
            if (exception != null) { File.AppendAllText(Results, "FAIL: " + exception + "\n"); Debug.LogException(exception); }
            EditorApplication.Exit(exception == null ? 0 : 1);
        }

        private static void Near(float actual, float expected, string name) => Check(Mathf.Abs(actual - expected) < 0.001f, name);
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Pass(string message) { File.AppendAllText(Results, "PASS: " + message + "\n"); Debug.Log(message); }
    }
}
