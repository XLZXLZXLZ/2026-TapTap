using UnityEngine;
using UnityEngine.UI;

namespace TapTap
{
    [DisallowMultipleComponent]
    public sealed class NeonLevelPiece : MonoBehaviour
    {
        public int Sequence;
        public string ModuleId;
        public NeonLevelOutline Outline;
        public Text Number;
        public Color Tint;
        public Vector2 DiagramLabelPosition;
        private float intensity=1f, reveal=1f;
        public float Emphasis => Outline.Emphasis;

        public void SetEmphasis(float value)
        {
            Outline.SetEmphasis(value);
            UpdateNumber();
        }

        public bool Contains(Vector2 screen, Camera camera)
        {
            return gameObject.activeInHierarchy
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(Outline.rectTransform, screen, camera, out Vector2 local)
                && Outline.ContainsLocalPoint(local);
        }

        public Vector2 NumberScreenPosition(Camera camera) => RectTransformUtility.WorldToScreenPoint(camera, Number.transform.position);

        public void SetIntensity(float value)
        {
            intensity=value;
            Outline.color = new Color(Tint.r, Tint.g, Tint.b, Mathf.Clamp01(value));
            UpdateNumber();
        }

        public void SetReveal(float value)
        {
            reveal=Mathf.Clamp01(value);
            Outline.SetReveal(reveal);
            UpdateNumber();
        }

        private void UpdateNumber()
        {
            float numberAlpha=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(0.55f,1f,reveal));
            float numberIntensity=Mathf.Sqrt(Mathf.Clamp01(intensity))*Mathf.Lerp(0.74f,1f,Emphasis);
            Number.color=Color.Lerp(Tint,Color.white,0.75f)*new Color(1f,1f,1f,numberIntensity*numberAlpha);
        }
    }
}
