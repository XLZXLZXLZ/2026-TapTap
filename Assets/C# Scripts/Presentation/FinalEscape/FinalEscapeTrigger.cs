using UnityEngine;

namespace TapTap
{
    [RequireComponent(typeof(BoxCollider2D)), DefaultExecutionOrder(800)]
    public sealed class FinalEscapeTrigger : MonoBehaviour
    {
        public FinalEscapeSequence Sequence;
        [Min(1)] public int Step = 1;
        private BoxCollider2D area;
        private void Awake() { area = GetComponent<BoxCollider2D>(); area.isTrigger = true; }

        private void FixedUpdate()
        {
            if (Sequence == null || !Sequence.CanTrigger || Step <= Sequence.CurrentStep) return;
            PlayerController player = Sequence.Player;
            if (player == null || player.Body == null || player.Body.IsReturning) return;
            KinematicMotor2D motor = player.Body.Motor;
            // Do not use CollisionBounds: it can include the joined head.
            Bounds body = new Bounds(motor.Position + motor.CenterOffset, motor.Size);
            Bounds trigger = area.bounds;
            if (trigger.Intersects(body)) { Sequence.Advance(Step); return; }
            trigger.Expand(body.size);
            foreach (MotionSegment segment in motor.MotionSegments)
            {
                Vector2 delta = segment.To - segment.From;
                if (delta.sqrMagnitude < 0.000001f) continue;
                if (trigger.IntersectRay(new Ray(segment.From + motor.CenterOffset, delta.normalized), out float distance)
                    && distance <= delta.magnitude) { Sequence.Advance(Step); return; }
            }
        }

        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider2D>();
            if (box == null) return;
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 1f, 0.8f, 0.15f);
            Gizmos.DrawCube(box.offset, box.size);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(box.offset, box.size);
            Gizmos.matrix = old;
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position, "逃离推进 " + Step);
#endif
        }
    }
}
