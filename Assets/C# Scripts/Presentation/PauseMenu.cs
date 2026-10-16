using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Current = null; closedFrame = -1; }

        private void Awake()
        {
            audioService = MenuAudio.Ensure(BackgroundMusic, ClickSound);
            Content.SetActive(false);
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
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Toggle();
        }

        public void Toggle()
        {
            if (open) Resume();
            else Open();
        }

        public bool Open()
        {
            if (open) return true;
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
            EventSystem.current.SetSelectedGameObject(ResumeButton.gameObject);
            return true;
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            if (ownedEventSystem == null)
            {
                ownedEventSystem = new GameObject("Pause Menu EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                ownedEventSystem.transform.SetParent(transform, false);
            }
            ownedEventSystem.SetActive(true);
        }

        public void Resume()
        {
            if (!open) return;
            audioService.PlayClick();
            Close();
        }

        private void Close()
        {
            if (!open) return;
            open = false;
            closedFrame = Time.frameCount;
            Time.timeScale = resumeScale;
            Cursor.lockState = cursorLock;
            Cursor.visible = cursorVisible;
            Content.SetActive(false);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(previousSelection != null && previousSelection.activeInHierarchy
                    ? previousSelection : null);
            if (ownedEventSystem != null) ownedEventSystem.SetActive(false);
            audioService.SaveVolumes();
        }

        public void ReturnToMainMenu()
        {
            if (!open) return;
            string path = MainMenuScenePath;
            WorldMapRoot world = FindObjectOfType<WorldMapRoot>();
            if (world != null && world.Source != null && !string.IsNullOrEmpty(world.Source.SelectionScenePath))
                path = world.Source.SelectionScenePath;
            audioService.PlayClick();
            Close(); // Restore time before the scene router captures its resume scale.
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
            if (Current == this) Current = null;
        }
    }
}
