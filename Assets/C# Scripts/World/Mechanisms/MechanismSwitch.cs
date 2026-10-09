using DG.Tweening;
using UnityEngine;

namespace TapTap
{
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class MechanismSwitch : MonoBehaviour, IRewindable<MechanismSwitch.Snapshot>
    {
        public struct Snapshot
        {
            public bool Occupied;
            public double ReadyAt;
        }
        [SerializeField] private SpriteRenderer indicator;
        [SerializeField] private Transform cap;
        [SerializeField, Min(0.02f)] private float transitionDuration = 0.18f;
        [SerializeField, Min(0f)] private float retriggerDelay = 0.18f;
        private WorldPhaseState state;
        private BoxCollider2D sensor;
        private Vector3 indicatorScale;
        private Vector3 capScale;
        private bool occupied;
        private double readyAt;
        private Sequence indicatorTween;
        private Tween capTween;

        public void ConfigureVisual(SpriteRenderer lamp, Transform button) { indicator = lamp; cap = button; }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            sensor = GetComponent<BoxCollider2D>();
            sensor.isTrigger = true;
            if (indicator != null) indicatorScale = indicator.transform.localScale;
            if (cap != null) capScale = cap.localScale;
            state = WorldPhaseState.Instance;
            state.Changed += AnimateState;
            SetIndicator(state.Active);
            occupied = false;
            readyAt = 0f;
            if (RewindManager.Current != null) RewindManager.Current.Register(this, this, 30);
        }

        private void FixedUpdate()
        {
            if (!RewindManager.DrivesSimulation) SimulateStep();
        }

        public void SimulateStep()
        {
            bool touching = false;
            Bounds zone = sensor.bounds;
            foreach (MovableEntity entity in MovableEntity.ActiveEntities)
            {
                if (entity == null || entity.IsReturning || !entity.Motor.CollisionsEnabled) continue;
                Bounds actor = new Bounds(entity.Motor.Position + entity.Motor.CenterOffset, entity.Motor.Size);
                if (zone.Intersects(actor)) { touching = true; break; }
                foreach (MotionSegment segment in entity.Motor.MotionSegments)
                {
                    Bounds sweptZone = zone;
                    sweptZone.Expand(actor.size);
                    Vector2 from = segment.From + entity.Motor.CenterOffset;
                    Vector2 delta = segment.To - segment.From;
                    if (delta.sqrMagnitude > 0.000001f && sweptZone.IntersectRay(new Ray(from, delta.normalized), out float distance)
                        && distance <= delta.magnitude) { touching = true; break; }
                }
                if (touching) break;
            }
            double now = RewindManager.Current != null ? RewindManager.Current.SimulationTime : Time.time;
            if (touching && !occupied && now >= readyAt)
            {
                readyAt = now + retriggerDelay;
                state.Toggle();
            }
            if (occupied != touching && cap != null)
            {
                capTween?.Kill();
                capTween = cap.DOScale(Vector3.Scale(capScale, touching ? new Vector3(1.08f, 0.6f, 1f) : Vector3.one), 0.12f)
                    .SetEase(Ease.OutSine);
            }
            occupied = touching;
        }

        public Snapshot CaptureState() => new Snapshot { Occupied = occupied, ReadyAt = readyAt };

        public void RestoreState(in Snapshot snapshot)
        {
            occupied = snapshot.Occupied;
            readyAt = snapshot.ReadyAt;
            indicatorTween?.Kill();
            capTween?.Kill();
            SetIndicator(state.Active);
            if (cap != null) cap.localScale = Vector3.Scale(capScale,
                occupied ? new Vector3(1.08f, 0.6f, 1f) : Vector3.one);
        }

        private void SetIndicator(bool value)
        {
            if (indicator == null) return;
            Color color = indicator.color;
            color.a = value ? 1f : 0f;
            indicator.color = color;
            indicator.transform.localScale = value ? indicatorScale : Vector3.zero;
        }

        private void AnimateState(bool value)
        {
            if (indicator == null) return;
            indicatorTween?.Kill();
            indicatorTween = DOTween.Sequence()
                .Append(indicator.transform.DOScale(value ? indicatorScale : Vector3.zero, transitionDuration).SetEase(value ? Ease.OutBack : Ease.InSine))
                .Join(DOTween.To(() => indicator.color.a, opacity =>
                {
                    Color color = indicator.color; color.a = opacity; indicator.color = color;
                }, value ? 1f : 0f, transitionDuration).SetEase(Ease.OutSine));
        }

        private void OnDisable()
        {
            if (RewindManager.Current != null) RewindManager.Current.Unregister(this);
            indicatorTween?.Kill();
            capTween?.Kill();
            if (state != null) state.Changed -= AnimateState;
            state = null;
            if (indicator != null) indicator.transform.localScale = indicatorScale;
            if (cap != null) cap.localScale = capScale;
        }
    }
}
