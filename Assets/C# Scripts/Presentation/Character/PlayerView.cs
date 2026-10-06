using UnityEngine;

namespace TapTap
{
    public sealed class PlayerView : MonoBehaviour
    {
        [SerializeField] private PlayerController controller;
        [SerializeField] private MovableEntity body;
        [SerializeField] private MovableEntity head;
        [SerializeField] private PlayerConfig config;
        private PlayerController subscribedController;
        private LineRenderer magnet;
        private LineRenderer guide;
        private Material lineMaterial;
        private float magnetAlpha;
        private float guideAlpha;
        private TrailRenderer bodyTrail;
        private TrailRenderer headTrail;

        public void Configure(PlayerController player, MovableEntity lower, MovableEntity upper, PlayerConfig settings)
        {
            controller = player; body = lower; head = upper; config = settings;
            if (!Application.isPlaying) return;
            if (subscribedController != controller)
            {
                if (subscribedController != null) subscribedController.Effect -= OnEffect;
                subscribedController = controller;
                if (controller != null) controller.Effect += OnEffect;
            }
            EnsureVisuals();
        }

        private void Start() => Configure(controller, body, head, config);

        private void EnsureVisuals()
        {
            if (magnet != null) return;
            lineMaterial = new Material(Shader.Find("Sprites/Default"));
            magnet = MakeLine("Magnetic connection", 0.035f);
            guide = MakeLine("Recall guide", 0.025f);
            bodyTrail = MakeTrail(body, new Color(0.22f, 0.89f, 0.78f));
            headTrail = MakeTrail(head, new Color(1f, 0.94f, 0.79f));
        }

        private LineRenderer MakeLine(string name, float width)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(transform, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = lineMaterial;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = line.endWidth = width;
            line.sortingOrder = 7;
            return line;
        }

        private TrailRenderer MakeTrail(MovableEntity entity, Color color)
        {
            TrailRenderer trail = entity.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = lineMaterial;
            trail.time = 0.18f;
            trail.startWidth = 0.35f;
            trail.endWidth = 0f;
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
            trail.sortingOrder = 8;
            trail.emitting = false;
            return trail;
        }

        private void LateUpdate()
        {
            if (controller == null || body == null || head == null || config == null) return;
            EnsureVisuals();
            float dt = Time.deltaTime;
            bool connected = controller.HasMagneticConnection && controller.Phase != PlayerPhase.Joined
                && !body.IsReturning && !head.IsReturning;
            magnetAlpha = Mathf.MoveTowards(magnetAlpha, connected ? 0.9f : 0f, dt * 8f);
            guideAlpha = Mathf.MoveTowards(guideAlpha, controller.GuideVisible ? 0.75f : 0f, dt * 6f);
            Color magneticColor = new Color(0.65f, 0.92f, 1f, magnetAlpha);
            magnet.startColor = magnet.endColor = magneticColor;
            magnet.SetPosition(0, body.transform.position);
            magnet.SetPosition(1, head.transform.position);
            Color guideColor = controller.CanRecall ? new Color(0.25f, 0.65f, 1f, guideAlpha)
                : new Color(1f, 0.32f, 0.32f, guideAlpha);
            guide.startColor = guide.endColor = guideColor;
            guide.SetPosition(0, head.transform.position);
            guide.SetPosition(1, head.transform.position + Vector3.down * 1000f);
            bodyTrail.emitting = body.IsReturning;
            headTrail.emitting = head.IsReturning;

            if (head.Visual != null)
            {
                Vector3 target = controller.HasMagneticConnection && !head.IsReturning
                    ? new Vector3(-body.Velocity.x * 0.007f, 0f, 0f) : Vector3.zero;
                head.Visual.localPosition = Vector3.Lerp(head.Visual.localPosition, target, 1f - Mathf.Exp(-14f * dt));
            }
        }

        private void OnEffect(PlayerEffect effect) => EffectManager.Instance.Play(effect, body.Visual);

        private void OnDestroy()
        {
            if (subscribedController != null) subscribedController.Effect -= OnEffect;
            if (lineMaterial != null) Destroy(lineMaterial);
        }
    }
}
