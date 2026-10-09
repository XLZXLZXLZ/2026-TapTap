using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D), typeof(WorldSurface))]
    public sealed class PhaseBlock : MonoBehaviour
    {
        [SerializeField] private bool solidWhenActive = true;
        [SerializeField] private SpriteRenderer fill;
        [SerializeField] private Color solidColor = new Color(0.4f, 0.76f, 0.94f, 0.85f);
        [SerializeField, Range(0f, 1f)] private float ghostOpacity = 0.1f;
        private BoxCollider2D box;
        private WorldPhaseState state;
        public bool SolidWhenActive { get => solidWhenActive; set => solidWhenActive = value; }
        public bool IsSolid { get; private set; }
        public Bounds WorldBounds
        {
            get
            {
                if (box == null) box = GetComponent<BoxCollider2D>();
                Vector3 scale = transform.lossyScale;
                return new Bounds(transform.TransformPoint(box.offset),
                    new Vector3(box.size.x * Mathf.Abs(scale.x), box.size.y * Mathf.Abs(scale.y), 0.1f));
            }
        }

        public void ConfigureVisual(SpriteRenderer renderer) => fill = renderer;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            box = GetComponent<BoxCollider2D>();
            if (fill == null) fill = GetComponentInChildren<SpriteRenderer>();
            state = WorldPhaseState.Instance;
            state.Changed += Apply;
            Apply(state.Active);
            state.Register(this);
        }

        private void Apply(bool active)
        {
            IsSolid = active == solidWhenActive;
            box.enabled = IsSolid;
            if (fill != null)
            {
                Color color = solidColor;
                if (!IsSolid) color.a = ghostOpacity;
                fill.color = color;
            }
        }

        internal void ApplyRestoredState(bool active) => Apply(active);

        private void OnDisable()
        {
            if (state == null) return;
            state.Changed -= Apply;
            state.Unregister(this);
            state = null;
        }
    }
}
