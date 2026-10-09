using DG.Tweening;
using UnityEngine;

namespace TapTap
{
    public sealed class PlayerView : MonoBehaviour
    {
        [SerializeField] private PlayerController controller;
        [SerializeField] private MovableEntity body;
        [SerializeField] private MovableEntity head;
        [SerializeField] private PlayerConfig config;
        [SerializeField] private PlayerVisualConfig visualSettings;
        private PlayerController subscribedController;
        private MovableEntity subscribedHead;
        [SerializeField] private MagneticConnectionView magnet;
        [SerializeField] private ContactParticles particles;
        [SerializeField] private LineRenderer guide;
        [SerializeField] private Color recallReadyColor = new Color(0.25f, 0.65f, 1f, 1f);
        [SerializeField] private Color recallBlockedColor = new Color(1f, 0.32f, 0.32f, 1f);
        private float guideWidth;
        private float guideAlpha;
        private Vector3 bodyScale, headScale, bodyPosition, headPosition;
        private Quaternion bodyRotation, headRotation;
        private bool captured;
        private float bodySquash, headSquash;
        private float motionLean;
        private float headLean, bodyLag, headLag, breathingPhase;
        private Tween bodyTween, headTween;
        private bool headSpringActive;

        public void ConfigureVisuals(PlayerVisualConfig settings) => visualSettings = settings;
        public void ConfigureEffects(MagneticConnectionView connection, ContactParticles contact, LineRenderer recall)
        { magnet = connection; particles = contact; guide = recall; }

        public void Configure(PlayerController player, MovableEntity lower, MovableEntity upper, PlayerConfig settings)
        {
            controller = player; body = lower; head = upper; config = settings;
            if (!Application.isPlaying) return;
            if (subscribedController != controller)
            {
                Unsubscribe();
                subscribedController = controller;
                if (controller != null)
                {
                    controller.Effect += OnEffect;
                    controller.PhaseChanged += OnPhase;
                    controller.ContactFeedback += OnContact;
                }
            }
            if (subscribedHead != head)
            {
                if (subscribedHead != null) subscribedHead.SpringBounced -= OnHeadSpringBounce;
                subscribedHead = head;
                if (subscribedHead != null) subscribedHead.SpringBounced += OnHeadSpringBounce;
            }
            EnsureVisuals();
        }

        private void Start() => Configure(controller, body, head, config);
        private void OnEnable()
        {
            if (Application.isPlaying && captured) Configure(controller, body, head, config);
        }

        private void EnsureVisuals()
        {
            if (!captured && body != null && head != null && body.Visual != null && head.Visual != null)
            {
                bodyScale = body.Visual.localScale; headScale = head.Visual.localScale;
                bodyPosition = body.Visual.localPosition; headPosition = head.Visual.localPosition;
                bodyRotation = body.Visual.localRotation; headRotation = head.Visual.localRotation;
                captured = true;
            }
            if (guide != null && guideWidth <= 0f) guideWidth = guide.widthMultiplier;
        }

        private void LateUpdate()
        {
            if (RewindManager.Rewinding) RenderRewindState();
            else Simulate(Time.deltaTime);
        }

        public void ClearTransientEffects()
        {
            EnsureVisuals();
            ResetVisual(body);
            ResetVisual(head);
            guideAlpha = 0f;
            if (particles != null) particles.Clear();
        }

        public void RenderRewindState()
        {
            if (controller == null || body == null || head == null || config == null) return;
            EnsureVisuals();
            float unit = body.Motor.Size.x / Mathf.Max(0.01f, body.Motor.Collider.size.x);
            bool returning = body.IsReturning || head.IsReturning;
            bool connected = controller.HasMagneticConnection && controller.Phase != PlayerPhase.Joined && !returning;
            RenderGuide(unit, returning, 0f, true);
            if (magnet != null) magnet.Render(body, head, connected, controller.Phase == PlayerPhase.Pulling, unit, true);
        }

        private void RenderGuide(float unit, bool returning, float dt, bool immediate)
        {
            float target = controller.GuideVisible && !returning ? 0.75f : 0f;
            guideAlpha = immediate ? target : Mathf.MoveTowards(guideAlpha, target, dt * 6f);
            bool canRecall = controller.CanRecall;
            Color color = canRecall ? recallReadyColor : recallBlockedColor;
            color.a *= guideAlpha;
            if (guide == null) return;
            guide.startColor = guide.endColor = color;
            guide.widthMultiplier = guideWidth * unit;
            guide.enabled = guideAlpha > 0.001f;
            guide.SetPosition(0, head.transform.position);
            Vector3 guideEnd = head.transform.position + Vector3.down * 1000f;
            if (canRecall) guideEnd.y = body.transform.position.y;
            guide.SetPosition(1, guideEnd);
        }

