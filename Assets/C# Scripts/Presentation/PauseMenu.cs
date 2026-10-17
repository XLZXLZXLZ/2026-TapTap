using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DG.Tweening;
using ScreenFilters;
using System;

namespace TapTap
{
    // Runs before rewind/player input, so Escape cannot also trigger a world shortcut.
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    public sealed class PauseMenu : MonoBehaviour
    {
        public GameObject Content;
        public Button ResumeButton;
        public Button MainMenuButton;
        public Slider MusicSlider;
        public Slider EffectsSlider;
        public Text MusicValue;
        public Text EffectsValue;
        public Text Notice;
        public AudioClip BackgroundMusic;
        public AudioClip ClickSound;
        [Header("Pause presentation")]
        public Button OpenButton;
        public RectTransform LeftArt;
        public RectTransform[] RightRows = new RectTransform[0];
        public CanvasGroup ContentGroup;
        public CanvasGroup BackdropGroup;
        public CanvasGroup PanelGroup;
        [Min(0.05f)] public float SlideDuration = 0.48f;
        [Min(0f)] public float RowStagger = 0.065f;
        [Min(0.05f)] public float FadeDuration = 0.4f;
        public bool IsTransitioning => transition != null && transition.IsActive();
        public bool IsClosing => closing;
        [Tooltip("没有大世界配置时，主菜单按钮使用此场景路径。")]
        public string MainMenuScenePath = "Assets/Scenes/WorldMaps/World_Character_Select.unity";

        public static PauseMenu Current { get; private set; }
        public static bool IsOpen => Current != null && Current.open;
        public static bool BlocksInput => IsOpen || closedFrame == Time.frameCount;
        public static bool HandlesEscape => Current != null && Current.isActiveAndEnabled;
        private static int closedFrame = -1;
        private bool open;
        private float resumeScale;
        private bool cursorVisible;
        private CursorLockMode cursorLock;
        private GameObject previousSelection;
        private GameObject ownedEventSystem;
        private MenuAudio audioService;
        private float nextPreview;
        private Sequence transition;
        private bool closing;
        private Vector2 leftHome;
        private Vector2[] rowHomes;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Current = null; closedFrame = -1; }

        private void Awake()
        {
            audioService = MenuAudio.Ensure(BackgroundMusic, ClickSound);
            Content.SetActive(false);
            if (LeftArt != null) leftHome = LeftArt.anchoredPosition;
            rowHomes = new Vector2[RightRows.Length];
            for (int i = 0; i < RightRows.Length; i++) rowHomes[i] = RightRows[i].anchoredPosition;
            if (OpenButton != null) OpenButton.onClick.AddListener(Toggle);
            EnsureEventSystem(); // The HUD icon must work before the first Escape press.
            ResumeButton.onClick.AddListener(Resume);
            MainMenuButton.onClick.AddListener(ReturnToMainMenu);
            MusicSlider.onValueChanged.AddListener(SetMusicVolume);
            EffectsSlider.onValueChanged.AddListener(SetEffectsVolume);
            RefreshVolumes();
        }

        private void OnEnable()
        {
            if (Current != null && Current != this)
            {
                Debug.LogWarning("场景中已有暂停菜单，已停用重复实例。", this);
                gameObject.SetActive(false);
                return;
            }
            Current = this;
            EnsureEventSystem();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Toggle();
        }

        public void Toggle()
        {
            if (closing) Open();
            else if (open) Resume();
            else Open();
        }

