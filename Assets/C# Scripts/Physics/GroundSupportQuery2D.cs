using System;
using UnityEngine;

namespace TapTap
{
    public sealed class GroundSupportQuery2D
    {
        private const float OverlapEpsilon = 0.00001f;
        private readonly CollisionQuery2D collisionQuery = new CollisionQuery2D();
        private RaycastHit2D[] hits = new RaycastHit2D[32];

        public SweepResult Probe(KinematicMotor2D motor, MovableEntity ignoredEntity,
            out float conveyorSpeed)
        {
            conveyorSpeed = 0f;
            float distance = motor.Skin * 3f + 0.01f;
            Vector2 delta = Vector2.down * distance;
            SweepResult nearest = collisionQuery.Sweep(motor, delta, ignoredEntity, true);

            if (!nearest.Blocked || nearest.Normal.y <= 0.5f
                || nearest.Entity != null || nearest.Hit.collider == null)
                return nearest;

            Bounds bounds = motor.CollisionBounds;
            Vector2 center = bounds.center;
            Vector2 castSize = new Vector2(
                Mathf.Max(0.001f, bounds.size.x),
                Mathf.Max(0.001f, bounds.size.y - motor.Skin * 2f));
            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(Physics2D.GetLayerCollisionMask(motor.gameObject.layer));
            filter.useTriggers = false;

            int count;
            while (true)
            {
                count = Physics2D.BoxCast(center, castSize, 0f, Vector2.down,
                    filter, hits, distance + motor.Skin * 2f);
                if (count < hits.Length || hits.Length >= 1024)
                    break;
                Array.Resize(ref hits, hits.Length * 2);
            }

            float largestOverlap = 0f;
            int selectedColliderId = int.MaxValue;
            float heightTolerance = motor.Skin + OverlapEpsilon;
            KinematicMotor2D partner = motor.JoinedPartner;

            for (int index = 0; index < count; index++)
            {
                RaycastHit2D hit = hits[index];
                Collider2D collider = hit.collider;
                if (collider == null || collider == motor.Collider || collider.isTrigger
                    || (partner != null && collider == partner.Collider)
                    || Physics2D.GetIgnoreCollision(motor.Collider, collider))
                    continue;

                if (collider.GetComponentInParent<MovableEntity>() != null || hit.normal.y <= 0.5f)
                    continue;

                WorldSurface surface = collider.GetComponentInParent<WorldSurface>();
                if (surface == null || surface.ConveyorSpeed == 0f)
                    continue;

                Bounds surfaceBounds = collider.bounds;
                if (surface.Kind == SurfaceKind.OneWay
                    && (motor.IgnoringOneWay
                        || bounds.min.y < surfaceBounds.max.y - motor.Skin * 2f))
                    continue;

                if (hit.distance <= OverlapEpsilon
                    && Vector2.Dot(center - (Vector2)surfaceBounds.center, delta) > 0f)
                    continue;

                if (Mathf.Abs(hit.distance - nearest.Hit.distance) > heightTolerance)
                    continue;

                float overlap = Mathf.Min(bounds.max.x, surfaceBounds.max.x)
                    - Mathf.Max(bounds.min.x, surfaceBounds.min.x);
                if (overlap <= OverlapEpsilon)
                    continue;

                int colliderId = collider.GetInstanceID();
                bool widerContact = overlap > largestOverlap + OverlapEpsilon;
                bool stableTie = Mathf.Abs(overlap - largestOverlap) <= OverlapEpsilon
                    && colliderId < selectedColliderId;
                if (!widerContact && !stableTie)
                    continue;

                largestOverlap = overlap;
                selectedColliderId = colliderId;
                conveyorSpeed = surface.ConveyorSpeed;
            }

            return nearest;
        }
    }
}
