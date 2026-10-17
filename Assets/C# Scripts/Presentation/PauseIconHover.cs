using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TapTap
{
    [RequireComponent(typeof(PauseIconOutline))]
    public sealed class PauseIconHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        private Outline outline;
        private Tween fade;
        private void Awake() { outline = GetComponent<Outline>(); SetAlpha(0f); }
        public void OnPointerEnter(PointerEventData data) => Highlight(true);
        public void OnPointerExit(PointerEventData data) => Highlight(false);
        public void OnSelect(BaseEventData data) => Highlight(true);
        public void OnDeselect(BaseEventData data) => Highlight(false);
        private void Highlight(bool highlighted)
        {
            fade?.Kill();
            fade = DOTween.To(() => outline.effectColor.a, SetAlpha, highlighted ? 1f : 0f, 0.13f).SetUpdate(true);
        }
        private void SetAlpha(float alpha) => outline.effectColor = new Color(1f, 1f, 1f, alpha);
        private void OnDisable() { fade?.Kill(); if (outline != null) SetAlpha(0f); }
    }
}
