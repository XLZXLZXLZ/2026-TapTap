using System;
using UnityEngine;

namespace TapTap
{
    public sealed class CollisionQuery2D
    {
        private RaycastHit2D[] hits = new RaycastHit2D[64];
        private Collider2D[] overlaps = new Collider2D[32];

        public SweepResult Sweep(KinematicMotor2D motor, Vector2 delta,
            MovableEntity ignoredEntity = null, bool preserveGroundWidth = false)
        {
            float distance = delta.magnitude;
            if (distance < 0.000001f || !motor.CollisionsEnabled)
                return SweepResult.Clear;

            Vector2 direction = delta / distance;
            Bounds bounds = motor.CollisionBounds;
            Vector2 center = bounds.center;
            Vector2 size = bounds.size;
            Vector2 castSize = size - Vector2.one * (motor.Skin * 2f);
            if (preserveGroundWidth) castSize.x = size.x;
            castSize.x = Mathf.Max(0.001f, castSize.x);
            castSize.y = Mathf.Max(0.001f, castSize.y);

            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(Physics2D.GetLayerCollisionMask(motor.gameObject.layer));
            filter.useTriggers = false;

            int count;
            while (true)
            {
                count = Physics2D.BoxCast(center, castSize, 0f, direction,
                    filter, hits, distance + motor.Skin * 2f);
                if (count < hits.Length || hits.Length >= 1024)
                    break;
                Array.Resize(ref hits, hits.Length * 2);
            }

            SweepResult nearest = SweepResult.Clear;
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = hits[i];
                Collider2D collider = hit.collider;
                if (collider == null || collider == motor.Collider || collider.isTrigger)
                    continue;
                if (Physics2D.GetIgnoreCollision(motor.Collider, collider))
                    continue;

                MovableEntity entity = collider.GetComponentInParent<MovableEntity>();
                if (entity != null)
                    continue;

                if (preserveGroundWidth && Mathf.Min(bounds.max.x, collider.bounds.max.x)
                    - Mathf.Max(bounds.min.x, collider.bounds.min.x) <= 0.00001f)
                    continue;

                if (Vector2.Dot(direction, hit.normal) >= -0.0001f)
                    continue;

                WorldSurface surface = collider.GetComponentInParent<WorldSurface>();
                if (surface != null && surface.Kind == SurfaceKind.OneWay)
                {
                    float bottom = bounds.min.y;
                    if (motor.IgnoringOneWay || delta.y >= 0f || hit.normal.y < 0.5f
                        || bottom < collider.bounds.max.y - motor.Skin * 2f)
                        continue;
                }

                if (hit.distance <= 0.00001f && IsEscapingOverlap(center, collider.bounds, delta))
                    continue;

                float normalFactor = Mathf.Max(0.05f, -Vector2.Dot(direction, hit.normal));
                float allowedDistance = Mathf.Max(0f, hit.distance - motor.Skin / normalFactor);
                float fraction = Mathf.Clamp01(allowedDistance / distance);
                if (fraction >= nearest.Fraction || fraction >= 0.99999f)
                    continue;

                nearest = new SweepResult
                {
                    Fraction = fraction,
                    Blocked = true,
                    Hit = hit,
                    Normal = hit.normal,
                    Surface = surface
                };
            }

            var entities = MovableEntity.ActiveEntities;
            int layerMask = Physics2D.GetLayerCollisionMask(motor.gameObject.layer);
            for (int i = 0; i < entities.Count; i++)
            {
                MovableEntity entity = entities[i];
                if (entity == null || entity == motor.Entity || entity == ignoredEntity
                    || (motor.JoinedPartner != null && entity == motor.JoinedPartner.Entity)
                    || entity.IsReturning || !entity.isActiveAndEnabled)
                    continue;

                KinematicMotor2D other = entity.Motor;
                if (other == null || !other.CollisionsEnabled || other.Collider.isTrigger
                    || (layerMask & (1 << entity.gameObject.layer)) == 0
                    || Physics2D.GetIgnoreCollision(motor.Collider, other.Collider))
                    continue;

                Bounds otherBounds = other.CollisionBounds;
                Vector2 otherCenter = otherBounds.center;
                Vector2 extents = (size + (Vector2)otherBounds.size) * 0.5f;
                if (!SweepBox(center, otherCenter, extents, delta,
                    out float fraction, out Vector2 normal))
                    continue;

                if (fraction >= nearest.Fraction || fraction >= 0.99999f)
                    continue;

                nearest = new SweepResult
                {
                    Fraction = fraction,
                    Blocked = true,
                    Normal = normal,
                    Entity = entity
                };
            }

