using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace TapTap
{
    public sealed class FinalEscapeEndingTransition : MonoBehaviour
    {
        [Tooltip("最终逃离完成后加载的场景；留空时使用旧的 EndingScreen。")]
        public string EndingScenePath = "Assets/Scenes/MainMenu/Ending.unity";
        public GameObject EndingScreen;
        [Min(0f)] public float HoldDuration = 0.25f;
        [Min(0.01f)] public float FadeDuration = 0.7f;
        public UnityEvent OnBlack = new UnityEvent();
        private bool playing;
        private bool sceneChangeHandedOff;

        public void Play()
        {
            if (playing || WorldMapSceneRouter.IsLoading) return;
            playing = true;
            sceneChangeHandedOff = false;
            AudioManager.Instance?.BeginMusicTransition(this);
            GameplayActionLocks.Acquire(this, GameplayAction.Input | GameplayAction.Rewind);
            RewindManager.Current?.EndRewind();
            foreach (PlayerInput input in FindObjectsOfType<PlayerInput>())
                if (input.gameObject.scene == gameObject.scene) input.ResetForTransition();
            StartCoroutine(Transition());
        }

        private IEnumerator Transition()
        {
            float elapsed = 0f;
            while (elapsed < HoldDuration) { elapsed += Time.unscaledDeltaTime; yield return null; }
            ScreenFadeOverlay overlay = ScreenFadeOverlay.Instance;
            yield return overlay.FadeTo(1f, FadeDuration);
            if (AudioManager.Existing != null) yield return AudioManager.Existing.WaitForMusicSilence();
            yield return null;
            if (!string.IsNullOrWhiteSpace(EndingScenePath))
            {
                OnBlack.Invoke();
                if (!playing || !isActiveAndEnabled) yield break;
                WorldMapSceneRouter router = WorldMapSceneRouter.Instance;
                if (router.Load(EndingScenePath))
                {
                    sceneChangeHandedOff = true;
                    AudioManager.Existing?.EndMusicTransition(this);
                }
                else
                {
                    // Another scene change may already own the cover; never clear its fade.
                    sceneChangeHandedOff = WorldMapSceneRouter.IsLoading;
                    Debug.LogError("最终逃离无法进入结局：" + router.LastError, this);
                    Cancel();
                }
                // The persistent router owns loading and uncovering the new scene.
                yield break;
            }
            if (EndingScreen != null) EndingScreen.SetActive(true);
            AudioManager.Existing?.EndMusicTransition(this);
            OnBlack.Invoke();
            yield return overlay.FadeTo(0f, FadeDuration);
        }

        public void Cancel()
        {
            AudioManager.Existing?.EndMusicTransition(this);
            StopAllCoroutines();
            GameplayActionLocks.Release(this);
            // Scene unloading invokes Cancel from both the sequence and OnDisable.
            // After handoff, the persistent router must keep the cover fully black until Ending is ready.
            if (playing && !sceneChangeHandedOff && ScreenFadeOverlay.Existing != null)
                ScreenFadeOverlay.Existing.SetOpacity(0f);
            if (EndingScreen != null) EndingScreen.SetActive(false);
            playing = false;
            sceneChangeHandedOff = false;
        }

        private void OnDisable() => Cancel();
    }
}
