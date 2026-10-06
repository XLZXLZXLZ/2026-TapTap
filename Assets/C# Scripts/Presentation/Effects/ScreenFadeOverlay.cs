using System.Collections;
using UnityEngine;

namespace TapTap
{
    public sealed class ScreenFadeOverlay : LazySingleton<ScreenFadeOverlay>
    {
        public float Opacity { get; private set; }

        private void Awake() => DontDestroyOnLoad(gameObject);

        public void SetOpacity(float opacity) => Opacity = Mathf.Clamp01(opacity);

        public IEnumerator FadeTo(float opacity, float duration)
        {
            float target = Mathf.Clamp01(opacity);
            float start = Opacity;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetOpacity(Mathf.SmoothStep(start, target, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            SetOpacity(target);
        }

        private void OnGUI()
        {
            if (Opacity <= 0f || Event.current.type != EventType.Repaint) return;
            int previousDepth = GUI.depth;
            Color previousColor = GUI.color;
            GUI.depth = int.MinValue;
            GUI.color = new Color(0f, 0f, 0f, Opacity);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }
    }
}
