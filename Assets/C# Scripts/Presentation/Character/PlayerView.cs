using DG.Tweening;
using UnityEngine;

namespace TapTap
{
    public sealed class PlayerView : MonoBehaviour
    {
        public struct FacingState
        {
            public float BodyYaw, HeadYaw;
        }

        [SerializeField] private PlayerController controller;
        [SerializeField] private MovableEntity body;
        [SerializeField] private MovableEntity head;
        [SerializeField] private PlayerConfig config;
        [SerializeField] private PlayerVisualConfig visualSettings;
        [SerializeField] private Transform bodyFacing;
        [SerializeField] private Transform headFacing;
        [SerializeField] private Animator bodyAnimator;
        [SerializeField] private Animator headAnimator;
        private FacingState facing = new FacingState { BodyYaw = 180f, HeadYaw = 180f };
        private FacingState previousFacing = new FacingState { BodyYaw = 180f, HeadYaw = 180f };
        private static readonly int MovingParameter = Animator.StringToHash("Moving");
        private static readonly int IdleState = Animator.StringToHash("Base Layer.Idle");
        private static readonly int WalkState = Animator.StringToHash("Base Layer.Walk");
        private bool bodyWalking, headWalking;
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
        private Tween bodyTween, headTween;
        private bool headSpringActive;
        public bool ExitPresentation { get; private set; }
        private AnimatorUpdateMode bodyAnimationMode, headAnimationMode;

        public void BeginExitPresentation(bool includeHead)
        {
            EnsureVisuals();
            ClearTransientEffects();
            // Both machines start the cinematic from the same state, including when
            // pickup interrupted an already walking lower half.
            SetWalking(bodyAnimator, ref bodyWalking, false, 0f);
            if (includeHead) SetWalking(headAnimator, ref headWalking, false, 0f);
            ExitPresentation = true;
            if (guide != null) guide.enabled = false;
            if (magnet != null) magnet.Render(body, head, false, false, config.UnitSize, true);
            if (bodyAnimator != null)
            {
                bodyAnimationMode = bodyAnimator.updateMode;
                bodyAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
                bodyAnimator.speed = 1f;
            }
            if (headAnimator != null)
            {
                headAnimationMode = headAnimator.updateMode;
                headAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
                headAnimator.speed = includeHead ? 1f : 0f;
            }
        }

        public void RenderExitPose(Vector3 lowerPosition, Vector3 upperPosition, float yaw, bool walking, bool includeHead)
        {
            if (!ExitPresentation) return;
            body.Visual.position = lowerPosition;
            facing.BodyYaw = yaw;
            if (includeHead) { head.Visual.position = upperPosition; facing.HeadYaw = yaw; }
            previousFacing = facing;
            RenderFacing(true);
            SetWalking(bodyAnimator, ref bodyWalking, walking, 0f);
            if (includeHead) SetWalking(headAnimator, ref headWalking, walking, 0f);
        }

        public void EndExitPresentation()
        {
            if (!ExitPresentation) return;
            ExitPresentation = false;
            ResetVisual(body); ResetVisual(head);
            if (bodyAnimator != null) { bodyAnimator.updateMode = bodyAnimationMode; bodyAnimator.speed = 1f; }
            if (headAnimator != null) { headAnimator.updateMode = headAnimationMode; headAnimator.speed = 1f; }
            SetWalking(bodyAnimator, ref bodyWalking, false, 0f);
            SetWalking(headAnimator, ref headWalking, false, 0f);
        }

        public void ConfigureVisuals(PlayerVisualConfig settings) => visualSettings = settings;
        public void ConfigureModels(Transform lowerFacing, Transform upperFacing, Animator lowerAnimator, Animator upperAnimator)
        {
            bodyFacing = lowerFacing; headFacing = upperFacing;
            bodyAnimator = lowerAnimator; headAnimator = upperAnimator;
            if (bodyAnimator != null) bodyAnimator.applyRootMotion = false;
            if (headAnimator != null) headAnimator.applyRootMotion = false;
            RenderFacing(true);
        }

        public FacingState CaptureFacing() => facing;

        public void RestoreFacing(in FacingState state)
        {
            facing = previousFacing = state;
            RenderFacing(true);
        }

        public void ResetFacing()
        {
            float front = visualSettings != null ? visualSettings.FrontYaw : 180f;
            facing = previousFacing = new FacingState { BodyYaw = front, HeadYaw = front };
            RenderFacing(true);
        }

