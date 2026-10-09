using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    [DefaultExecutionOrder(-200)]
    public sealed class WorldPhaseState : LazySingleton<WorldPhaseState>, IRewindable<bool>
    {
        [SerializeField] private bool active;
        private readonly List<PhaseBlock> blocks = new List<PhaseBlock>();
        private readonly HashSet<MovableEntity> resolved = new HashSet<MovableEntity>();
        private PhaseBlockOutline outline;
        public bool Active => active;
        public IReadOnlyList<PhaseBlock> Blocks => blocks;
        public event Action<bool> Changed;

        private void Awake()
        {
            outline = GetComponentInChildren<PhaseBlockOutline>(true);
        }

        [ContextMenu("翻转全局虚实状态")]
        public void Toggle() => SetActive(!active);

        public void SetActive(bool value)
        {
            if (active == value) return;
            active = value;
            Changed?.Invoke(active);
            Physics2D.SyncTransforms();
            ResolveMaterialization();
            RefreshOutline();
        }

        public bool CaptureState() => active;

        public void RestoreState(in bool value)
        {
            active = value;
            // Do not emit Changed or eject actors: their historical positions are restored separately.
            foreach (PhaseBlock block in blocks) if (block != null) block.ApplyRestoredState(active);
            RefreshOutline();
        }

        internal void Register(PhaseBlock block)
        {
            if (!blocks.Contains(block)) blocks.Add(block);
            RefreshOutline();
        }

        internal void Unregister(PhaseBlock block)
        {
            blocks.Remove(block);
            RefreshOutline();
        }

        internal void RefreshOutline()
        {
            if (outline != null) outline.MarkDirty(this);
        }

        private void ResolveMaterialization()
        {
            resolved.Clear();
            foreach (MovableEntity item in MovableEntity.ActiveEntities)
            {
                if (item == null || item.IsReturning || !item.Motor.CollisionsEnabled) continue;
                MovableEntity entity = item;
                var partner = entity.Motor.JoinedPartner;
                if (partner != null && partner.Entity.Part == EntityPart.Body) entity = partner.Entity;
                if (!resolved.Add(entity)) continue;
                partner = entity.Motor.JoinedPartner;
                if (partner != null) resolved.Add(partner.Entity);
                Bounds actor = entity.Motor.CollisionBounds;
                Bounds occupied = default;
                bool overlap = false;
                foreach (PhaseBlock block in blocks)
                {
                    if (block == null || !block.IsSolid || !Overlaps(actor, block.WorldBounds)) continue;
                    if (!overlap) occupied = block.WorldBounds;
                    else occupied.Encapsulate(block.WorldBounds);
                    overlap = true;
                }
                if (!overlap) continue;
                // Include touching solid blocks so a merged wall ejects to its outside edge.
                for (int pass = 0; pass < blocks.Count; pass++)
                {
                    Bounds before = occupied;
                    Bounds expanded = occupied;
                    expanded.Expand(0.002f);
                    foreach (PhaseBlock block in blocks)
                        if (block != null && block.IsSolid && expanded.Intersects(block.WorldBounds))
                            occupied.Encapsulate(block.WorldBounds);
                    if (before == occupied) break;
                }
                float skin = entity.Motor.Skin * 2f;
                Vector2[] options =
                {
                    Vector2.up * (occupied.max.y - actor.min.y + skin),
                    Vector2.down * (actor.max.y - occupied.min.y + skin),
                    Vector2.left * (actor.max.x - occupied.min.x + skin),
                    Vector2.right * (occupied.max.x - actor.min.x + skin)
                };
                Array.Sort(options, (a, b) => a.sqrMagnitude.CompareTo(b.sqrMagnitude));
                foreach (Vector2 delta in options)
                {
                    if (!ClearDestination(actor, delta, entity, partner)) continue;
                    entity.Motor.Teleport(entity.Motor.Position + delta);
                    if (partner != null) partner.Teleport(partner.Position + delta);
                    entity.Velocity = Vector2.zero;
                    if (partner != null) partner.Entity.Velocity = Vector2.zero;
                    break;
                }
            }
        }

        private static bool Overlaps(Bounds a, Bounds b) =>
            a.min.x < b.max.x - 0.0001f && a.max.x > b.min.x + 0.0001f
            && a.min.y < b.max.y - 0.0001f && a.max.y > b.min.y + 0.0001f;

        private static bool ClearDestination(Bounds actor, Vector2 delta, MovableEntity entity, KinematicMotor2D partner)
        {
            Vector2 size = (Vector2)actor.size - Vector2.one * 0.002f;
            Bounds destination = actor;
            destination.center += (Vector3)delta;
            foreach (MovableEntity other in MovableEntity.ActiveEntities)
                if (other != null && other != entity && (partner == null || other != partner.Entity) && !other.IsReturning
                    && Overlaps(destination, other.Motor.CollisionBounds)) return false;
            foreach (Collider2D collider in Physics2D.OverlapBoxAll((Vector2)actor.center + delta, size, 0f))
            {
                if (collider.isTrigger || collider.GetComponentInParent<MovableEntity>() != null) continue;
                return false;
            }
            foreach (RaycastHit2D hit in Physics2D.BoxCastAll(actor.center, size, 0f, delta.normalized, delta.magnitude))
            {
                if (hit.collider.isTrigger || hit.collider.GetComponentInParent<MovableEntity>() != null
                    || hit.collider.GetComponent<PhaseBlock>() != null) continue;
                return false;
            }
            return true;
        }
    }
}
