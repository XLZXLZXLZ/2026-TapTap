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
        public float CurrentSpeed => speeds[speedIndex];
        public static float Speed => current != null ? current.CurrentSpeed : 1f;

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

        private void SceneLoaded(Scene scene, LoadSceneMode mode) => player = FindObjectOfType<PlayerController>();

        private bool CanControl => player != null && !WorldMapProgression.InputBlocked && !LevelAnnotation.IsFeedbackOpen
            && !RewindManager.Rewinding && !(SceneResetManager.Existing != null && SceneResetManager.Existing.IsResetting);

        private void Update()
        {
            if (Application.isFocused && CanControl && Input.GetKeyDown(KeyCode.I)) CycleSpeed();
        }

        public void CycleSpeed() => speedIndex = (speedIndex + 1) % speeds.Length;

        private void LateUpdate()
        {
            if (!CanControl || Time.timeScale <= 0f) return;
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
