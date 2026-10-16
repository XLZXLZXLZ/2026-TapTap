using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace TapTap
{
    public sealed class FinalEscapeEndingTransition : MonoBehaviour
    {
        public GameObject EndingScreen;
        [Min(0f)] public float HoldDuration = 0.25f;
        [Min(0.01f)] public float FadeDuration = 0.7f;
        public UnityEvent OnBlack = new UnityEvent();
        private bool playing;

        public void Play()
        {
            if (playing) return;
            playing = true;
            GameplayActionLocks.Acquire(this, GameplayAction.Input | GameplayAction.Rewind | GameplayAction.DebugCamera);
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
            yield return null;
            if (EndingScreen != null) EndingScreen.SetActive(true);
            OnBlack.Invoke();
            yield return overlay.FadeTo(0f, FadeDuration);
        }

        public void Cancel()
        {
            StopAllCoroutines();
            GameplayActionLocks.Release(this);
            if (playing && ScreenFadeOverlay.Existing != null) ScreenFadeOverlay.Existing.SetOpacity(0f);
            if (EndingScreen != null) EndingScreen.SetActive(false);
            playing = false;
        }

        private void OnDisable() => Cancel();
    }
}
