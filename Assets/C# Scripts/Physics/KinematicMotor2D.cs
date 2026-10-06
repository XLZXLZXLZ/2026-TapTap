using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class KinematicMotor2D : MonoBehaviour
    {
        [SerializeField, Min(0.001f)] private float skin = 0.008f;
        private Rigidbody2D physicsBody;
        private BoxCollider2D box;
        private MovableEntity entity;
        private CollisionQuery2D query;
        private GroundSupportQuery2D groundQuery;
        private bool includeJoinedPartner;
        private Vector2 plannedPosition;
        private float ignoreOneWayRemaining;
        private readonly List<MotionSegment> motionSegments = new List<MotionSegment>(8);
        private readonly List<MoveResult> contacts = new List<MoveResult>(8);

        public Vector2 Position { get { Initialize(); return plannedPosition; } }
        public Vector2 Size
        {
            get
            {
                Initialize();
                Vector3 scale = transform.lossyScale;
                return new Vector2(box.size.x * Mathf.Abs(scale.x), box.size.y * Mathf.Abs(scale.y));
            }
        }
        public Vector2 CenterOffset { get { Initialize(); return transform.TransformVector(box.offset); } }
        public KinematicMotor2D JoinedPartner { get; private set; }
        public Bounds CollisionBounds
        {
            get
            {
                Bounds bounds = PartBounds;
                if (includeJoinedPartner && JoinedPartner != null)
                    bounds.Encapsulate(JoinedPartner.PartBounds);
                return bounds;
            }
        }
        private Bounds PartBounds => new Bounds(Position + CenterOffset, Size);
        public float Skin => skin;
        public BoxCollider2D Collider { get { Initialize(); return box; } }
        public MovableEntity Entity
        {
            get
            {
                Initialize();
                if (entity == null)
                    entity = GetComponent<MovableEntity>();
                return entity;
            }
        }
        public bool CollisionsEnabled { get { Initialize(); return box.enabled && physicsBody.simulated; } }
        public bool IgnoringOneWay => ignoreOneWayRemaining > 0f;
        public bool Grounded { get; private set; }
        public WorldSurface SupportSurface { get; private set; }
        public MovableEntity SupportEntity { get; private set; }
        public float ConveyorSpeed { get; private set; }
        public MoveResult LastMoveResult { get; private set; }
        public IReadOnlyList<MotionSegment> MotionSegments => motionSegments;
        public IReadOnlyList<MoveResult> Contacts => contacts;

        public void Initialize()
        {
            if (physicsBody != null)
                return;
            physicsBody = GetComponent<Rigidbody2D>();
            box = GetComponent<BoxCollider2D>();
            entity = GetComponent<MovableEntity>();
            query = new CollisionQuery2D();
            groundQuery = new GroundSupportQuery2D();
            physicsBody.bodyType = RigidbodyType2D.Kinematic;
            physicsBody.gravityScale = 0f;
            physicsBody.constraints = RigidbodyConstraints2D.FreezeRotation;
            physicsBody.useFullKinematicContacts = true;
            physicsBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            plannedPosition = physicsBody.position;
        }

        private void Awake() => Initialize();

        public void BeginStep(float dt = -1f)
        {
            Initialize();
            float simulationDt = dt < 0f ? Time.fixedDeltaTime : dt;
            ignoreOneWayRemaining = Mathf.Max(0f, ignoreOneWayRemaining - Mathf.Max(0f, simulationDt));
            plannedPosition = physicsBody.position;
            motionSegments.Clear();
            contacts.Clear();
            LastMoveResult = default;
            ProbeGround();
        }

        public SweepResult Sweep(Vector2 delta, MovableEntity ignoredEntity = null)
        {
            Initialize();
            return query.Sweep(this, delta, ignoredEntity);
        }

        public MoveResult Move(Vector2 delta, MovableEntity ignoredEntity = null)
        {
            SweepResult sweep = Sweep(delta, ignoredEntity);
            Vector2 actualDelta = delta * sweep.Fraction;
            StageTranslation(actualDelta);
            LastMoveResult = new MoveResult(actualDelta, sweep);
            if (sweep.Blocked)
                contacts.Add(LastMoveResult);
            ProbeGround(ignoredEntity);
            return LastMoveResult;
        }

        public void StageTranslation(Vector2 validatedDelta)
        {
            Initialize();
            Vector2 from = plannedPosition;
            plannedPosition += validatedDelta;
            if (validatedDelta.sqrMagnitude > 0.0000000001f)
                motionSegments.Add(new MotionSegment(from, plannedPosition));
        }

        public void Commit()
        {
            Initialize();
            if (physicsBody.simulated)
                physicsBody.MovePosition(plannedPosition);
        }

        public void Teleport(Vector2 position)
        {
            Initialize();
            physicsBody.position = position;
            physicsBody.velocity = Vector2.zero;
            plannedPosition = position;
            ignoreOneWayRemaining = 0f;
            motionSegments.Clear();
            contacts.Clear();
            LastMoveResult = default;
            Grounded = false;
            SupportSurface = null;
            SupportEntity = null;
            ConveyorSpeed = 0f;
        }

        public void IgnoreOneWayFor(float seconds)
        {
            ignoreOneWayRemaining = Mathf.Max(ignoreOneWayRemaining, Mathf.Max(0f, seconds));
            Grounded = false;
            SupportSurface = null;
            SupportEntity = null;
            ConveyorSpeed = 0f;
        }

        public void EndDrop() => ignoreOneWayRemaining = 0f;

        public void SetSkin(float value) => skin = Mathf.Max(0.001f, value);

        public void SetJoinedPartner(KinematicMotor2D partner, bool includeInBounds)
        {
            JoinedPartner = partner;
            includeJoinedPartner = includeInBounds;
        }

        public bool CanJoinWith(KinematicMotor2D partner)
        {
            Initialize();
            return query.CanOccupyJoinedShape(this, partner);
        }

        public void SetCollisionsEnabled(bool enabled)
        {
            Initialize();
            box.enabled = enabled;
            physicsBody.velocity = Vector2.zero;
            Grounded = false;
            SupportSurface = null;
            SupportEntity = null;
            ConveyorSpeed = 0f;
        }

        public void ProbeGround(MovableEntity ignoredEntity = null)
        {
            Initialize();
            SweepResult support = groundQuery.Probe(this, ignoredEntity, out float conveyorSpeed);
            Grounded = support.Blocked && support.Normal.y > 0.5f;
            SupportSurface = Grounded ? support.Surface : null;
            SupportEntity = Grounded ? support.Entity : null;
            ConveyorSpeed = Grounded ? conveyorSpeed : 0f;
        }
    }
}
