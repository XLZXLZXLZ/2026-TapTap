using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap
{
    [DisallowMultipleComponent]
    public sealed class StartupSplash : MonoBehaviour
    {
        public const string ScenePath = "Assets/Scenes/MainMenu/Startup.unity";
        public CanvasGroup StudioGroup;
        public string NextScenePath = "Assets/Scenes/MainMenu/MainMenu.unity";
        [Min(0f)] public float InitialBlackDuration = 0.35f;
        [Min(0f)] public float FadeInDuration = 1f;
        [Min(0f)] public float HoldDuration = 1.6f;
        [Min(0f)] public float FadeOutDuration = 1f;
        [Min(0f)] public float FinalBlackDuration = 0.25f;

        private void Awake()
        {
            StudioGroup.alpha = 0f;
            StudioGroup.interactable = false;
            StudioGroup.blocksRaycasts = false;
            GameplayActionLocks.Acquire(this, GameplayAction.Input | GameplayAction.PlayerControls | GameplayAction.Rewind);
        }

        private IEnumerator Start()
        {
            // Keep the destination unloaded until the logo has completely faded to black.
            yield return new WaitForSecondsRealtime(InitialBlackDuration);
            yield return Fade(0f, 1f, FadeInDuration);
            yield return new WaitForSecondsRealtime(HoldDuration);
            yield return Fade(1f, 0f, FadeOutDuration);
            yield return new WaitForSecondsRealtime(FinalBlackDuration);
            if (!Application.CanStreamedLevelBeLoaded(NextScenePath))
            {
                Debug.LogError("Startup destination is missing from Build Settings: " + NextScenePath, this);
                yield break;
            }
            yield return SceneManager.LoadSceneAsync(NextScenePath, LoadSceneMode.Single);
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                StudioGroup.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / duration));
                yield return null;
            }
            StudioGroup.alpha = to;
        }

        private void OnDestroy() => GameplayActionLocks.Release(this);
    }
}
