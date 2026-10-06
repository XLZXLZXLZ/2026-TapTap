using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace TapTap
{
    public sealed class RespawnService : MonoBehaviour
    {
        private sealed class ReturnTrip
        {
            public MovableEntity Entity;
            public Vector2 From;
            public Vector2 Destination;
            public float Elapsed;
            public bool Grouped;
            public MovableEntity GroupBody;
            public MovableEntity GroupHead;
            public float GroupOffset;
        }

        [SerializeField] private PlayerConfig config;
        [SerializeField] private Vector2 checkpoint;
        [SerializeField] private bool hasCheckpoint;
        private readonly List<ReturnTrip> trips = new List<ReturnTrip>();
        private readonly List<ReturnTrip> completed = new List<ReturnTrip>();
        public Vector2 Checkpoint => checkpoint;
        public bool HasCheckpoint => hasCheckpoint;
        public event Action<MovableEntity> Returned;

        public void Configure(PlayerConfig settings) => config = settings;
        public void SetCheckpoint(Vector2 position)
        {
            checkpoint = position;
            hasCheckpoint = true;
        }

        public void BeginGroupReturn(MovableEntity body, MovableEntity head, float offset)
        {
            Begin(body, checkpoint, true);
            Begin(head, checkpoint + Vector2.up * offset, true);
            foreach (ReturnTrip trip in trips)
            {
                if (trip.Entity != body && trip.Entity != head) continue;
                trip.GroupBody = body;
                trip.GroupHead = head;
                trip.GroupOffset = offset;
            }
        }

        public void BeginSingleReturn(MovableEntity entity) => Begin(entity, checkpoint, false);

        private void Begin(MovableEntity entity, Vector2 destination, bool grouped)
        {
            if (entity == null || entity.IsReturning) return;
            entity.IsReturning = true;
            entity.Velocity = Vector2.zero;
            entity.Motor.SetCollisionsEnabled(false);
            trips.Add(new ReturnTrip
            {
                Entity = entity, From = entity.Motor.Position,
                Destination = destination, Grouped = grouped
            });
        }

        public void Simulate(float dt)
        {
            completed.Clear();
            for (int i = trips.Count - 1; i >= 0; i--)
            {
                ReturnTrip trip = trips[i];
                if (trip.Entity == null) { trips.RemoveAt(i); continue; }
                trip.Elapsed += dt;
                float t = Mathf.Clamp01(trip.Elapsed / (config != null ? config.RespawnDuration : 0.45f));
                Vector2 position = Vector2.Lerp(trip.From, trip.Destination,
                    DOVirtual.EasedValue(0f, 1f, t, Ease.InOutSine));
                position.y += Mathf.Sin(t * Mathf.PI) * 0.7f;
                trip.Entity.Motor.Teleport(position);
                if (t < 1f) continue;

                completed.Add(trip);
            }
            foreach (ReturnTrip trip in completed)
            {
                if (!trips.Contains(trip)) continue;
                if (trip.Grouped)
                {
                    Vector2 bodyDestination = FindLandingPosition(trip.GroupBody, checkpoint, trip.GroupHead);
                    Complete(trip.GroupBody, bodyDestination);
                    Complete(trip.GroupHead, bodyDestination + Vector2.up * trip.GroupOffset);
                    trips.RemoveAll(t => t.GroupBody == trip.GroupBody);
                }
                else
                {
                    Complete(trip.Entity, FindLandingPosition(trip.Entity, trip.Destination));
                    trips.Remove(trip);
                }
            }
        }

        private void Complete(MovableEntity entity, Vector2 destination)
        {
            entity.Motor.Teleport(destination);
            entity.Velocity = Vector2.zero;
            entity.IsReturning = false;
            entity.Motor.SetCollisionsEnabled(true);
            Returned?.Invoke(entity);
        }

        private Vector2 FindLandingPosition(MovableEntity entity, Vector2 desired, MovableEntity ignored = null)
        {
            float skin = config != null ? config.Skin : 0.01f;
            foreach (MovableEntity other in MovableEntity.ActiveEntities)
            {
                if (other == null || other == entity || other == ignored || other.IsReturning) continue;
                Vector2 halfSize = (entity.Motor.Size + other.Motor.Size) * 0.5f;
                Vector2 distance = desired - other.Motor.Position;
                if (Mathf.Abs(distance.x) >= halfSize.x || Mathf.Abs(distance.y) >= halfSize.y) continue;
                if ((entity.Part == EntityPart.Body && other.Part == EntityPart.Head)
                    || (entity.Part == EntityPart.Head && other.Part == EntityPart.Body))
                {
                    desired.y = Mathf.Max(desired.y, other.Motor.Position.y
                        + (entity.Motor.Size.y + other.Motor.Size.y) * 0.5f + skin * 2f);
                }
            }
            return desired;
        }

        public void CancelAll()
        {
            foreach (ReturnTrip trip in trips)
            {
                if (trip.Entity == null) continue;
                trip.Entity.IsReturning = false;
                trip.Entity.Motor.SetCollisionsEnabled(true);
            }
            trips.Clear();
        }
    }
}