        // Advance with the gameplay tick, so snapshots include the actual smoothed pose.
        public void AdvanceFacing(float dt)
        {
            if (ExitPresentation) return;
            if (dt <= 0f || controller == null || body == null || head == null) return;
            previousFacing = facing;
            float front = visualSettings != null ? visualSettings.FrontYaw : 180f;
            float right = visualSettings != null ? visualSettings.RightYaw : 120f;
            float left = visualSettings != null ? visualSettings.LeftYaw : 240f;
            float smoothTime = visualSettings != null ? visualSettings.TurnSmoothTime : 0.05f;
            float blend = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, smoothTime));
            bool steering = controller.Phase != PlayerPhase.Extending && controller.Phase != PlayerPhase.Retracting
                && controller.Phase != PlayerPhase.Pulling && !body.IsReturning;
            // Velocity excludes conveyor motion, and is zeroed when steering is blocked by a wall.
            float movement = steering ? body.Velocity.x : 0f;
            float target = Mathf.Abs(movement) < 0.001f ? front : movement > 0f ? right : left;
            if (!body.IsReturning) facing.BodyYaw = Mathf.LerpAngle(facing.BodyYaw, target, blend);
            if (controller.HasMagneticConnection && !head.IsReturning)
            {
                facing.HeadYaw = Mathf.LerpAngle(facing.HeadYaw, target, blend);
                if (Mathf.Abs(Mathf.DeltaAngle(facing.HeadYaw, facing.BodyYaw)) < 0.05f)
                    facing.HeadYaw = facing.BodyYaw;
            }

        }

        // Animation is presentation only. Rewind freezes the pose; no gait history is recorded.
        public void RenderMovementAnimations(bool paused)
        {
            if (ExitPresentation) return;
            if (controller == null || body == null || head == null) return;
            paused |= WorldMapProgression.InputBlocked || LevelAnnotation.IsFeedbackOpen;
            if (bodyAnimator != null) bodyAnimator.speed = paused ? 0f : 1f;
            if (headAnimator != null) headAnimator.speed = paused ? 0f : 1f;
            if (paused) return;
            bool moving = !body.IsReturning && body.Motor.Grounded && Mathf.Abs(body.Velocity.x) > 0.001f
                && controller.Phase != PlayerPhase.Extending && controller.Phase != PlayerPhase.Retracting
                && controller.Phase != PlayerPhase.Pulling;
            SetWalking(bodyAnimator, ref bodyWalking, moving, 0f);
            float upperOffset = 0f;
            if (moving && bodyAnimator != null && bodyAnimator.runtimeAnimatorController != null)
            {
                AnimatorStateInfo state = bodyAnimator.IsInTransition(0)
                    ? bodyAnimator.GetNextAnimatorStateInfo(0) : bodyAnimator.GetCurrentAnimatorStateInfo(0);
                if (state.fullPathHash == WalkState) upperOffset = Mathf.Repeat(state.normalizedTime, 1f) * state.length;
            }
            SetWalking(headAnimator, ref headWalking, moving && controller.HasMagneticConnection && !head.IsReturning, upperOffset);
            if (bodyWalking && headWalking && bodyAnimator != null && headAnimator != null
                && !bodyAnimator.IsInTransition(0) && !headAnimator.IsInTransition(0))
            {
                AnimatorStateInfo lower = bodyAnimator.GetCurrentAnimatorStateInfo(0);
                AnimatorStateInfo upper = headAnimator.GetCurrentAnimatorStateInfo(0);
                if (lower.fullPathHash == WalkState && upper.fullPathHash == WalkState
                    && Mathf.Abs(lower.normalizedTime - upper.normalizedTime) > 0.005f)
                {
                    // A newly joined machine can lose one evaluation frame during its crossfade.
                    // Align after that blend, without storing any animation state in rewind history.
                    headAnimator.Play(WalkState, 0, lower.normalizedTime);
                    headAnimator.Update(0f);
                }
            }
        }

        private static void SetWalking(Animator animator, ref bool playing, bool walking, float offset)
        {
            if (animator == null || animator.runtimeAnimatorController == null || playing == walking) return;
            int state = walking ? WalkState : IdleState;
            if (!animator.HasState(0, state)) return;
            animator.SetBool(MovingParameter, walking);
            animator.CrossFadeInFixedTime(state, 0.08f, 0, walking ? offset : 0f);
            playing = walking;
        }

        private void RenderFacing(bool immediate)
        {
            float blend = immediate ? 1f : Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            if (bodyFacing != null)
                bodyFacing.localRotation = Quaternion.Euler(0f, Mathf.LerpAngle(previousFacing.BodyYaw, facing.BodyYaw, blend), 0f);
            if (headFacing != null)
                headFacing.localRotation = Quaternion.Euler(0f, Mathf.LerpAngle(previousFacing.HeadYaw, facing.HeadYaw, blend), 0f);
        }
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
            if (ExitPresentation) return;
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
            if (ExitPresentation) return;
            if (controller == null || body == null || head == null || config == null) return;
            EnsureVisuals();
            RenderFacing(true);
            RenderMovementAnimations(true);
            if (captured && !head.IsReturning)
                head.Visual.localPosition = headPosition + PickupOffset();
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
            if (ExitPresentation) return;
            if (dt <= 0f || controller == null || body == null || head == null || config == null) return;
            EnsureVisuals();
            RenderFacing(false);
            RenderMovementAnimations(false);
            float unit = body.Motor.Size.x / Mathf.Max(0.01f, body.Motor.Collider.size.x);
            bool returning = body.IsReturning || head.IsReturning;
            bool connected = controller.HasMagneticConnection && controller.Phase != PlayerPhase.Joined && !returning;
            RenderGuide(unit, returning, dt, false);
            if (!captured || visualSettings == null)
            {
                if (captured && !head.IsReturning)
                    head.Visual.localPosition = headPosition + PickupOffset();
                if (magnet != null) magnet.Render(body, head, connected, controller.Phase == PlayerPhase.Pulling, unit);
                return;
            }
            if (!body.IsReturning)
            {
                float stretch = Mathf.Clamp(Mathf.Abs(body.Velocity.y) / (12f * unit), 0f, 1f) * visualSettings.MotionStretch;
                Vector3 factor = new Vector3(1f + bodySquash - stretch * 0.5f, 1f - bodySquash + stretch, 1f);
                body.Visual.localScale = Vector3.Scale(bodyScale, factor);
                body.Visual.localPosition = bodyPosition
                    + Vector3.up * ((factor.y - 1f) * body.Motor.Collider.size.y * 0.5f);
                body.Visual.localRotation = bodyRotation;
            }
            if (!head.IsReturning)
            {
                float stretch = controller.Phase == PlayerPhase.Extending || controller.Phase == PlayerPhase.Pulling ? visualSettings.MotionStretch : 0f;
                Vector3 factor = new Vector3(1f + headSquash - stretch * 0.5f, 1f - headSquash + stretch, 1f);
                head.Visual.localScale = Vector3.Scale(headScale, factor);
                head.Visual.localPosition = headPosition + PickupOffset()
                    + Vector3.up * (headSpringActive ? (factor.y - 1f) * head.Motor.Collider.size.y * 0.5f : 0f);
                head.Visual.localRotation = headRotation;
            }
            if (magnet != null) magnet.Render(body, head, connected, controller.Phase == PlayerPhase.Pulling, unit);
        }

        private Vector3 PickupOffset()
        {
            if (controller.Phase != PlayerPhase.PickingUp) return Vector3.zero;
            // Follow the rendered body pose so Rigidbody interpolation cannot add a second head movement.
            Vector3 target = body.transform.position + Vector3.up * config.ToWorld(config.JoinedOffset)
                + (Vector3)controller.PickupVisualOffset;
            Vector3 offset = target - head.transform.position;
            return head.Visual.parent != null ? head.Visual.parent.InverseTransformVector(offset) : offset;
        }

        private void OnContact(PlayerContactFeedback contact)
        {
            if (visualSettings == null || contact.Entity == null || contact.Entity.IsReturning) return;
            float unit = contact.Entity.Motor.Size.x / Mathf.Max(0.01f, contact.Entity.Motor.Collider.size.x);
            if (particles != null) particles.Burst(contact.Point, contact.Normal, contact.Assembly, unit);
            if (contact.Assembly) return;
            float amount = visualSettings.LandingSquash * Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(contact.Speed / (8f * unit)));
            if (contact.Entity != head || !headSpringActive) Pulse(contact.Entity, amount);
        }

        private void OnEffect(PlayerEffect effect)
        {
            EffectManager.Instance.Play(effect, body.Visual);
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
            if (lower) { bodyTween?.Kill(); bodySquash = 0f; }
            else { headTween?.Kill(); headSpringActive = false; headSquash = 0f; }
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
            subscribedController.ContactFeedback -= OnContact;
            subscribedController = null;
        }

        private void OnDisable()
        {
            EndExitPresentation();
            Unsubscribe(); ResetVisual(body); ResetVisual(head);
            bodyWalking = headWalking = false;
            if (bodyAnimator != null) bodyAnimator.speed = 1f;
            if (headAnimator != null) headAnimator.speed = 1f;
        }
        private void OnDestroy() => Unsubscribe();
    }
}
