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
                Vector3 width = transform.TransformVector(Vector3.right * box.size.x);
                Vector3 height = transform.TransformVector(Vector3.up * box.size.y);
                return new Bounds(transform.TransformPoint(box.offset),
                    new Vector3(Mathf.Abs(width.x) + Mathf.Abs(height.x), Mathf.Abs(width.y) + Mathf.Abs(height.y), 0.1f));
            }
        }

        public void ConfigureVisual(SpriteRenderer renderer) => fill = renderer;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            box = GetComponent<BoxCollider2D>();
            if (fill == null && GetComponent<PhasePiston>() == null) fill = GetComponentInChildren<SpriteRenderer>();
            state = WorldPhaseState.Instance;
            state.Changed += Apply;
            Apply(state.Active, true);
            state.Register(this);
        }

        private void Apply(bool active) => Apply(active, false);

        private void Apply(bool active, bool immediate)
        {
            IsSolid = active == solidWhenActive;
            box.enabled = IsSolid;
            PhasePiston piston = GetComponent<PhasePiston>();
            if (piston != null) piston.SetExtended(IsSolid, immediate);
            if (fill != null)
            {
                Color color = solidColor;
                if (!IsSolid) color.a = ghostOpacity;
                fill.color = color;
            }
        }

        internal void ApplyRestoredState(bool active) => Apply(active, true);

        private void OnDisable()
        {
            if (state == null) return;
            state.Changed -= Apply;
            state.Unregister(this);
            state = null;
        }
    }
}