            return nearest;
        }

        public bool CanOccupyJoinedShape(KinematicMotor2D motor, KinematicMotor2D partner)
        {
            Bounds bounds = motor.CollisionBounds;
            bounds.Encapsulate(partner.CollisionBounds);
            Vector2 size = (Vector2)bounds.size - Vector2.one * (motor.Skin * 2f);
            size.x = Mathf.Max(0.001f, size.x);
            size.y = Mathf.Max(0.001f, size.y);
            Bounds interiorBounds = new Bounds(bounds.center, size);
            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(Physics2D.GetLayerCollisionMask(motor.gameObject.layer));
            filter.useTriggers = false;
            int count;
            while (true)
            {
                count = Physics2D.OverlapBox(bounds.center, size, 0f, filter, overlaps);
                if (count < overlaps.Length || overlaps.Length >= 1024) break;
                Array.Resize(ref overlaps, overlaps.Length * 2);
            }
            for (int i = 0; i < count; i++)
            {
                Collider2D collider = overlaps[i];
                if (collider == null || collider.GetComponentInParent<MovableEntity>() != null
                    || Physics2D.GetIgnoreCollision(motor.Collider, collider)
                    || !HasAreaOverlap(interiorBounds, collider.bounds)) continue;
                WorldSurface surface = collider.GetComponentInParent<WorldSurface>();
                if (surface == null || surface.Kind != SurfaceKind.OneWay) return false;
            }
            int layerMask = Physics2D.GetLayerCollisionMask(motor.gameObject.layer);
            foreach (MovableEntity entity in MovableEntity.ActiveEntities)
            {
                if (entity == null || entity == motor.Entity || entity == partner.Entity
                    || entity.IsReturning || !entity.isActiveAndEnabled) continue;
                KinematicMotor2D other = entity.Motor;
                if (!other.CollisionsEnabled || other.Collider.isTrigger
                    || (layerMask & (1 << entity.gameObject.layer)) == 0
                    || Physics2D.GetIgnoreCollision(motor.Collider, other.Collider)) continue;
                Bounds otherBounds = other.CollisionBounds;
                Vector2 offset = (Vector2)bounds.center - (Vector2)otherBounds.center;
                Vector2 extents = (size + (Vector2)otherBounds.size) * 0.5f;
                if (Mathf.Abs(offset.x) < extents.x && Mathf.Abs(offset.y) < extents.y)
                    return false;
            }
            return true;
        }

        private static bool HasAreaOverlap(Bounds a, Bounds b)
        {
            return Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x) > 0.00001f
                && Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y) > 0.00001f;
        }

        private static bool IsEscapingOverlap(Vector2 center, Bounds bounds, Vector2 delta)
        {
            Vector2 relative = center - (Vector2)bounds.center;
            return Vector2.Dot(relative, delta) > 0f;
        }

        private static bool SweepBox(Vector2 origin, Vector2 target, Vector2 extents,
            Vector2 delta, out float fraction, out Vector2 normal)
        {
            Vector2 relative = origin - target;
            float entry = float.NegativeInfinity;
            float exit = float.PositiveInfinity;
            normal = Vector2.zero;
            fraction = 1f;

            for (int axis = 0; axis < 2; axis++)
            {
                float velocity = delta[axis];
                float position = relative[axis];
                float extent = extents[axis];
                if (Mathf.Abs(velocity) < 0.000001f)
                {
                    // Touching an edge while sliding past it does not block motion.
                    if (position <= -extent + 0.00001f || position >= extent - 0.00001f)
                        return false;
                    continue;
                }

                float near = (-extent - position) / velocity;
                float far = (extent - position) / velocity;
                Vector2 axisNormal = axis == 0 ? Vector2.left : Vector2.down;
                if (near > far)
                {
                    float swap = near;
                    near = far;
                    far = swap;
                    axisNormal = -axisNormal;
                }

                if (near > entry)
                {
                    entry = near;
                    normal = axisNormal;
                }
                exit = Mathf.Min(exit, far);
                if (entry > exit)
                    return false;
            }

            if (exit < 0f || entry > 1f)
                return false;

            if (entry < -0.00001f)
            {
                if (Vector2.Dot(relative, delta) >= 0f)
                    return false;
                normal = Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
                    ? new Vector2(-Mathf.Sign(delta.x), 0f)
                    : new Vector2(0f, -Mathf.Sign(delta.y));
            }

            fraction = Mathf.Clamp01(entry);
            return true;
        }
    }
}
