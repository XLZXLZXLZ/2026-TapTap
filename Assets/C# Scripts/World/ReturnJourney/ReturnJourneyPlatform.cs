using System.Collections;
using DG.Tweening;
using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    public sealed class ReturnJourneyPlatform : MonoBehaviour
    {
        [Tooltip("只移动此视觉节点，任何碰撞体都留在原位。")]
        public Transform View;
        public Vector3 FallenLocalPosition;
        public Vector3 FallenLocalEuler = new Vector3(0f, 0f, -7f);
        [Min(0.01f)] public float DropDuration = 0.45f;
        public ParticleSystem CollapseBurst;
        private Tween drop;
        private Coroutine pendingCollapse;
        public bool HasCollapsed { get; private set; }
        public bool IsDropping => drop != null && drop.IsActive();
        private void OnEnable()
        {
            EventManager.Subscribe<GameStageChanged>(StageChanged);
            if (Application.isPlaying && GameStageProgression.HasProgressedIn(gameObject.scene))
            {
                var presentation = ReturnJourneyPresentation.InScene(gameObject.scene);
                if (presentation != null && presentation.IsEntryPlaying) pendingCollapse = StartCoroutine(CollapseAfterEntry());
                else SetFallen();
            }
        }
        private void StageChanged(GameStageChanged message)
        {
            if (message.Scene == gameObject.scene && message.Previous == GameStage.Outbound && pendingCollapse == null)
                pendingCollapse = StartCoroutine(CollapseAfterEntry());
        }
        private IEnumerator CollapseAfterEntry()
        {
            // Let all stage listeners receive the event before checking the presentation.
            yield return null;
            var presentation = ReturnJourneyPresentation.InScene(gameObject.scene);
            while (presentation != null && presentation.IsEntryPlaying) yield return null;
            pendingCollapse = null;
            foreach (ReturnJourneyTrigger trigger in FindObjectsOfType<ReturnJourneyTrigger>())
                if (trigger.gameObject.scene == gameObject.scene && trigger.Cue == ReturnJourneyCue.BeginReturn
                    && trigger.Platform == this && trigger.IsPlaying) yield break;
            Collapse();
        }
        public void Collapse()
        {
            if (HasCollapsed || View == null) return;
            HasCollapsed = true;
            AudioManager.Instance?.Play(SoundId.PlatformDetach, transform.position, owner: this);
            CollapseBurst?.Play(true);
            drop = DOTween.Sequence()
                .Append(View.DOLocalMove(FallenLocalPosition, DropDuration).SetEase(Ease.InQuad))
                .Join(View.DOLocalRotate(FallenLocalEuler, DropDuration).SetEase(Ease.OutBack));
        }
        private void SetFallen()
        {
            HasCollapsed = true;
            if (View == null) return;
            View.localPosition = FallenLocalPosition;
            View.localRotation = Quaternion.Euler(FallenLocalEuler);
        }
        private void OnDisable()
        {
            EventManager.Unsubscribe<GameStageChanged>(StageChanged);
            if (pendingCollapse != null) StopCoroutine(pendingCollapse);
            pendingCollapse = null;
            drop?.Kill(); drop = null;
            if (HasCollapsed) SetFallen();
        }
    }
}