        public bool Open()
        {
            if (open)
            {
                if (closing) AnimateOpen(false);
                return true;
            }
            if (Current != this || !isActiveAndEnabled || LevelAnnotation.IsFeedbackOpen
                || WorldMapSceneRouter.IsLoading || GameStageProgression.InputBlocked
                || (WorldMapProgression.Current != null && WorldMapProgression.Current.IsTransitioning)
                || (SceneResetManager.Existing != null && SceneResetManager.Existing.IsResetting)
                || Time.timeScale <= 0f) return false;
            RewindManager.Current?.EndRewind();
            resumeScale = Time.timeScale;
            cursorVisible = Cursor.visible;
            cursorLock = Cursor.lockState;
            EnsureEventSystem();
            previousSelection = EventSystem.current.currentSelectedGameObject;
            open = true;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            foreach (PlayerInput input in FindObjectsOfType<PlayerInput>()) input.ResetForTransition();
            RefreshVolumes();
            if (Notice != null) Notice.text = "";
            Content.SetActive(true);
            if (OpenButton != null) OpenButton.gameObject.SetActive(false);
            AnimateOpen(true);
            // Keep keyboard navigation; prefab selection feedback must not tint the artwork.
            EventSystem.current.SetSelectedGameObject(ResumeButton.gameObject);
            return true;
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            foreach (EventSystem system in FindObjectsOfType<EventSystem>())
                if (system.isActiveAndEnabled) return;
            if (ownedEventSystem == null)
            {
                ownedEventSystem = new GameObject("Pause Menu EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                ownedEventSystem.transform.SetParent(transform, false);
            }
            ownedEventSystem.SetActive(true);
        }

        public void Resume()
        {
            if (!open || closing) return;
            audioService.PlayClick();
            AnimateClose(null);
        }

        private void Close()
        {
            if (!open) return;
            transition?.Kill();
            transition = null;
            closing = false;
            open = false;
            closedFrame = Time.frameCount;
            Time.timeScale = resumeScale;
            Cursor.lockState = cursorLock;
            Cursor.visible = cursorVisible;
            Content.SetActive(false);
            PauseBlurFeature.Amount = 0f;
            if (OpenButton != null) OpenButton.gameObject.SetActive(true);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(previousSelection != null && previousSelection.activeInHierarchy
                    ? previousSelection : null);
            audioService.SaveVolumes();
        }

        private void AnimateOpen(bool fromHidden)
        {
            transition?.Kill();
            closing = false;
            if (ContentGroup == null || LeftArt == null) return;
            ContentGroup.interactable = true;
            ContentGroup.blocksRaycasts = true;
            Rect canvasRect = ((RectTransform)transform).rect;
            if (fromHidden)
            {
                LeftArt.anchoredPosition = leftHome + Vector2.up * (canvasRect.height + LeftArt.rect.height) * 0.5f;
                for (int i = 0; i < RightRows.Length; i++)
                    RightRows[i].anchoredPosition = rowHomes[i] + Vector2.right * canvasRect.width;
                if (BackdropGroup != null) BackdropGroup.alpha = 0f;
                if (PanelGroup != null) PanelGroup.alpha = 0f;
                PauseBlurFeature.Amount = 0f;
            }
            transition = DOTween.Sequence().SetUpdate(true);
            transition.Insert(0f, Move(LeftArt, leftHome, SlideDuration, Ease.OutCubic));
            for (int i = 0; i < RightRows.Length; i++)
                transition.Insert(i * RowStagger, Move(RightRows[i], rowHomes[i], SlideDuration, Ease.OutCubic));
            AddFades(transition, 1f, FadeDuration);
            transition.OnComplete(() => transition = null);
        }

        private void AnimateClose(Action afterClose)
        {
            if (ContentGroup == null || LeftArt == null) { Close(); afterClose?.Invoke(); return; }
            transition?.Kill();
            closing = true;
            ContentGroup.interactable = false;
            // Keep the backdrop blocking world clicks throughout the exit animation.
            transition = DOTween.Sequence().SetUpdate(true);
            float width = ((RectTransform)transform).rect.width;
            float duration = SlideDuration * 0.8f;
            transition.Insert(0f, Move(LeftArt, leftHome + Vector2.left * width, duration, Ease.InCubic));
            for (int i = 0; i < RightRows.Length; i++)
                transition.Insert(i * RowStagger, Move(RightRows[i], rowHomes[i] + Vector2.right * width, duration, Ease.InCubic));
            AddFades(transition, 0f, duration + Mathf.Max(0, RightRows.Length - 1) * RowStagger);
            transition.OnComplete(() => { transition = null; Close(); afterClose?.Invoke(); });
        }

        private static Tween Move(RectTransform rect, Vector2 position, float duration, Ease ease) =>
            DOTween.To(() => rect.anchoredPosition, value => rect.anchoredPosition = value, position, duration).SetEase(ease);

        private void AddFades(Sequence sequence, float value, float duration)
        {
            if (BackdropGroup != null)
                sequence.Insert(0f, DOTween.To(() => BackdropGroup.alpha, alpha => BackdropGroup.alpha = alpha, value, duration).SetEase(Ease.InOutSine));
            if (PanelGroup != null)
                sequence.Insert(0f, DOTween.To(() => PanelGroup.alpha, alpha => PanelGroup.alpha = alpha, value, duration).SetEase(Ease.InOutSine));
            sequence.Insert(0f, DOTween.To(() => PauseBlurFeature.Amount, amount => PauseBlurFeature.Amount = amount, value, duration).SetEase(Ease.InOutSine));
        }

        public void ReturnToMainMenu()
        {
            if (!open || closing) return;
            string path = MainMenuScenePath;
            WorldMapRoot world = FindObjectOfType<WorldMapRoot>();
            if (world != null && world.Source != null && !string.IsNullOrEmpty(world.Source.SelectionScenePath))
                path = world.Source.SelectionScenePath;
            audioService.PlayClick();
            string destination = path;
            AnimateClose(() => Navigate(destination));
        }

        private void Navigate(string path)
        {
            // Close restored time before the scene router captures its resume scale.
            if (SceneManager.GetActiveScene().path == path) return;
            if (WorldMapSceneRouter.Instance.Load(path)) return;
            Open();
            if (Notice != null) Notice.text = WorldMapSceneRouter.Instance.LastError;
        }

        public void SetMusicVolume(float volume)
        {
            audioService.SetMusicVolume(volume);
            RefreshVolumes();
        }

        public void SetEffectsVolume(float volume)
        {
            audioService.SetEffectsVolume(volume);
            RefreshVolumes();
            if (Time.unscaledTime >= nextPreview)
            {
                nextPreview = Time.unscaledTime + 0.12f;
                audioService.PlayClick();
            }
        }

        private void RefreshVolumes()
        {
            MusicSlider.SetValueWithoutNotify(audioService.MusicVolume);
            EffectsSlider.SetValueWithoutNotify(audioService.EffectsVolume);
            if (MusicValue != null) MusicValue.text = Mathf.RoundToInt(audioService.MusicVolume * 100f) + "%";
            if (EffectsValue != null) EffectsValue.text = Mathf.RoundToInt(audioService.EffectsVolume * 100f) + "%";
        }

        private void OnDisable()
        {
            Close();
            transition?.Kill();
            if (ownedEventSystem != null) ownedEventSystem.SetActive(false);
            if (Current == this) Current = null;
        }
    }
}
