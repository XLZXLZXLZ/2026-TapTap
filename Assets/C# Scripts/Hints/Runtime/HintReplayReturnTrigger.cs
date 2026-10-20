using UnityEngine;

namespace TapTap.Hints
{
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider2D)), DefaultExecutionOrder(900)]
    public sealed class HintReplayReturnTrigger : MonoBehaviour
    {
        public HintReplayClip Clip;
        [Tooltip("仅兼容旧版相对坐标录像：对应旧录像的 Reference Frame，留空使用 Trigger。新的返程世界坐标录像忽略此字段。")]
        public Transform PlaybackOrigin;
        public HintReplayDirector Director;
        public PlayerController Player;
        public bool IsArmed { get; private set; }
        private BoxCollider2D area;
        private bool wasInside, authoredEnabled;

        private void Awake()
        {
            area = GetComponent<BoxCollider2D>();
            authoredEnabled = area.enabled;
            area.isTrigger = true;
        }
        private void Reset() => GetComponent<BoxCollider2D>().isTrigger = true;
        private void OnEnable()
        {
            EventManager.Subscribe<GameStageChanged>(StageChanged);
            ApplyStage();
        }
        private void OnDisable()
        {
            EventManager.Unsubscribe<GameStageChanged>(StageChanged);
            IsArmed = false; wasInside = false;
            if (area != null) area.enabled = false;
        }
        private void StageChanged(GameStageChanged message)
        {
            if (message.Scene == gameObject.scene) ApplyStage();
        }
        private void ApplyStage()
        {
            bool armed = GameStageProgression.HasProgressedIn(gameObject.scene);
            if (armed != IsArmed) wasInside = false;
            IsArmed = armed;
            if (area != null) area.enabled = armed && authoredEnabled;
        }
        private void FixedUpdate()
        {
            // Also covers moving an already-enabled object into a returning additive scene.
            ApplyStage();
            if (!IsArmed || area == null || !area.enabled || Clip == null || RewindManager.Rewinding
                || HintReplayContext.Paused || HintReplayContext.Transitioning(gameObject.scene)) return;
            if (Player == null) Player = HintReplayContext.Find<PlayerController>(gameObject.scene);
            if (Player == null || Player.gameObject.scene != gameObject.scene || Player.Body == null
                || Player.Body.IsReturning || !Player.Body.Motor.CollisionsEnabled) return;
            KinematicMotor2D motor = Player.Body.Motor;
            bool inside = Intersects(motor.Position + motor.CenterOffset, motor.Position + motor.CenterOffset, motor.Size * 0.5f);
            bool touched = inside;
            foreach (MotionSegment segment in motor.MotionSegments)
                if (Intersects(segment.From + motor.CenterOffset, segment.To + motor.CenterOffset, motor.Size * 0.5f))
                { touched = true; break; }
            if (!touched) { wasInside = false; return; }
            if (wasInside) return;
            if (Director == null) Director = HintReplayContext.Find<HintReplayDirector>(gameObject.scene);
            if (Director != null && Director.SelectReturnHint(this)) wasInside = inside;
        }

        // Swept AABB against the authored box, including rotated/scaled trigger axes.
        private bool Intersects(Vector2 from, Vector2 to, Vector2 bodyExtent)
        {
            Vector2 center = transform.TransformPoint(area.offset);
            Vector2 x = transform.TransformVector(Vector2.right * area.size.x * 0.5f);
            Vector2 y = transform.TransformVector(Vector2.up * area.size.y * 0.5f);
            Vector2 relative = from - center, delta = to - from;
            float enter = 0f, exit = 1f;
            return Axis(Vector2.right, relative, delta, x, y, bodyExtent, ref enter, ref exit)
                && Axis(Vector2.up, relative, delta, x, y, bodyExtent, ref enter, ref exit)
                && Axis(new Vector2(-x.y, x.x), relative, delta, x, y, bodyExtent, ref enter, ref exit)
                && Axis(new Vector2(-y.y, y.x), relative, delta, x, y, bodyExtent, ref enter, ref exit);
        }
        private static bool Axis(Vector2 axis, Vector2 relative, Vector2 delta, Vector2 x, Vector2 y,
            Vector2 bodyExtent, ref float enter, ref float exit)
        {
            float extent = Mathf.Abs(Vector2.Dot(axis, x)) + Mathf.Abs(Vector2.Dot(axis, y))
                + Mathf.Abs(axis.x) * bodyExtent.x + Mathf.Abs(axis.y) * bodyExtent.y;
            float start = Vector2.Dot(axis, relative), movement = Vector2.Dot(axis, delta);
            if (Mathf.Abs(movement) < 0.000001f) return Mathf.Abs(start) <= extent;
            float a = (-extent - start) / movement, b = (extent - start) / movement;
            enter = Mathf.Max(enter, Mathf.Min(a, b)); exit = Mathf.Min(exit, Mathf.Max(a, b));
            return enter <= exit;
        }
        private void OnDrawGizmos()
        {
            BoxCollider2D box = GetComponent<BoxCollider2D>();
            if (box == null) return;
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.25f, 0.9f, 1f, 0.15f); Gizmos.DrawCube(box.offset, box.size);
            Gizmos.color = new Color(0.25f, 0.9f, 1f, 0.8f); Gizmos.DrawWireCube(box.offset, box.size);
            Gizmos.matrix = previous;
        }
    }
}
