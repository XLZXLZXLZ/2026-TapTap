using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace TapTap.Hints
{
    // The HUD observes the replay director. Neither the player nor the pause menu depends on it.
    [DisallowMultipleComponent, DefaultExecutionOrder(-900)]
    public sealed class HintReplayPanel : MonoBehaviour
    {
        public HintReplayDirector Director;
        public Button OpenButton, ConfirmButton, CancelButton, DismissButton;
        public Graphic IconGraphic;
        public GameObject PanelRoot;
        public CanvasGroup PanelGroup;
        [Min(0.01f)] public float FadeDuration = 0.16f;
        [Range(0f, 1f)] public float UnavailableIconAlpha = 0.45f;
        public bool IsOpen => state == State.Opening || state == State.Open;
        public bool IsClosing => state == State.Closing;

        private enum State { Hidden, Opening, Open, Closing }
        private State state;
        private Tween fade;
        private HintReplayClip offeredClip;
        private Transform offeredOrigin;
        private bool playAfterClose;

        private void Awake()
        {
            OpenButton.onClick.AddListener(Toggle);
            ConfirmButton.onClick.AddListener(Confirm);
            CancelButton.onClick.AddListener(Cancel);
            if (DismissButton != null) DismissButton.onClick.AddListener(Cancel);
            HideImmediately();
        }

        private void Update()
        {
            ResolveDirector();
            bool blocked = PauseMenu.BlocksInput || RewindManager.Rewinding || HintReplayContext.Transitioning(gameObject.scene);
            bool available = Director != null && Director.CanRequestHint;
            OpenButton.gameObject.SetActive(Director != null && !PauseMenu.IsOpen);
            OpenButton.interactable = !blocked;
            if (IconGraphic != null)
            {
                Color color = IconGraphic.color;
                color.a = available ? 1f : UnavailableIconAlpha;
                IconGraphic.color = color;
            }
            bool sameOffer = available && Director.CurrentClip == offeredClip && Director.CurrentReferenceFrame == offeredOrigin;
            if (IsOpen && !sameOffer) BeginClose(false);
            if (IsClosing && !sameOffer) playAfterClose = false;
            if (PauseMenu.IsOpen && PanelGroup != null) PanelGroup.blocksRaycasts = false;
        }

        public void Toggle()
        {
            if (IsOpen) Cancel();
            else if (!IsClosing) TryOpen();
        }

        public bool TryOpen()
        {
            ResolveDirector();
            if (!isActiveAndEnabled || Director == null || !Director.CanRequestHint || IsClosing) return false;
            if (IsOpen) return true;
            offeredClip = Director.CurrentClip;
            offeredOrigin = Director.CurrentReferenceFrame;
            playAfterClose = false;
            state = State.Opening;
            PanelGroup.alpha = 0f;
            PanelGroup.interactable = true;
            PanelGroup.blocksRaycasts = true;
            PanelRoot.SetActive(true);
            // The transparent dismiss area stays behind the two HUD icons.
            PanelRoot.transform.SetAsLastSibling();
            OpenButton.transform.SetAsLastSibling();
            PauseMenu pause = PauseMenu.Current;
            if (pause != null && pause.gameObject.scene == gameObject.scene && pause.OpenButton != null)
                pause.OpenButton.transform.SetAsLastSibling();
            fade?.Kill();
            fade = DOTween.To(() => PanelGroup.alpha, value => PanelGroup.alpha = value, 1f, FadeDuration)
                .SetUpdate(true).OnComplete(() => { fade = null; state = State.Open; });
            MenuAudio.Current?.PlayClick();
            return true;
        }

        public void Confirm()
        {
            if (!IsOpen) return;
            MenuAudio.Current?.PlayClick();
            BeginClose(true);
        }

        public void Cancel()
        {
            if (!IsOpen && !IsClosing) return;
            MenuAudio.Current?.PlayClick();
            BeginClose(false);
        }

        private void BeginClose(bool play)
        {
            playAfterClose = play;
            if (state == State.Hidden || IsClosing) return;
            state = State.Closing;
            PanelGroup.interactable = false;
            fade?.Kill();
            fade = DOTween.To(() => PanelGroup.alpha, value => PanelGroup.alpha = value, 0f, FadeDuration).SetUpdate(true).OnComplete(() =>
            {
                fade = null;
                bool requested = playAfterClose;
                playAfterClose = false;
                state = State.Hidden;
                PanelGroup.blocksRaycasts = false;
                PanelRoot.SetActive(false);
                // Region/return-trigger changes during the fade invalidate the captured offer.
                if (requested && Director != null && Director.CurrentClip == offeredClip
                    && Director.CurrentReferenceFrame == offeredOrigin && Director.CanRequestHint)
                    Director.RequestHint();
                offeredClip = null; offeredOrigin = null;
            });
        }

        private void ResolveDirector()
        {
            if (Director == null || Director.gameObject.scene != gameObject.scene)
                Director = HintReplayContext.Find<HintReplayDirector>(gameObject.scene);
        }

        private void HideImmediately()
        {
            fade?.Kill(); fade = null;
            state = State.Hidden;
            playAfterClose = false;
            offeredClip = null; offeredOrigin = null;
            if (PanelGroup != null)
            {
                PanelGroup.alpha = 0f;
                PanelGroup.interactable = false;
                PanelGroup.blocksRaycasts = false;
            }
            if (PanelRoot != null) PanelRoot.SetActive(false);
        }

        private void OnDisable() => HideImmediately();
        private void OnDestroy()
        {
            if (OpenButton != null) OpenButton.onClick.RemoveListener(Toggle);
            if (ConfirmButton != null) ConfirmButton.onClick.RemoveListener(Confirm);
            if (CancelButton != null) CancelButton.onClick.RemoveListener(Cancel);
            if (DismissButton != null) DismissButton.onClick.RemoveListener(Cancel);
        }
    }
}
