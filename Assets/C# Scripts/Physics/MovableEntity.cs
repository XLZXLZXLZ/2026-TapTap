using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(KinematicMotor2D))]
    public sealed class MovableEntity : MonoBehaviour, IRewindable<MovableEntity.Snapshot>
    {
        public struct Snapshot
        {
            public Vector2 Position, Velocity;
            public float IgnoreOneWayRemaining;
            public bool CollisionsEnabled, IsReturning, SpringEnabled;
        }
        private static readonly List<MovableEntity> activeEntities = new List<MovableEntity>();
        private KinematicMotor2D motor;

        public EntityPart Part = EntityPart.Other;
        public Transform Visual;
        public Vector2 Velocity;
        public bool IsReturning;
        public bool ControlledExternally;
        [Min(0.01f)] public float Gravity = 24f;
        [Min(0f)] public float SpringHeight = 3f;
        public bool SpringEnabled = true;
        public event Action<MovableEntity> SpringBounced;

        public void NotifySpringBounce(MovableEntity bouncingEntity) => SpringBounced?.Invoke(bouncingEntity);

        public static IReadOnlyList<MovableEntity> ActiveEntities => activeEntities;
        public KinematicMotor2D Motor { get { Initialize(); return motor; } }

        public void Initialize()
        {
            if (motor == null)
                motor = GetComponent<KinematicMotor2D>();
            motor.Initialize();
        }

        public void Configure(EntityPart part, bool controlledExternally, float gravity)
        {
            Part = part;
            ControlledExternally = controlledExternally;
            Gravity = Mathf.Max(0.01f, gravity);
            Initialize();
        }

        private void Awake() => Initialize();

        private void OnEnable()
        {
            if (!activeEntities.Contains(this))
                activeEntities.Add(this);
            if (RewindManager.Current != null) RewindManager.Current.Register(this, this, 10);
        }

        private void OnDisable()
        {
            activeEntities.Remove(this);
            if (RewindManager.Current != null) RewindManager.Current.Unregister(this);
        }

        public Snapshot CaptureState() => new Snapshot
        {
            Position = Motor.Position, Velocity = Velocity,
            IgnoreOneWayRemaining = Motor.IgnoreOneWayRemaining,
            CollisionsEnabled = Motor.CollisionsEnabled,
            IsReturning = IsReturning, SpringEnabled = SpringEnabled
        };

        public void RestoreState(in Snapshot state)
        {
            Velocity = state.Velocity;
            IsReturning = state.IsReturning;
            SpringEnabled = state.SpringEnabled;
            Motor.RestoreState(state.Position, state.IgnoreOneWayRemaining, state.CollisionsEnabled);
        }

        private void FixedUpdate()
        {
            if (RewindManager.DrivesSimulation || ControlledExternally || IsReturning)
                return;
            Motor.BeginStep(Time.fixedDeltaTime);
            SimulateFree(Time.fixedDeltaTime, 0f);
            Motor.Commit();
        }

        public void SimulateFree(float dt, float horizontalSpeed, MovableEntity ignoredEntity = null)
        {
            if (IsReturning)
                return;

            Motor.ProbeGround(ignoredEntity);
            float conveyor = Motor.ConveyorSpeed;
            Velocity = new Vector2(horizontalSpeed, Velocity.y - Gravity * dt);

            MoveResult horizontal = Motor.Move(Vector2.right * ((Velocity.x + conveyor) * dt), ignoredEntity);
            if (horizontal.Blocked)
                Velocity = new Vector2(0f, Velocity.y);

            MoveResult vertical = Motor.Move(Vector2.up * (Velocity.y * dt), ignoredEntity);
            if (!vertical.Blocked)
                return;

            if (Velocity.y <= 0f && vertical.Normal.y > 0.5f &&
                SpringPad.TryBounce(vertical.Surface, Gravity, out float springSpeed))
            {
                Velocity = new Vector2(Velocity.x, springSpeed);
            }
            else if (Velocity.y <= 0f && vertical.Entity != null
                && HeadContactRules.Evaluate(Part, vertical.Entity.Part, vertical.Normal) == HeadContactAction.BounceEntity
                && vertical.Entity.SpringEnabled && !vertical.Entity.IsReturning)
            {
                Velocity = new Vector2(Velocity.x,
                    Mathf.Sqrt(2f * Gravity * vertical.Entity.SpringHeight));
                vertical.Entity.NotifySpringBounce(this);
            }
            else
            {
                Velocity = new Vector2(Velocity.x, 0f);
            }
        }
    }
}
