using DG.Tweening;
using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D), typeof(WorldSurface))]
    public sealed class SpringPad : MonoBehaviour
    {
        [Min(0.01f)] public float BounceHeight = 3f;
        [Min(0.02f)] public float AnimationDuration = 0.28f;
        [SerializeField] private Transform visual;
        private Vector3 initialScale = Vector3.one;
        private Sequence animation;

        public void ConfigureVisual(Transform target)
        {
            visual = target;
            if (visual != null) initialScale = visual.localScale;
        }

        private void OnEnable()
        {
            if (visual != null) initialScale = visual.localScale;
        }

        public static bool TryBounce(WorldSurface surface, float gravity, out float speed)
        {
            speed = 0f;
            SpringPad spring = surface != null ? surface.GetComponent<SpringPad>() : null;
            if (spring == null || !spring.isActiveAndEnabled || spring.BounceHeight <= 0f || gravity <= 0f) return false;
            speed = Mathf.Sqrt(2f * gravity * spring.BounceHeight);
            spring.Animate();
            return true;
        }

        private void Animate()
        {
            if (visual == null || !Application.isPlaying) return;
            animation?.Kill();
            visual.localScale = initialScale;
            float duration = Mathf.Max(0.02f, AnimationDuration);
            animation = DOTween.Sequence()
                .Append(visual.DOScale(Vector3.Scale(initialScale, new Vector3(1.18f, 0.55f, 1f)), duration * 0.25f).SetEase(Ease.OutQuad))
                .Append(visual.DOScale(Vector3.Scale(initialScale, new Vector3(0.9f, 1.15f, 1f)), duration * 0.35f).SetEase(Ease.OutQuad))
                .Append(visual.DOScale(initialScale, duration * 0.4f).SetEase(Ease.OutSine));
        }

        private void OnDisable()
        {
            animation?.Kill();
            animation = null;
            if (visual != null) visual.localScale = initialScale;
        }
    }
}
