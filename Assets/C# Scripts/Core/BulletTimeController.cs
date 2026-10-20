using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap
{
    // Gameplay bullet time composes with transient effects without changing the fixed simulation step.
    public sealed class BulletTimeController : MonoBehaviour
    {
        private static BulletTimeController current;
        private PlayerController player;
        private bool bulletTimeActive;
        private bool restoreAfterSceneChange;
        public bool BulletTimeActive => bulletTimeActive;
        public float CurrentSpeed => bulletTimeActive ? 0.5f : 1f;
        public static float Speed => current != null ? current.CurrentSpeed : 1f;
        public static bool BulletTimeOn => current != null && current.bulletTimeActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { current = null; Time.timeScale = 1f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (current == null) new GameObject("Bullet Time").AddComponent<BulletTimeController>();
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
            && !GameplayActionLocks.IsLocked(GameplayAction.PlayerControls, player.gameObject.scene)
            && !RewindManager.Rewinding && !(SceneResetManager.Existing != null && SceneResetManager.Existing.IsResetting);

        private void Update()
        {
            if (!Application.isFocused || !CanControl) return;
            if (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl)) ToggleBulletTime();
        }

        public void ToggleBulletTime()
        {
            if (!CanControl || Time.timeScale <= 0f) return;
            bulletTimeActive = !bulletTimeActive;
            AudioManager.Instance?.Play(bulletTimeActive ? SoundId.BulletTimeOn : SoundId.BulletTimeOff);
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
