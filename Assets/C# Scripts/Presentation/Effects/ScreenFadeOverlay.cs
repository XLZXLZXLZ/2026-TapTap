using System.Collections;
using UnityEngine;

namespace TapTap
{
    public sealed class ScreenFadeOverlay : LazySingleton<ScreenFadeOverlay>
    {
        public float Opacity { get; private set; }
        public float BulletOpacity { get; private set; }

        private void Awake() => DontDestroyOnLoad(gameObject);

        private void Update() => AdvanceBulletTint(Time.unscaledDeltaTime, GameDebugTime.BulletTimeOn);

        public void AdvanceBulletTint(float unscaledDt, bool active)
        {
            const float opacity = 0.10f;
            const float duration = 0.20f;
            BulletOpacity = Mathf.MoveTowards(BulletOpacity, active ? opacity : 0f,
                opacity * Mathf.Max(0f, unscaledDt) / duration);
        }

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
            if ((Opacity <= 0f && BulletOpacity <= 0f) || Event.current.type != EventType.Repaint) return;
            int previousDepth = GUI.depth;
            Color previousColor = GUI.color;
            GUI.depth = int.MinValue;
            Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
            if (BulletOpacity > 0f)
            {
                GUI.color = new Color(0.45f, 0.75f, 1f, BulletOpacity);
                GUI.DrawTexture(screen, Texture2D.whiteTexture);
            }
            if (Opacity > 0f)
            {
                GUI.color = new Color(0f, 0f, 0f, Opacity);
                GUI.DrawTexture(screen, Texture2D.whiteTexture);
            }
            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }
    }
}
