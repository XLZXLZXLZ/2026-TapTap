using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap
{
    // A debug clock changes the number of fixed simulation steps per real second, not their duration.
    public sealed class GameDebugTime : MonoBehaviour
    {
        private static readonly float[] speeds = { 1f, 2f, 4f, 8f };
        private static GameDebugTime current;
        private int speedIndex;
        private PlayerController player;
        private bool bulletTimeActive;
        private bool restoreAfterSceneChange;
        public bool BulletTimeActive => bulletTimeActive;
        public float CurrentSpeed => bulletTimeActive ? 0.5f : speeds[speedIndex];
        public static float Speed => current != null ? current.CurrentSpeed : 1f;
        public static bool BulletTimeOn => current != null && current.bulletTimeActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { current = null; Time.timeScale = 1f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (current == null) new GameObject("Gameplay Debug Time").AddComponent<GameDebugTime>();
        }

        private void Awake()
        {
            if (current != null && current != this) { Destroy(gameObject); return; }
            current = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += SceneLoaded;
            player = FindObjectOfType<PlayerController>();
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            player = FindObjectOfType<PlayerController>();
            restoreAfterSceneChange |= bulletTimeActive;
            bulletTimeActive = false;
        }

        private bool CanControl => player != null && !WorldMapProgression.InputBlocked
            && !RewindManager.Rewinding && !(SceneResetManager.Existing != null && SceneResetManager.Existing.IsResetting);

        private void Update()
        {
            if (!Application.isFocused || !CanControl) return;
            if (Input.GetKeyDown(KeyCode.I)) CycleSpeed();
            if (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl)) ToggleBulletTime();
        }

        public void CycleSpeed() => speedIndex = (speedIndex + 1) % speeds.Length;

        public void ToggleBulletTime()
        {
            if (!CanControl || Time.timeScale <= 0f) return;
            bulletTimeActive = !bulletTimeActive;
            if (bulletTimeActive) ScreenFadeOverlay.Instance.enabled = true;
            ApplySpeed();
        }

        private void LateUpdate()
        {
            if (restoreAfterSceneChange && Time.timeScale > 0f && !WorldMapSceneRouter.IsLoading
                && !(SceneResetManager.Existing != null && SceneResetManager.Existing.IsResetting))
            {
                restoreAfterSceneChange = false;
                ApplySpeed();
            }
            if (!CanControl || Time.timeScale <= 0f) return;
            ApplySpeed();
        }

        private void ApplySpeed()
        {
            if (EffectManager.Existing != null) EffectManager.Existing.SetGameplayTimeScale(CurrentSpeed);
            else Time.timeScale = CurrentSpeed;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            if (current != this) return;
            current = null;
            Time.timeScale = 1f;
        }
    }
}
