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
        public bool HasCollapsed { get; private set; }
        private void OnEnable()
        {
            EventManager.Subscribe<GameStageChanged>(StageChanged);
            if (Application.isPlaying && GameStageProgression.HasProgressedIn(gameObject.scene)) SetFallen();
        }
        private void StageChanged(GameStageChanged message)
        {
            if (message.Scene == gameObject.scene && message.Previous == GameStage.Outbound) Collapse();
        }
        public void Collapse()
        {
            if (HasCollapsed || View == null) return;
            HasCollapsed = true;
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
            drop?.Kill(); drop = null;
            if (HasCollapsed) SetFallen();
        }
    }
}
