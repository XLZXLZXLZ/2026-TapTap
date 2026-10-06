using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace TapTap
{
    public enum PlayerPhase { Joined, Extending, Holding, Retracting, Pulling, Detached }
    public enum PlayerEffect { HeadLanded, PullBlocked, Joined, DownwardJoined, Bounce, Death, RecallFailed, HeadReturned, Landed }

    public struct PlayerContactFeedback
    {
        public MovableEntity Entity;
        public Vector2 Point;
        public Vector2 Normal;
        public float Speed;
        public bool Assembly;
    }

    public sealed class PlayerController : MonoBehaviour
    {
        private enum Outcome { None, HeadLanded, Join, Detach, DownwardJoin }
        [SerializeField] private PlayerInput input;
        [SerializeField] private PlayerConfig config;
        [SerializeField] private MovableEntity body;
        [SerializeField] private MovableEntity head;
        [SerializeField] private RespawnService respawn;
        [SerializeField] private PlayerView view;
        [SerializeField] private PlayerPhase phase;

        private bool initialized;
        private bool retractPending;
        private bool recallBuffered;
        private bool bufferedRelease;
        private bool guideActive;
        private float clock;
        private float recallReadyAt;
        private float failedGuideUntil;
        private float actionTime;
        private float retractStartExtra;
        private float pullStartX;
        private Vector2 pullBodyTarget;
        private float pendingLaunchHeight;
        private float headFallSpeed;
        private Outcome outcome;
        private bool bodyDied;
        private bool headDied;
        private MovableEntity pairSupport;
        private Vector2 pairContactNormal;
        private bool bodyGroundContact;
        private bool headGroundContact;
        private readonly List<PlayerEffect> effects = new List<PlayerEffect>(4);
        private readonly List<PlayerContactFeedback> contacts = new List<PlayerContactFeedback>(3);

        public PlayerPhase Phase => phase;
        public MovableEntity Body => body;
        public MovableEntity Head => head;
        public PlayerConfig Config => config;
        public Vector2 PresentationPosition => respawn != null && body != null
            ? respawn.FollowPosition(body) : body != null ? (Vector2)body.transform.position : (Vector2)transform.position;
        public bool HasMagneticConnection => phase != PlayerPhase.Detached;
        public bool GuideVisible => guideActive || clock < failedGuideUntil;
        public bool CanRecall => !body.IsReturning && !head.IsReturning
            && Mathf.Abs(body.Motor.Position.x - head.Motor.Position.x) <= config.ToWorld(config.AlignmentTolerance)
            && head.Motor.Position.y - body.Motor.Position.y > config.ToWorld(config.JoinedOffset) + config.JoinTolerance;
        public event Action<PlayerEffect> Effect;
        public event Action<PlayerPhase> PhaseChanged;
        public event Action<PlayerContactFeedback> ContactFeedback;

        public void Configure(PlayerInput controls, PlayerConfig settings, MovableEntity lower,
            MovableEntity upper, RespawnService returns, PlayerView presentation)
        {
            input = controls; config = settings; body = lower; head = upper;
            respawn = returns; view = presentation;
            initialized = false;
            Initialize();
        }

        private void Start() => Initialize();

        private void Initialize()
        {
            if (initialized) return;
            if (config == null || body == null || head == null || input == null || respawn == null)
                return;
            body.Configure(EntityPart.Body, true, config.WorldGravity);
            head.Configure(EntityPart.Head, true, config.WorldGravity);
            body.Motor.SetSkin(config.Skin);
            head.Motor.SetSkin(config.Skin);
            body.SpringHeight = head.SpringHeight = config.ToWorld(config.SpringHeight);
            head.SpringEnabled = phase == PlayerPhase.Detached;
            UpdateJoinedCollision();
            respawn.Configure(config);
            if (Application.isPlaying && !respawn.HasCheckpoint)
                respawn.SetCheckpoint(body.Motor.Position);
            if (view != null) view.Configure(this, body, head, config);
            initialized = true;
        }

        private void FixedUpdate() => SimulateStep(Time.fixedDeltaTime);

        public void SimulateStep(float dt)
        {
            Initialize();
            if (!initialized || dt <= 0f) return;
            clock += dt;
            respawn.Simulate(dt);
            body.Motor.BeginStep(dt);
            head.Motor.BeginStep(dt);
            effects.Clear();
            contacts.Clear();
            outcome = Outcome.None;
            bodyDied = headDied = false;
            headFallSpeed = head.Velocity.y;

            ConsumeInput();
            if (phase == PlayerPhase.Detached)
            {
                TickDetached(dt);
            }
            else if (!body.IsReturning && !head.IsReturning)
            {
                switch (phase)
                {
                    case PlayerPhase.Joined: TickMagneticMovement(dt); break;
                    case PlayerPhase.Extending: TickMagneticMovement(dt); TickExtension(dt); break;
                    case PlayerPhase.Holding:
                        TickMagneticMovement(dt);
                        if (retractPending) BeginRetraction();
                        break;
                    case PlayerPhase.Retracting: TickMagneticMovement(dt); TickRetraction(dt); break;
                    case PlayerPhase.Pulling: TickPull(dt); break;
                }
            }

            CollectWorldInteractions();
            ResolveOutcomes();
            body.Motor.Commit();
            head.Motor.Commit();
            foreach (PlayerEffect effect in effects) Effect?.Invoke(effect);
            foreach (PlayerContactFeedback contact in contacts) ContactFeedback?.Invoke(contact);
        }

        private void ConsumeInput()
        {
            while (input.TryConsume(out MagnetInput edge))
            {
                if (body.IsReturning || head.IsReturning) continue;
                if (phase == PlayerPhase.Joined && edge == MagnetInput.Press)
                {
                    actionTime = 0f;
                    retractPending = false;
                    SetPhase(PlayerPhase.Extending);
                }
                else if (phase == PlayerPhase.Extending && edge == MagnetInput.Release)
                    retractPending = true;
                else if (phase == PlayerPhase.Holding && edge == MagnetInput.Release)
                    BeginRetraction();
                else if (phase == PlayerPhase.Detached)
                    ConsumeDetachedInput(edge);
            }
        }

        private void ConsumeDetachedInput(MagnetInput edge)
        {
            if (edge == MagnetInput.Press)
            {
                if (clock < recallReadyAt)
                {
                    if (!recallBuffered) { recallBuffered = true; bufferedRelease = false; }
                }
                else guideActive = true;
            }
            else if (recallBuffered) bufferedRelease = true;
            else if (guideActive) AttemptRecall();
        }

        private void TickMagneticMovement(float dt)
        {
            Vector2 velocity = body.Velocity;
            float impactSpeed = Mathf.Max(0f, -velocity.y);
            velocity.x = input.Horizontal * config.ToWorld(config.MoveSpeed);
            if (body.Motor.Grounded && velocity.y <= 0f) velocity.y = 0f;
            velocity.y -= config.WorldGravity * dt;
            impactSpeed = Mathf.Max(impactSpeed, -velocity.y);
            float conveyor = body.Motor.Grounded ? body.Motor.ConveyorSpeed
                : phase == PlayerPhase.Joined ? 0f : head.Motor.ConveyorSpeed;
            Vector2 delta = new Vector2((velocity.x + conveyor) * dt, velocity.y * dt);
            if (MovePairAxis(new Vector2(delta.x, 0f)) < 1f) velocity.x = 0f;
            pairSupport = null;
            pairContactNormal = Vector2.zero;
            if (MovePairAxis(new Vector2(0f, delta.y)) < 1f)
            {
                if (delta.y < 0f && pairSupport != null && pairSupport.Part == EntityPart.Head
                    && pairSupport.SpringEnabled && !pairSupport.IsReturning)
                {
                    velocity.y = Mathf.Sqrt(2f * config.WorldGravity * pairSupport.SpringHeight);
                    effects.Add(PlayerEffect.Bounce);
                }
                else velocity.y = 0f;
            }
            bool groundContact = delta.y < 0f && pairContactNormal.y > 0.5f;
            QueueLanding(body, groundContact, bodyGroundContact, impactSpeed, pairContactNormal);
            bodyGroundContact = groundContact && velocity.y <= 0f;
            body.Velocity = head.Velocity = velocity;
        }

        private float MovePairAxis(Vector2 delta)
        {
            if (delta.sqrMagnitude < 0.00000001f) return 1f;
            SweepResult a = body.Motor.Sweep(delta, head);
            SweepResult b = phase == PlayerPhase.Joined ? SweepResult.Clear : head.Motor.Sweep(delta, body);
            float fraction = Mathf.Min(a.Fraction, b.Fraction);
            if (delta.y < 0f && fraction < 1f)
            {
                SweepResult contact = a.Fraction <= b.Fraction ? a : b;
                pairSupport = contact.Entity;
                pairContactNormal = contact.Normal;
            }
            Vector2 allowed = delta * fraction;
            body.Motor.StageTranslation(allowed);
            head.Motor.StageTranslation(allowed);
            return fraction;
        }

        private void TickExtension(float dt)
        {
            actionTime += dt;
            float t = Mathf.Clamp01(actionTime / config.ExtensionDuration);
            float wantedExtra = DOVirtual.EasedValue(0f, config.ToWorld(config.MaxExtension), t, Ease.OutQuad);
            float extra = head.Motor.Position.y - body.Motor.Position.y - config.ToWorld(config.JoinedOffset);
            MoveResult moved = head.Motor.Move(Vector2.up * Mathf.Max(0f, wantedExtra - extra), body);
            if (moved.Blocked || t >= 1f) SetPhase(PlayerPhase.Holding);
        }

        private void BeginRetraction()
        {
            actionTime = 0f;
            retractPending = false;
            retractStartExtra = Mathf.Max(0f, head.Motor.Position.y - body.Motor.Position.y
                - config.ToWorld(config.JoinedOffset));
            SetPhase(PlayerPhase.Retracting);
        }

        private void TickRetraction(float dt)
        {
            actionTime += dt;
            float t = Mathf.Clamp01(actionTime / config.RetractionDuration);
            float wantedOffset = config.ToWorld(config.JoinedOffset)
                + DOVirtual.EasedValue(retractStartExtra, 0f, t, Ease.InQuad);
            float currentOffset = head.Motor.Position.y - body.Motor.Position.y;
            MoveResult moved = head.Motor.Move(Vector2.down * Mathf.Max(0f, currentOffset - wantedOffset), body);
            if (moved.Blocked && moved.Normal.y > 0.5f)
            {
                outcome = Outcome.HeadLanded;
                effects.Add(PlayerEffect.HeadLanded);
                QueueLanding(head, true, headGroundContact, 1f, moved.Normal);
                headGroundContact = true;
            }
            else if (head.Motor.Position.y - body.Motor.Position.y
                <= config.ToWorld(config.JoinedOffset) + config.JoinTolerance)
            {
                pendingLaunchHeight = 0f;
                outcome = Outcome.Join;
            }
        }

        private void BeginPull(bool distant)
        {
            actionTime = 0f;
            pullStartX = body.Motor.Position.x;
            pullBodyTarget = head.Motor.Position - Vector2.up * config.ToWorld(config.JoinedOffset);
            pendingLaunchHeight = distant ? config.DistantLaunchHeight : config.NormalLaunchHeight;
            body.Velocity = head.Velocity = Vector2.zero;
            guideActive = false;
            head.SpringEnabled = false;
            SetPhase(PlayerPhase.Pulling);
        }

        private void TickPull(float dt)
        {
            actionTime += dt;
            float acceleration = Mathf.Clamp01(actionTime / config.PullAccelerationTime);
            float speed = DOVirtual.EasedValue(config.ToWorld(config.PullStartSpeed),
                config.ToWorld(config.PullMaxSpeed), acceleration, Ease.InQuad);
            float alignment = Mathf.Clamp01(actionTime / config.AlignmentDuration);
            float wantedX = DOVirtual.EasedValue(pullStartX, pullBodyTarget.x, alignment, Ease.OutSine);
            float wantedY = Mathf.MoveTowards(body.Motor.Position.y, pullBodyTarget.y, speed * dt);
            Vector2 delta = new Vector2(wantedX, wantedY) - body.Motor.Position;
            MoveResult moved = body.Motor.Move(delta, head);
            if (Vector2.Distance(body.Motor.Position, pullBodyTarget) <= config.JoinTolerance)
            {
                MoveResult final = body.Motor.Move(pullBodyTarget - body.Motor.Position, head);
                outcome = final.Blocked ? Outcome.Detach : Outcome.Join;
            }
            else if (moved.Blocked) outcome = Outcome.Detach;
        }

        private void TickDetached(float dt)
        {
            head.SpringEnabled = !head.IsReturning;
            if (recallBuffered && clock >= recallReadyAt && !body.IsReturning && !head.IsReturning)
            {
                guideActive = true;
                bool execute = bufferedRelease || !input.SpaceHeld;
                recallBuffered = bufferedRelease = false;
                if (execute) AttemptRecall();
            }
            if (phase != PlayerPhase.Detached) { TickPull(dt); return; }
            if (!body.IsReturning)
            {
                float impactSpeed = Mathf.Max(0f, config.WorldGravity * dt - body.Velocity.y);
                body.SimulateFree(dt, input.Horizontal * config.ToWorld(config.MoveSpeed));
                bool contact = body.Motor.LastMoveResult.Blocked && body.Motor.LastMoveResult.Normal.y > 0.5f;
                QueueLanding(body, contact, bodyGroundContact, impactSpeed, body.Motor.LastMoveResult.Normal);
                bodyGroundContact = contact && body.Velocity.y <= 0f;
            }
            if (!head.IsReturning)
            {
                float impactSpeed = Mathf.Max(0f, config.WorldGravity * dt - head.Velocity.y);
                head.SimulateFree(dt, 0f);
                bool contact = head.Motor.LastMoveResult.Blocked && head.Motor.LastMoveResult.Normal.y > 0.5f;
                QueueLanding(head, contact, headGroundContact, impactSpeed, head.Motor.LastMoveResult.Normal);
                headGroundContact = contact && head.Velocity.y <= 0f;
            }

            if (!body.IsReturning && !head.IsReturning)
            {
                foreach (MoveResult contact in head.Motor.Contacts)
                {
                    if (contact.Entity == body && HeadContactRules.Evaluate(
                        EntityPart.Head, EntityPart.Body, contact.Normal) == HeadContactAction.AssembleDownward)
                        outcome = Outcome.DownwardJoin;
                }
                foreach (MoveResult contact in body.Motor.Contacts)
                {
                    if (contact.Entity == head && contact.Normal.y > 0.5f)
                        effects.Add(PlayerEffect.Bounce);
                }
            }
        }

        private void QueueLanding(MovableEntity entity, bool groundContact, bool previousContact, float impactSpeed, Vector2 normal)
        {
            if (!groundContact || previousContact) return;
            if (normal.sqrMagnitude < 0.1f) normal = Vector2.up;
            contacts.Add(new PlayerContactFeedback
            {
                Entity = entity, Normal = normal, Speed = impactSpeed,
                Point = entity.Motor.Position + entity.Motor.CenterOffset
                    - Vector2.Scale(normal, entity.Motor.Size * 0.5f)
            });
            if (impactSpeed >= config.ToWorld(config.LandingShakeSpeed)
                && !effects.Contains(PlayerEffect.Landed))
                effects.Add(PlayerEffect.Landed);
        }

        private void AttemptRecall()
        {
            guideActive = false;
            recallReadyAt = clock + config.RecallCooldown;
            if (!CanRecall)
            {
                failedGuideUntil = clock + 0.3f;
                effects.Add(PlayerEffect.RecallFailed);
                return;
            }
            bool distant = head.Motor.Position.y - body.Motor.Position.y
                > config.ToWorld(config.DistantThreshold);
            BeginPull(distant);
        }

        private void CollectWorldInteractions()
        {
            if (!body.IsReturning)
            {
                CheckpointFlag flag = FindTrigger<CheckpointFlag>(body);
                if (flag != null) respawn.SetCheckpoint(flag.SpawnPosition);
                bodyDied = body.Motor.Position.y < config.DeathBoundary
                    || FindTrigger<LethalZone>(body, phase == PlayerPhase.Joined) != null;
            }
            if (!head.IsReturning)
                headDied = head.Motor.Position.y < config.DeathBoundary || FindTrigger<LethalZone>(head) != null;
        }

        private static T FindTrigger<T>(MovableEntity entity, bool useCollisionBounds = false) where T : Component
        {
            Bounds bounds = useCollisionBounds ? entity.Motor.CollisionBounds
                : new Bounds(entity.Motor.Position + entity.Motor.CenterOffset, entity.Motor.Size);
            Vector2 centerOffset = (Vector2)bounds.center - entity.Motor.Position;
            Vector2 size = (Vector2)bounds.size * 0.98f;
            Collider2D[] overlaps = Physics2D.OverlapBoxAll(bounds.center, size, 0f);
            foreach (Collider2D collider in overlaps)
            {
                T found = collider.GetComponentInParent<T>();
                if (found != null) return found;
            }
            foreach (MotionSegment segment in entity.Motor.MotionSegments)
            {
                Vector2 delta = segment.To - segment.From;
                if (delta.sqrMagnitude < 0.000001f) continue;
                RaycastHit2D[] hits = Physics2D.BoxCastAll(segment.From + centerOffset, size,
                    0f, delta.normalized, delta.magnitude);
                foreach (RaycastHit2D hit in hits)
                {
                    T found = hit.collider.GetComponentInParent<T>();
                    if (found != null) return found;
                }
            }
            return null;
        }

        private void ResolveOutcomes()
        {
            if (bodyDied || headDied)
            {
                bool grouped = HasMagneticConnection;
                CancelAction();
                effects.Clear();
                contacts.Clear();
                effects.Add(PlayerEffect.Death);
                if (grouped)
                {
                    SetPhase(PlayerPhase.Joined);
                    respawn.BeginGroupReturn(body, head, config.ToWorld(config.JoinedOffset));
                }
                else
                {
                    if (bodyDied) respawn.BeginSingleReturn(body);
                    if (headDied) respawn.BeginSingleReturn(head);
                }
                return;
            }
            switch (outcome)
            {
                case Outcome.HeadLanded:
                    CancelAction();
                    head.Velocity = Vector2.zero;
                    head.SpringEnabled = true;
                    SetPhase(PlayerPhase.Detached);
                    headGroundContact = true;
                    break;
                case Outcome.Join: ApplyJoin(pendingLaunchHeight, false); break;
                case Outcome.DownwardJoin: ApplyJoin(0f, true); break;
                case Outcome.Detach:
                    body.Velocity = head.Velocity = Vector2.zero;
                    head.SpringEnabled = true;
                    SetPhase(PlayerPhase.Detached);
                    effects.Add(PlayerEffect.PullBlocked);
                    break;
            }
        }

        private void ApplyJoin(float height, bool downward)
        {
            Vector2 target = body.Motor.Position + Vector2.up * config.ToWorld(config.JoinedOffset);
            MoveResult correction = head.Motor.Move(target - head.Motor.Position, body);
            if (correction.Blocked || !body.Motor.CanJoinWith(head.Motor))
            {
                SetPhase(PlayerPhase.Detached);
                head.SpringEnabled = true;
                return;
            }
            Vector2 velocity = body.Velocity;
            if (downward)
            {
                velocity.y = Mathf.Min(-4f * config.UnitSize, headFallSpeed);
                body.Motor.IgnoreOneWayFor(0.3f);
                head.Motor.IgnoreOneWayFor(0.3f);
            }
            else if (height > 0f) velocity.y = config.LaunchSpeed(height);
            body.Velocity = head.Velocity = velocity;
            head.SpringEnabled = false;
            CancelAction();
            SetPhase(PlayerPhase.Joined);
            contacts.RemoveAll(contact => contact.Entity == head);
            contacts.Add(new PlayerContactFeedback
            {
                Entity = body, Assembly = true, Speed = Mathf.Abs(headFallSpeed),
                Point = (body.Motor.Position + Vector2.up * body.Motor.Size.y * 0.5f
                    + head.Motor.Position - Vector2.up * head.Motor.Size.y * 0.5f) * 0.5f,
                Normal = downward || height <= 0f ? Vector2.up : Vector2.down
            });
            if (downward || height > 0f) effects.Add(downward ? PlayerEffect.DownwardJoined : PlayerEffect.Joined);
            else effects.Add(PlayerEffect.HeadReturned);
        }

        private void CancelAction()
        {
            actionTime = 0f;
            retractPending = recallBuffered = bufferedRelease = guideActive = false;
            failedGuideUntil = 0f;
            input.ClearMagnetBuffer();
        }

        private void SetPhase(PlayerPhase next)
        {
            if (phase == next) return;
            phase = next;
            if (next == PlayerPhase.Pulling || next == PlayerPhase.Detached)
                bodyGroundContact = headGroundContact = false;
            UpdateJoinedCollision();
            PhaseChanged?.Invoke(next);
        }

        private void UpdateJoinedCollision()
        {
            bool joined = phase == PlayerPhase.Joined;
            body.Motor.SetJoinedPartner(joined ? head.Motor : null, joined);
            head.Motor.SetJoinedPartner(joined ? body.Motor : null, false);
        }

        public void TeleportJoined(Vector2 bodyPosition)
        {
            Initialize();
            if (!initialized) return;
            respawn.CancelAll();
            CancelAction();
            body.Motor.Teleport(bodyPosition);
            head.Motor.Teleport(bodyPosition + Vector2.up * config.ToWorld(config.JoinedOffset));
            body.Velocity = head.Velocity = Vector2.zero;
            bodyGroundContact = headGroundContact = false;
            head.SpringEnabled = false;
            SetPhase(PlayerPhase.Joined);
        }
    }
}
