using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TapTap
{
    [DisallowMultipleComponent, RequireComponent(typeof(Canvas))]
    public sealed class MainMenuController : MonoBehaviour
    {
        public Button StartButton;
        public Button SettingsButton;
        public Button QuitButton;
        public string SelectScenePath = "Assets/Scenes/WorldMaps/World_Character_Select.unity";
        [Tooltip("Reserved settings entry. Connect a settings panel here when it is ready.")]
        public UnityEvent SettingsRequested = new UnityEvent();

        private bool loading;

        private void Awake()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (EventSystem.current == null)
                new GameObject("Main Menu EventSystem", typeof(EventSystem), typeof(StandaloneInputModule))
                    .transform.SetParent(transform, false);
            StartButton.onClick.AddListener(StartGame);
            SettingsButton.onClick.AddListener(OpenSettings);
            QuitButton.onClick.AddListener(QuitGame);
        }

        private void Start()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(StartButton.gameObject);
        }

        public void StartGame()
        {
            if (loading || WorldMapSceneRouter.IsLoading) return;
            AudioManager.Instance.PlayClick();
            if (!WorldMapSceneRouter.Instance.Load(SelectScenePath))
            {
                Debug.LogError(WorldMapSceneRouter.Instance.LastError, this);
                return;
            }
            loading = true;
            StartButton.interactable = SettingsButton.interactable = QuitButton.interactable = false;
        }

        public void OpenSettings()
        {
            if (loading) return;
            AudioManager.Instance.PlayClick();
            SettingsRequested.Invoke();
        }

        public void QuitGame()
        {
            if (loading) return;
            AudioManager.Instance.PlayClick();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            if (StartButton != null) StartButton.onClick.RemoveListener(StartGame);
            if (SettingsButton != null) SettingsButton.onClick.RemoveListener(OpenSettings);
            if (QuitButton != null) QuitButton.onClick.RemoveListener(QuitGame);
        }
    }
}
