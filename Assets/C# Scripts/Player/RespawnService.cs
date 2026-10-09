using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace TapTap
{
    public sealed class RespawnService : MonoBehaviour
    {
        public struct JourneySnapshot
        {
            public long Id;
            public MovableEntity Primary, Companion;
            public Vector2 Origin, PrimaryOrigin, Destination;
            public float GroupOffset, Elapsed;
            public float ShakeDuration, CollapseDuration, DotDuration, FlightDuration, RebuildDuration;
            public bool Arrived;
        }

        public struct Snapshot
        {
            public Vector2 Checkpoint;
            public bool HasCheckpoint;
            public int Count;
            public JourneySnapshot First, Second;
        }

        public sealed class Journey
        {
            public long Id { get; internal set; }
            public MovableEntity Primary { get; internal set; }
            public MovableEntity Companion { get; internal set; }
            public Vector2 Origin { get; internal set; }
            public Vector2 PrimaryOrigin { get; internal set; }
            public Vector2 Destination { get; internal set; }
            public float GroupOffset { get; internal set; }
            public float Elapsed { get; internal set; }
            public float ShakeDuration { get; internal set; }
            public float CollapseDuration { get; internal set; }
            public float DotDuration { get; internal set; }
            public float FlightDuration { get; internal set; }
            public float RebuildDuration { get; internal set; }
            public float FlightStart => ShakeDuration + CollapseDuration + DotDuration;
            public float ArrivalTime => FlightStart + FlightDuration;
            public float TotalDuration => ArrivalTime + DotDuration + RebuildDuration;
            internal bool Arrived;
        }

        [SerializeField] private PlayerConfig config;
        [SerializeField] private Vector2 checkpoint;
        [SerializeField] private bool hasCheckpoint;
        private readonly List<Journey> journeys = new List<Journey>();
        private long nextJourneyId;
        public IReadOnlyList<Journey> Journeys => journeys;
        public Vector2 Checkpoint => checkpoint;
        public bool HasCheckpoint => hasCheckpoint;
        public event Action<MovableEntity> Returned;
        public event Action<Journey> ReturnStarted;
        public event Action<Journey> ReturnEnded;

        public void Configure(PlayerConfig settings) => config = settings;
        public void SetCheckpoint(Vector2 position) { checkpoint = position; hasCheckpoint = true; }
        public void BeginGroupReturn(MovableEntity body, MovableEntity head, float offset) => Begin(body, head, offset);
        public void BeginSingleReturn(MovableEntity entity) => Begin(entity, null, 0f);

        private void Begin(MovableEntity entity, MovableEntity companion, float offset)
        {
            if (entity == null || entity.IsReturning || (companion != null && companion.IsReturning)) return;
            var journey = new Journey
            {
                Id = ++nextJourneyId,
                Primary = entity, Companion = companion, GroupOffset = offset,
                Origin = companion != null ? (entity.Motor.Position + companion.Motor.Position) * 0.5f : entity.Motor.Position,
                PrimaryOrigin = entity.Motor.Position,
                Destination = checkpoint,
                ShakeDuration = config != null ? Mathf.Max(0.01f, config.DeathShakeDuration) : 0.08f,
                CollapseDuration = config != null ? Mathf.Max(0.01f, config.RespawnCollapseDuration) : 0.18f,
                DotDuration = config != null ? Mathf.Max(0.01f, config.RespawnDotDuration) : 0.1f,
                FlightDuration = config != null ? Mathf.Max(0.02f, config.RespawnDuration) : 0.45f,
                RebuildDuration = config != null ? Mathf.Max(0.01f, config.RespawnRebuildDuration) : 0.2f
            };
            Suspend(entity);
            if (companion != null) Suspend(companion);
            journeys.Add(journey);
            ReturnStarted?.Invoke(journey);
        }

        private static void Suspend(MovableEntity entity)
        {
            entity.IsReturning = true;
            entity.Velocity = Vector2.zero;
            entity.Motor.SetCollisionsEnabled(false);
        }

        public Vector2 FlightPoint(Journey journey, float easedProgress)
        {
            Vector2 destination = journey.Destination;
            if (journey.Companion != null) destination.y += journey.GroupOffset * 0.5f;
            Vector2 position = Vector2.Lerp(journey.Origin, destination, easedProgress);
            position.y += Mathf.Sin(easedProgress * Mathf.PI) * (config != null ? config.ToWorld(0.7f) : 0.7f);
            return position;
        }

        public static float RenderTime(Journey journey) => Mathf.Min(journey.TotalDuration,
            journey.Elapsed + (RewindManager.Rewinding ? 0f : Mathf.Clamp(Time.time - Time.fixedTime, 0f, Time.fixedDeltaTime)));

        public Snapshot CaptureState()
        {
            if (journeys.Count > 2)
                throw new InvalidOperationException("Player rewind supports at most two simultaneous return journeys.");
            return new Snapshot
            {
                Checkpoint = checkpoint, HasCheckpoint = hasCheckpoint, Count = journeys.Count,
                First = journeys.Count > 0 ? CaptureJourney(journeys[0]) : default,
                Second = journeys.Count > 1 ? CaptureJourney(journeys[1]) : default
            };
        }

        private static JourneySnapshot CaptureJourney(Journey journey) => new JourneySnapshot
        {
            Id = journey.Id, Primary = journey.Primary, Companion = journey.Companion,
            Origin = journey.Origin, PrimaryOrigin = journey.PrimaryOrigin, Destination = journey.Destination,
            GroupOffset = journey.GroupOffset, Elapsed = journey.Elapsed, Arrived = journey.Arrived,
            ShakeDuration = journey.ShakeDuration, CollapseDuration = journey.CollapseDuration,
            DotDuration = journey.DotDuration, FlightDuration = journey.FlightDuration,
            RebuildDuration = journey.RebuildDuration
        };

        public void RestoreState(in Snapshot state)
        {
            checkpoint = state.Checkpoint;
            hasCheckpoint = state.HasCheckpoint;
            bool sameTasks = journeys.Count == state.Count
                && (state.Count < 1 || journeys[0].Id == state.First.Id)
                && (state.Count < 2 || journeys[1].Id == state.Second.Id);
            if (!sameTasks)
            {
                journeys.Clear();
                for (int i = 0; i < state.Count; i++) journeys.Add(new Journey());
            }
            if (state.Count > 0) RestoreJourney(journeys[0], in state.First);
            if (state.Count > 1) RestoreJourney(journeys[1], in state.Second);
        }

        private static void RestoreJourney(Journey journey, in JourneySnapshot state)
        {
            journey.Id = state.Id;
            journey.Primary = state.Primary;
            journey.Companion = state.Companion;
            journey.Origin = state.Origin;
            journey.PrimaryOrigin = state.PrimaryOrigin;
            journey.Destination = state.Destination;
            journey.GroupOffset = state.GroupOffset;
            journey.Elapsed = state.Elapsed;
            journey.Arrived = state.Arrived;
            journey.ShakeDuration = state.ShakeDuration;
            journey.CollapseDuration = state.CollapseDuration;
            journey.DotDuration = state.DotDuration;
            journey.FlightDuration = state.FlightDuration;
            journey.RebuildDuration = state.RebuildDuration;
        }

        public Vector2 FollowPosition(MovableEntity entity)
        {
            foreach (Journey journey in journeys)
            {
                if (journey.Primary != entity) continue;
                float t = RenderTime(journey);
                if (t < journey.FlightStart)
                    return Vector2.Lerp(journey.PrimaryOrigin, journey.Origin, t / journey.FlightStart);
                if (t >= journey.ArrivalTime)
                    return Vector2.Lerp(FlightPoint(journey, 1f), journey.Destination,
                        (t - journey.ArrivalTime) / Mathf.Max(0.01f, journey.DotDuration + journey.RebuildDuration));
                float flight = (t - journey.FlightStart) / journey.FlightDuration;
                return FlightPoint(journey, DOVirtual.EasedValue(0f, 1f, flight, Ease.InOutSine));
            }
            return entity.transform.position;
        }

        public void Simulate(float dt)
        {
            for (int i = journeys.Count - 1; i >= 0; i--)
            {
                Journey journey = journeys[i];
                if (journey.Primary == null || (journey.Companion == null && journey.GroupOffset > 0f))
                {
                    Release(journey.Primary); Release(journey.Companion);
                    ReturnEnded?.Invoke(journey);
                    journeys.RemoveAt(i);
                    continue;
                }
                journey.Elapsed += Mathf.Max(0f, dt);
                if (!journey.Arrived && journey.Elapsed >= journey.ArrivalTime)
                {
                    journey.Destination = FindLandingPosition(journey.Primary, journey.Destination, journey.Companion);
                    journey.Primary.Motor.Teleport(journey.Destination);
                    if (journey.Companion != null)
                        journey.Companion.Motor.Teleport(journey.Destination + Vector2.up * journey.GroupOffset);
                    journey.Arrived = true;
                }
                if (journey.Elapsed < journey.TotalDuration) continue;
                ReturnEnded?.Invoke(journey);
                Complete(journey.Primary);
                if (journey.Companion != null) Complete(journey.Companion);
                journeys.RemoveAt(i);
            }
        }

        private static void Release(MovableEntity entity)
        {
            if (entity == null) return;
            entity.Velocity = Vector2.zero;
            entity.IsReturning = false;
            entity.Motor.SetCollisionsEnabled(true);
        }

        private void Complete(MovableEntity entity)
        {
            Release(entity);
            Returned?.Invoke(entity);
        }

        private Vector2 FindLandingPosition(MovableEntity entity, Vector2 desired, MovableEntity ignored = null)
        {
            float skin = config != null ? config.Skin : 0.01f;
            foreach (MovableEntity other in MovableEntity.ActiveEntities)
            {
                if (other == null || other == entity || other == ignored) continue;
                if (other.IsReturning && !journeys.Exists(journey => journey.Arrived &&
                    (journey.Primary == other || journey.Companion == other))) continue;
                Vector2 halfSize = (entity.Motor.Size + other.Motor.Size) * 0.5f;
                Vector2 distance = desired - other.Motor.Position;
                if (Mathf.Abs(distance.x) >= halfSize.x || Mathf.Abs(distance.y) >= halfSize.y) continue;
                if ((entity.Part == EntityPart.Body && other.Part == EntityPart.Head)
                    || (entity.Part == EntityPart.Head && other.Part == EntityPart.Body))
                    desired.y = Mathf.Max(desired.y, other.Motor.Position.y
                        + (entity.Motor.Size.y + other.Motor.Size.y) * 0.5f + skin * 2f);
            }
            for (int pass = 0; pass < 8; pass++)
            {
                Bounds occupied = new Bounds(desired + entity.Motor.CenterOffset, entity.Motor.Size);
                if (ignored != null)
                    occupied.Encapsulate(new Bounds(desired + Vector2.up * config.ToWorld(config.JoinedOffset) + ignored.Motor.CenterOffset, ignored.Motor.Size));
                float rise = 0f;
                foreach (Collider2D collider in Physics2D.OverlapBoxAll(occupied.center, (Vector2)occupied.size * 0.99f, 0f))
                {
                    if (collider.isTrigger || collider.GetComponentInParent<MovableEntity>() != null) continue;
                    var surface = collider.GetComponentInParent<WorldSurface>();
                    if (surface != null && surface.Kind == SurfaceKind.OneWay) continue;
                    rise = Mathf.Max(rise, collider.bounds.max.y - occupied.min.y + skin * 2f);
                }
                if (rise <= 0f) break;
                desired.y += rise;
            }
            return desired;
        }

        public void CancelAll()
        {
            foreach (Journey journey in journeys)
            {
                ReturnEnded?.Invoke(journey);
                Release(journey.Primary); Release(journey.Companion);
            }
            journeys.Clear();
        }

        private void OnDisable() => CancelAll();
    }
}
