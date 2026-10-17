using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TapTap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Selectable))]
    public sealed class UIInteractionScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISubmitHandler
    {
        public RectTransform Target;
        [Min(1f)] public float HoverScale = 1.05f;
        [Range(0.5f, 1f)] public float PressedScale = 0.95f;
        [Min(0.01f)] public float Duration = 0.1f;

        private Selectable control;
        private Vector3 homeScale;
        private Tween tween;
        private bool hovered, held;
        private float submitUntil;
        private float currentScale = 1f;

        private void Awake()
        {
            control = GetComponent<Selectable>();
            if (Target == null) Target = (RectTransform)transform;
            homeScale = Target.localScale;
        }

        private bool CanInteract => control != null && control.isActiveAndEnabled && control.IsInteractable();

        private void LateUpdate()
        {
            // CanvasGroup disables children without disabling their MonoBehaviours.
            if (!CanInteract) { hovered = held = false; submitUntil = 0f; }
            Refresh();
        }

        public void OnPointerEnter(PointerEventData data) { hovered = CanInteract; Refresh(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; Refresh(); }

        public void OnPointerDown(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || !CanInteract) return;
            held = true;
            Refresh();
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left) return;
            held = false;
            Refresh();
        }

        public void OnSubmit(BaseEventData data)
        {
            if (!CanInteract || !(control is Button)) return;
            submitUntil = Time.unscaledTime + Duration;
            Refresh();
        }

        private void Refresh()
        {
            if (Target == null) return;
            bool pressed = (held && (hovered || control is Slider)) || Time.unscaledTime < submitUntil;
            float scale = !CanInteract ? 1f : pressed ? PressedScale : hovered ? HoverScale : 1f;
            if (Mathf.Approximately(scale, currentScale)) return;
            currentScale = scale;
            tween?.Kill();
            // Independent time keeps feedback responsive while the game is paused.
            tween = DOTween.To(() => Target.localScale, value => Target.localScale = value,
                homeScale * scale, Duration).SetEase(Ease.OutCubic).SetUpdate(true);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            hovered = held = false;
            submitUntil = 0f;
            Refresh();
        }

        private void OnDisable()
        {
            tween?.Kill();
            tween = null;
            hovered = held = false;
            submitUntil = 0f;
            currentScale = 1f;
            if (Target != null) Target.localScale = homeScale;
        }
    }
}
