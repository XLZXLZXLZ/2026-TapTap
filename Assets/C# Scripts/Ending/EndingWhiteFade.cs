using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TapTap
{
    /// <summary>Owns the white cover across scene unloading, including revealing the new menu.</summary>
    public sealed class EndingWhiteFade : MonoBehaviour
    {
        private static EndingWhiteFade active;
        private Image cover;

        public static bool TryLoad(string scenePath, float fadeIn, float hold, float fadeOut)
        {
            if (active != null) return false;
            if (string.IsNullOrWhiteSpace(scenePath) || !Application.CanStreamedLevelBeLoaded(scenePath))
            {
                Debug.LogError("结局无法加载主菜单，请检查 Build Settings：" + scenePath);
                return false;
            }
            active = new GameObject("Ending White Fade").AddComponent<EndingWhiteFade>();
            DontDestroyOnLoad(active.gameObject);
            active.CreateCover();
            active.StartCoroutine(active.Transition(scenePath, fadeIn, hold, fadeOut));
            return true;
        }

        private void CreateCover()
        {
            var canvasObject = new GameObject("White Cover", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            var imageObject = new GameObject("White", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.SetParent(canvasObject.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            cover = imageObject.GetComponent<Image>();
            cover.raycastTarget = true;
            cover.color = new Color(1f, 1f, 1f, 0f);
        }

        private IEnumerator Transition(string scenePath, float fadeIn, float hold, float fadeOut)
        {
            yield return Fade(1f, fadeIn);
            float elapsed = 0f;
            while (elapsed < hold) { elapsed += Time.unscaledDeltaTime; yield return null; }
            AsyncOperation loading = null;
            try { loading = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single); }
            catch (Exception exception) { Debug.LogException(exception, this); }
            if (loading != null)
            {
                while (!loading.isDone) yield return null;
                // Keep the cover until the new scene has rendered its first frame.
                yield return null;
            }
            else
            {
                foreach (EndingSequenceController sequence in FindObjectsOfType<EndingSequenceController>())
                    sequence.RecoverFromFailedTransition();
            }
            yield return Fade(0f, fadeOut);
            Destroy(gameObject);
        }

        private IEnumerator Fade(float opacity, float duration)
        {
            float start = cover.color.a;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                cover.color = new Color(1f, 1f, 1f,
                    Mathf.SmoothStep(start, opacity, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            cover.color = new Color(1f, 1f, 1f, opacity);
        }

        private void OnDestroy() { if (active == this) active = null; }
    }
}