        public void Simulate(float dt)
        {
            if (dt <= 0f || controller == null || body == null || head == null || config == null) return;
            EnsureVisuals();
            float unit = body.Motor.Size.x / Mathf.Max(0.01f, body.Motor.Collider.size.x);
            bool returning = body.IsReturning || head.IsReturning;
            bool connected = controller.HasMagneticConnection && controller.Phase != PlayerPhase.Joined && !returning;
            RenderGuide(unit, returning, dt, false);
            if (!captured || visualSettings == null)
            {
                if (magnet != null) magnet.Render(body, head, connected, controller.Phase == PlayerPhase.Pulling, unit);
                return;
            }
            float smoothing = 1f - Mathf.Exp(-14f * dt);
            float lagSmoothing = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, visualSettings.MagneticLagSmoothTime));
            float speed = body.Velocity.x / Mathf.Max(unit, 0.01f);
            float movement = Mathf.Clamp(speed / Mathf.Max(0.01f, config.MoveSpeed), -1f, 1f);
            float leanTarget = connected ? -movement * visualSettings.MagneticLeanAngle : -speed * 0.65f;
            motionLean = Mathf.Lerp(motionLean, body.IsReturning ? 0f : leanTarget, smoothing);
            headLean = Mathf.Lerp(headLean, returning ? 0f : connected
                ? leanTarget * visualSettings.MagneticHeadLeanMultiplier
                : controller.HasMagneticConnection ? motionLean * 0.35f : 0f, lagSmoothing);
            bodyLag = Mathf.Lerp(bodyLag, connected ? -movement * visualSettings.MagneticLagDistance * 0.2f : 0f, lagSmoothing);
            float headLagTarget = connected ? -movement * visualSettings.MagneticLagDistance
                : controller.HasMagneticConnection && !returning ? -speed * 0.007f : 0f;
            headLag = Mathf.Lerp(headLag, headLagTarget, lagSmoothing);
            breathingPhase = Mathf.Repeat(breathingPhase + dt * visualSettings.BreathingFrequency * 2f * Mathf.PI, 2f * Mathf.PI);
            float breathAmount = visualSettings.BreathingAmplitude * Mathf.Lerp(1f, 0.4f, Mathf.Abs(movement));
            float bodyBreath = Mathf.Sin(breathingPhase) * breathAmount;
            float headBreath = Mathf.Sin(breathingPhase + 0.45f) * breathAmount * 0.6f;
            if (!body.IsReturning)
            {
                float stretch = Mathf.Clamp(Mathf.Abs(body.Velocity.y) / (12f * unit), 0f, 1f) * visualSettings.MotionStretch;
                Vector3 factor = new Vector3(1f + bodySquash - stretch * 0.5f, 1f - bodySquash + stretch, 1f);
                factor = Vector3.Scale(factor, new Vector3(1f - bodyBreath * 0.5f, 1f + bodyBreath, 1f));
                body.Visual.localScale = Vector3.Scale(bodyScale, factor);
                body.Visual.localPosition = bodyPosition + Vector3.right * bodyLag
                    + Vector3.up * ((factor.y - 1f) * body.Motor.Collider.size.y * 0.5f);
                body.Visual.localRotation = bodyRotation * Quaternion.Euler(0f, 0f, motionLean);
            }
            if (!head.IsReturning)
            {
                float stretch = controller.Phase == PlayerPhase.Extending || controller.Phase == PlayerPhase.Pulling ? visualSettings.MotionStretch : 0f;
                Vector3 factor = new Vector3(1f + headSquash - stretch * 0.5f, 1f - headSquash + stretch, 1f);
                factor = Vector3.Scale(factor, new Vector3(1f - headBreath * 0.5f, 1f + headBreath, 1f));
                head.Visual.localScale = Vector3.Scale(headScale, factor);
                head.Visual.localPosition = headPosition + Vector3.right * headLag
                    + Vector3.up * (headSpringActive ? (factor.y - 1f) * head.Motor.Collider.size.y * 0.5f : 0f);
                head.Visual.localRotation = headRotation * Quaternion.Euler(0f, 0f, headLean);
            }
            if (magnet != null) magnet.Render(body, head, connected, controller.Phase == PlayerPhase.Pulling, unit);
        }

        private void OnContact(PlayerContactFeedback contact)
        {
            if (visualSettings == null || contact.Entity == null || contact.Entity.IsReturning) return;
            float unit = contact.Entity.Motor.Size.x / Mathf.Max(0.01f, contact.Entity.Motor.Collider.size.x);
            if (particles != null) particles.Burst(contact.Point, contact.Normal, contact.Assembly, unit);
            float amount = contact.Assembly ? visualSettings.AssemblySquash : visualSettings.LandingSquash;
            if (!contact.Assembly) amount *= Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(contact.Speed / (8f * unit)));
            if (contact.Entity != head || !headSpringActive) Pulse(contact.Entity, amount);
            if (contact.Assembly) Pulse(head, amount * 0.55f);
        }

        private void OnPhase(PlayerPhase phase)
        {
            if (visualSettings == null) return;
            if (phase == PlayerPhase.Extending)
            {
                PlayExtensionSquash();
                Pulse(head, -visualSettings.ExtensionSquash * 0.6f);
            }
            if (phase == PlayerPhase.Pulling) Pulse(body, -visualSettings.MotionStretch);
            if (phase == PlayerPhase.Detached) Pulse(head, visualSettings.LandingSquash * 0.45f);
        }

        private void OnEffect(PlayerEffect effect)
        {
            EffectManager.Instance.Play(effect, body.Visual);
            if (effect == PlayerEffect.Bounce && visualSettings != null) Pulse(body, -visualSettings.AssemblySquash * 0.7f);
            if (effect == PlayerEffect.RecallFailed && visualSettings != null) Pulse(body, visualSettings.LandingSquash * 0.4f);
        }

        private void PlayExtensionSquash()
        {
            if (body == null || body.IsReturning) return;
            bodyTween?.Kill();
            float duration = Mathf.Max(0.02f, visualSettings.InteractionDuration);
            float amount = visualSettings.ExtensionSquash;
            bodyTween = DOTween.Sequence()
                .Append(DOVirtual.Float(bodySquash, amount, duration * 0.2f, value => bodySquash = value).SetEase(Ease.OutQuad))
                .Append(DOVirtual.Float(amount, -amount * 0.6f, duration * 0.35f, value => bodySquash = value).SetEase(Ease.InOutSine))
                .Append(DOVirtual.Float(-amount * 0.6f, 0f, duration * 0.45f, value => bodySquash = value).SetEase(Ease.OutSine));
        }

        private void Pulse(MovableEntity entity, float amount)
        {
            if (entity == null || entity.IsReturning) return;
            float duration = Mathf.Max(0.02f, visualSettings.InteractionDuration);
            if (entity == body)
            {
                bodyTween?.Kill(); bodySquash = amount;
                bodyTween = DOVirtual.Float(amount, 0f, duration, value => bodySquash = value).SetEase(Ease.OutSine);
            }
            else if (entity == head)
            {
                headSpringActive = false;
                headTween?.Kill(); headSquash = amount;
                headTween = DOVirtual.Float(amount, 0f, duration, value => headSquash = value).SetEase(Ease.OutSine);
            }
        }

        private void OnHeadSpringBounce(MovableEntity bouncingEntity)
        {
            if (visualSettings == null || head == null || head.IsReturning) return;
            headTween?.Kill();
            headSpringActive = true;
            float duration = Mathf.Max(0.02f, visualSettings.InteractionDuration);
            float amount = visualSettings.SpringSquash;
            headTween = DOTween.Sequence()
                .Append(DOVirtual.Float(headSquash, amount, duration * 0.2f, value => headSquash = value).SetEase(Ease.OutQuad))
                .Append(DOVirtual.Float(amount, -amount * 0.6f, duration * 0.35f, value => headSquash = value).SetEase(Ease.InOutSine))
                .Append(DOVirtual.Float(-amount * 0.6f, 0f, duration * 0.45f, value => headSquash = value).SetEase(Ease.OutSine))
                .OnComplete(() => headSpringActive = false);
        }

        public void ResetVisual(MovableEntity entity)
        {
            if (!captured || entity == null || entity.Visual == null) return;
            bool lower = entity == body;
            if (lower) { bodyTween?.Kill(); bodySquash = bodyLag = motionLean = 0f; }
            else { headTween?.Kill(); headSpringActive = false; headSquash = headLag = headLean = 0f; }
            entity.Visual.localScale = lower ? bodyScale : headScale;
            entity.Visual.localPosition = lower ? bodyPosition : headPosition;
            entity.Visual.localRotation = lower ? bodyRotation : headRotation;
        }

        private void Unsubscribe()
        {
            if (subscribedHead != null) subscribedHead.SpringBounced -= OnHeadSpringBounce;
            subscribedHead = null;
            if (subscribedController == null) return;
            subscribedController.Effect -= OnEffect;
            subscribedController.PhaseChanged -= OnPhase;
            subscribedController.ContactFeedback -= OnContact;
            subscribedController = null;
        }

        private void OnDisable() { Unsubscribe(); ResetVisual(body); ResetVisual(head); }
        private void OnDestroy() => Unsubscribe();
    }
}
