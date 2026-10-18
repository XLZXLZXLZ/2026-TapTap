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
        private Collider2D ignoredOneWayPlatform;
        private readonly List<Collider2D> ignoredOneWayTiles = new List<Collider2D>();
        private Bounds ignoredOneWayBounds;
        private RigidbodyInterpolation2D normalInterpolation;
        private bool rewinding;
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
        internal Bounds PartBounds => new Bounds(Position + CenterOffset, Size);
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
        public float IgnoreOneWayRemaining => ignoreOneWayRemaining;
        public Collider2D IgnoredOneWayPlatform => ignoredOneWayPlatform;
        public bool Grounded { get; private set; }
        public WorldSurface SupportSurface { get; private set; }
        public Collider2D SupportCollider { get; private set; }
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

        public void BeginStep(float dt = -1f, bool probeGround = true)
        {
            Initialize();
            float simulationDt = dt < 0f ? Time.fixedDeltaTime : dt;
            ignoreOneWayRemaining = Mathf.Max(0f, ignoreOneWayRemaining - Mathf.Max(0f, simulationDt));
            UpdateIgnoredPlatform();
            // Logical position is authoritative; MovePosition only mirrors it to Unity.
            motionSegments.Clear();
            contacts.Clear();
            LastMoveResult = default;
            if (probeGround) ProbeGround();
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
            UpdateIgnoredPlatform();
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
            ignoredOneWayPlatform = null;
            ignoredOneWayTiles.Clear();
            motionSegments.Clear();
            contacts.Clear();
            LastMoveResult = default;
            Grounded = false;
            SupportSurface = null;
            SupportCollider = null;
            SupportEntity = null;
            ConveyorSpeed = 0f;
        }

        public void IgnoreOneWayFor(float seconds)
        {
            ignoreOneWayRemaining = Mathf.Max(ignoreOneWayRemaining, Mathf.Max(0f, seconds));
            Grounded = false;
            SupportSurface = null;
            SupportCollider = null;
            SupportEntity = null;
            ConveyorSpeed = 0f;
        }

        public void IgnoreOneWayPlatform(Collider2D platform)
        {
            WorldSurface surface = platform != null ? platform.GetComponentInParent<WorldSurface>() : null;
            if (surface == null || surface.Kind != SurfaceKind.OneWay) return;
            ignoredOneWayPlatform = platform;
            CollectIgnoredOneWayTiles();
            Grounded = false;
            SupportSurface = null;
            SupportCollider = null;
            SupportEntity = null;
            ConveyorSpeed = 0f;
        }

        public bool IsIgnoringOneWay(Collider2D platform) =>
            IgnoringOneWay || (ignoredOneWayPlatform != null && ignoredOneWayTiles.Contains(platform));

        private void CollectIgnoredOneWayTiles()
        {
            ignoredOneWayTiles.Clear();
            if (ignoredOneWayPlatform == null) return;
            ignoredOneWayTiles.Add(ignoredOneWayPlatform);
            ignoredOneWayBounds = ignoredOneWayPlatform.bounds;
            float top = ignoredOneWayBounds.max.y;
            // Grid tiles on one continuous ledge are one drop target. Other floors remain solid.
            const float tolerance = 0.001f;
            bool expanded;
            do
            {
                expanded = false;
                Vector2 size = ignoredOneWayBounds.size;
                size += Vector2.one * (tolerance * 2f);
                foreach (Collider2D tile in Physics2D.OverlapBoxAll(ignoredOneWayBounds.center, size, 0f))
                {
                    if (tile.isTrigger || ignoredOneWayTiles.Contains(tile)) continue;
                    WorldSurface surface = tile.GetComponentInParent<WorldSurface>();
                    Bounds bounds = tile.bounds;
                    if (surface == null || surface.Kind != SurfaceKind.OneWay
                        || Mathf.Abs(bounds.max.y - top) > tolerance
                        || bounds.min.x > ignoredOneWayBounds.max.x + tolerance
                        || bounds.max.x < ignoredOneWayBounds.min.x - tolerance
                        || (Physics2D.GetLayerCollisionMask(gameObject.layer) & (1 << tile.gameObject.layer)) == 0
                        || Physics2D.GetIgnoreCollision(Collider, tile)) continue;
                    ignoredOneWayTiles.Add(tile);
                    ignoredOneWayBounds.Encapsulate(bounds);
                    expanded = true;
                }
            } while (expanded);
        }

        private void UpdateIgnoredPlatform()
        {
            if (ignoredOneWayPlatform == null) return;
            Bounds actor = CollisionBounds;
            if (JoinedPartner != null) actor.Encapsulate(JoinedPartner.PartBounds);
            Bounds platform = ignoredOneWayBounds;
            // Keep the target until the entire joined shape clears it, even on thick platforms.
            if (!ignoredOneWayPlatform.enabled || !ignoredOneWayPlatform.gameObject.activeInHierarchy
                || actor.max.y < platform.min.y - skin
                || actor.min.y > platform.max.y + skin * 2f
                || actor.max.x < platform.min.x - skin || actor.min.x > platform.max.x + skin)
            {
                ignoredOneWayPlatform = null;
                ignoredOneWayTiles.Clear();
            }
        }

        public void EndDrop()
        {
            ignoreOneWayRemaining = 0f;
            ignoredOneWayPlatform = null;
            ignoredOneWayTiles.Clear();
        }

        public void RestoreState(Vector2 position, float ignoreOneWay, bool collisionsEnabled,
            Collider2D ignoredPlatform = null)
        {
            Teleport(position);
            ignoreOneWayRemaining = ignoreOneWay;
            ignoredOneWayPlatform = ignoredPlatform;
            CollectIgnoredOneWayTiles();
            SetCollisionsEnabled(collisionsEnabled);
            // Assign the transform too, so an interpolated future pose cannot leak into rewind.
            Vector3 pose = transform.position;
            transform.position = new Vector3(position.x, position.y, pose.z);
        }

        public void SetRewinding(bool value)
        {
            Initialize();
            if (rewinding == value) return;
            rewinding = value;
            if (value)
            {
                normalInterpolation = physicsBody.interpolation;
                physicsBody.interpolation = RigidbodyInterpolation2D.None;
                physicsBody.position = plannedPosition;
                physicsBody.velocity = Vector2.zero;
            }
            else physicsBody.interpolation = normalInterpolation;
        }

        public void SetSkin(float value) => skin = Mathf.Max(0.001f, value);

        public void SetJoinedPartner(KinematicMotor2D partner, bool includeInBounds)
        {
            JoinedPartner = partner;
            includeJoinedPartner = includeInBounds;
        }

        public bool CanJoinWith(KinematicMotor2D partner, Vector2? partnerPosition = null)
        {
            Initialize();
            return query.CanOccupyJoinedShape(this, partner, partnerPosition);
        }

        public void SetCollisionsEnabled(bool enabled)
        {
            Initialize();
            box.enabled = enabled;
            physicsBody.velocity = Vector2.zero;
            Grounded = false;
            SupportSurface = null;
            SupportCollider = null;
            SupportEntity = null;
            ConveyorSpeed = 0f;
        }

        // Read-only support check for one half, independent of the joined collision shape.
        public bool HasPartGroundSupport(MovableEntity ignoredEntity = null)
        {
            Initialize();
            SweepResult support = query.Sweep(this, Vector2.down * (skin * 3f + 0.01f),
                ignoredEntity, true, true);
            return support.Blocked && support.Normal.y > 0.5f;
        }

        public void ProbeGround(MovableEntity ignoredEntity = null)
        {
            Initialize();
            SweepResult support = groundQuery.Probe(this, ignoredEntity, out float conveyorSpeed);
            Grounded = support.Blocked && support.Normal.y > 0.5f;
            SupportSurface = Grounded ? support.Surface : null;
            SupportCollider = Grounded ? support.Hit.collider : null;
            SupportEntity = Grounded ? support.Entity : null;
            ConveyorSpeed = Grounded ? conveyorSpeed : 0f;
        }
    }
}
