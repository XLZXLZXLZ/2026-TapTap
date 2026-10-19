using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider2D)), DefaultExecutionOrder(850)]
    public sealed class TutorialPromptTrigger2D : MonoBehaviour
    {
        public TutorialPromptView PromptPrefab;
        public TutorialPromptPresenter PresenterPrefab;
        [Tooltip("可留空，运行时寻找同一场景中的角色。只检测下半身。")]
        public PlayerController Player;
        public bool PlayOnce = true;
        public bool HasTriggered { get; private set; }
        private bool wasInside;
        private BoxCollider2D area;
        private TutorialPromptPresenter presenter;

        private void Awake() { area = GetComponent<BoxCollider2D>(); area.isTrigger = true; }
        private void Reset() { GetComponent<BoxCollider2D>().isTrigger = true; }

        private void OnEnable()
        {
            EventManager.Subscribe<GameStageChanged>(StageChanged);
            if (GameStageProgression.HasProgressedIn(gameObject.scene)) enabled = false;
        }
        private void OnDisable() => EventManager.Unsubscribe<GameStageChanged>(StageChanged);
        private void StageChanged(GameStageChanged message)
        {
            if (message.Scene == gameObject.scene && message.Stage >= GameStage.Returning) enabled = false;
        }

        private void FixedUpdate()
        {
            // Also covers a runtime-created trigger moved into a returning scene after OnEnable.
            if (GameStageProgression.HasProgressedIn(gameObject.scene)) { enabled = false; return; }
            if (PromptPrefab == null || (PlayOnce && HasTriggered) || !area.enabled
                || TutorialPromptPresenter.GameplayBlocked || RewindManager.Rewinding) return;
            if (Player == null)
                foreach (PlayerController candidate in FindObjectsOfType<PlayerController>())
                    if (candidate.gameObject.scene == gameObject.scene) { Player = candidate; break; }
            if (Player == null || Player.Body == null || Player.Body.IsReturning) return;
            KinematicMotor2D motor = Player.Body.Motor;
            if (!motor.CollisionsEnabled) return;
            bool inside = TouchesBody(motor);
            if (inside && !wasInside)
            {
                if (presenter == null) presenter = TutorialPromptPresenter.Ensure(gameObject.scene, PresenterPrefab);
                if (presenter.Request(PromptPrefab)) HasTriggered = true;
            }
            wasInside = inside;
        }

        // Swept separating-axis test: includes fast crossings and rotated/scaled trigger boxes.
        public bool TouchesBody(KinematicMotor2D motor)
        {
            if (area == null) area = GetComponent<BoxCollider2D>();
            Bounds body = new Bounds(motor.Position + motor.CenterOffset, motor.Size);
            if (Intersects(body.center, body.center, body.extents)) return true;
            foreach (MotionSegment segment in motor.MotionSegments)
            {
                if (Intersects(segment.From + motor.CenterOffset, segment.To + motor.CenterOffset, body.extents)) return true;
            }
            return false;
        }

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
            enter = Mathf.Max(enter, Mathf.Min(a, b));
            exit = Mathf.Min(exit, Mathf.Max(a, b));
            return enter <= exit;
        }

        private void OnDrawGizmos()
        {
            BoxCollider2D box = GetComponent<BoxCollider2D>();
            if (box == null) return;
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.12f);
            Gizmos.DrawCube(box.offset, box.size);
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireCube(box.offset, box.size);
            Gizmos.matrix = previous;
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position, PromptPrefab != null
                ? PromptPrefab.Prefix + "[" + PromptPrefab.Key + "]" + PromptPrefab.Suffix : "教学提示（未配置）");
#endif
        }
    }
}
