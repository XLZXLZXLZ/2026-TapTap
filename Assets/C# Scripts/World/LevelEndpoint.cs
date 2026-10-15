using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    [AddComponentMenu("TapTap/World/Level Endpoint")]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class LevelEndpoint : MonoBehaviour
    {
        [Tooltip("触及终点后完成当前关，按大世界序号推进。")]
        public string EndpointId = "finish";
        public string Label = "终点";
        [Header("入门演出（锚点按脚底基准定位）")]
        [SerializeField] private Transform frontAnchor;
        [SerializeField] private Transform insideAnchor;
        [Min(0.1f), Tooltip("自动行走速度，单位为格/秒。")]
        public float EntryWalkSpeed = 2.5f;
        [Min(0.02f)] public float EntryRetractDuration = 0.25f;
        [Min(0.02f)] public float EntryTurnDuration = 0.16f;
        [Min(0f)] public float EntryHoldDuration = 0.18f;
        private BoxCollider2D trigger;

        public Vector3 FrontFootPosition => frontAnchor != null ? frontAnchor.position : transform.position;
        public Vector3 InsideFootPosition(float unit) => insideAnchor != null ? insideAnchor.position
            : FrontFootPosition + transform.forward.normalized * unit;
        public void ConfigureEntryAnchors(Transform front, Transform inside) { frontAnchor = front; insideAnchor = inside; }

        private void OnDrawGizmosSelected()
        {
            float unit = GetComponentInParent<LevelRegion>() != null ? GetComponentInParent<LevelRegion>().UnitSize : 1f;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(FrontFootPosition, 0.08f * unit);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(InsideFootPosition(unit), 0.08f * unit);
            Gizmos.DrawLine(FrontFootPosition, InsideFootPosition(unit));
        }

        private void Awake() => EnsureTrigger();
        private void Reset()
        {
            EnsureTrigger();
            trigger.size = new Vector2(0.8f, 1f);
            trigger.offset = new Vector2(0f, 0.5f);
        }

        public void EnsureTrigger()
        {
            trigger = GetComponent<BoxCollider2D>();
            if (trigger == null)
            {
                trigger = gameObject.AddComponent<BoxCollider2D>();
                trigger.size = new Vector2(0.8f, 1f);
                trigger.offset = new Vector2(0f, 0.5f);
            }
            trigger.isTrigger = true;
        }

        private void FixedUpdate()
        {
            WorldMapProgression flow = WorldMapProgression.Current;
            if (flow == null || !flow.ExitsEnabled || WorldMapProgression.InputBlocked || RewindManager.Rewinding) return;
            // The player uses a custom kinematic motor, so do not rely on Rigidbody trigger callbacks.
            foreach (MovableEntity entity in MovableEntity.ActiveEntities)
            {
                if (entity == null || entity.Part != EntityPart.Body || entity.IsReturning
                    || entity.GetComponentInParent<PlayerController>() == null) continue;
                if (trigger.IsTouching(entity.GetComponent<Collider2D>())
                    || TouchesBody(entity))
                {
                    flow.CompleteCurrent(this);
                    break;
                }
            }
        }

        private bool TouchesBody(MovableEntity entity)
        {
            Bounds actor = entity.Motor.CollisionBounds;
            if (trigger.bounds.Intersects(actor)) return true;
            Bounds expanded = trigger.bounds;
            expanded.Expand(actor.size);
            foreach (MotionSegment segment in entity.Motor.MotionSegments)
            {
                Vector2 delta = segment.To - segment.From;
                if (delta.sqrMagnitude < 0.000001f) continue;
                if (expanded.IntersectRay(new Ray(segment.From + entity.Motor.CenterOffset, delta.normalized), out float distance)
                    && distance <= delta.magnitude) return true;
            }
            return false;
        }
    }
}
