using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TapTap
{
    public sealed class BlueprintLevelPiece : MonoBehaviour
    {
        public int ArtIndex;
        public RawImage Normal, Glow;
        public RectTransform TravelAnchor;
        [Range(0.05f, 0.6f)] public float HoverDuration = 0.18f;
        [Range(1f, 1.3f)] public float ConfirmScale = 1.1f;
        [Min(0.1f)] public float ConfirmDuration = 0.28f;
        [Range(0.8f, 0.99f)] public float PressScale = 0.94f;
        [Min(0.02f)] public float PressDuration = 0.08f;
        public float Emphasis { get; private set; }
        public float Opacity { get; private set; } = 1f;
        private BlueprintMapArt.Piece art;
        private float hoverTarget;
        private bool confirming;
        private bool pressed;
        public bool IsConfirming => confirming;
        public int Sequence => art.Sequence;
        public string ModuleId => art.ModuleId;
        public BlueprintMapArt.Piece Art => art;

        public void Initialize(BlueprintMapArt source)
        {
            art = source.Pieces[ArtIndex];
            confirming = false;
            pressed = false;
            transform.localScale = Vector3.one;
            Emphasis = 0f;
            hoverTarget = 0f;
            Opacity = 1f;
            Refresh();
        }

        public void Hover(bool value) => hoverTarget = value ? 1f : 0f;
        public void SetPressed(bool value) => pressed = value;
        public void SetEmphasis(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(Emphasis, value)) return;
            Emphasis = value; Refresh();
        }
        public void SetOpacity(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(Opacity, value)) return;
            Opacity = value; Refresh();
        }

        private void Refresh()
        {
            Normal.color = new Color(1f, 1f, 1f, Opacity * (1f - Emphasis));
            Glow.color = new Color(1f, 1f, 1f, Opacity * Emphasis);
        }

        public bool Contains(Vector2 screen, Camera camera)
        {
            if (!gameObject.activeInHierarchy || Opacity < 0.01f || art == null) return false;
            // The visual shrinks under the pointer, but the button's hit area stays fixed.
            return RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent, screen, camera, out Vector2 local)
                && art.Contains(local - (Vector2)transform.localPosition + art.Center);
        }

        public IEnumerator Confirm()
        {
            confirming = true;
            pressed = false;
            SetEmphasis(1f);
            float startScale = transform.localScale.x;
            float elapsed = 0f;
            try
            {
                while (elapsed < ConfirmDuration)
                {
                    elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
                    float t = Mathf.Clamp01(elapsed / ConfirmDuration);
                    // Release from the held scale, overshoot, then settle before loading.
                    float scale = t < 0.55f ? Mathf.Lerp(startScale, ConfirmScale, 1f - Mathf.Pow(1f - t / 0.55f, 3f))
                        : Mathf.Lerp(ConfirmScale, 1f, Mathf.SmoothStep(0f, 1f, (t - 0.55f) / 0.45f));
                    transform.localScale = Vector3.one * scale;
                    yield return null;
                }
            }
            finally { transform.localScale = Vector3.one; confirming = false; }
        }

        private void Update()
        {
            if (!confirming && art != null)
            {
                SetEmphasis(Mathf.MoveTowards(Emphasis, hoverTarget, Time.unscaledDeltaTime / HoverDuration));
                float target = pressed ? PressScale : 1f;
                float value = Mathf.MoveTowards(transform.localScale.x, target,
                    Time.unscaledDeltaTime * (1f - PressScale) / PressDuration);
                transform.localScale = Vector3.one * value;
            }
        }
    }
}
