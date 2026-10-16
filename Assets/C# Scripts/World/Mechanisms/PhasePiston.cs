using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PhaseBlock))]
    public sealed class PhasePiston : MonoBehaviour
    {
        [Tooltip("Models use local +X as the extension direction. One segment is one unit long.")]
        public GameObject BaseModel;
        public GameObject BodyModel;
        public GameObject TipModel;
        [InspectorName("末端使用薄盖"), Tooltip("薄盖保持模型自身厚度；伸出主体填满薄盖后方，收回后薄盖仍贴在基座上。")]
        public bool UseThinCover;
        [InspectorName("薄盖厚度（格）"), Range(0.001f, 1f)] public float CoverThickness = 0.12f;
        [Min(0f)] public float AnimationDuration = 0.18f;
        [SerializeField] private int quarterTurns;
        [SerializeField, Min(1)] private int maxLength = 3;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private List<Transform> bodies = new List<Transform>();
        [SerializeField] private Transform tip;
        private float extension;
        private float target;
        public int MaxLength => maxLength;
        public int QuarterTurns => quarterTurns;
        public float VisualExtension => extension;

        public void Configure(int turns, int length)
        {
            quarterTurns = turns & 3;
            maxLength = Mathf.Clamp(length, 1, PistonPlacementSettings.MaximumLength);
            Vector2 direction = new PistonPlacementSettings { QuarterTurns = quarterTurns }.Direction;
            BoxCollider2D box = GetComponent<BoxCollider2D>();
            box.size = new Vector2(direction.x != 0 ? maxLength : 1, direction.y != 0 ? maxLength : 1);
            box.offset = direction * ((maxLength - 1) * 0.5f);
            RebuildVisual();
        }

        public void RebuildVisual()
        {
            // A shared-prefab model update can introduce a new inherited assembly beside a
            // level's saved override. Retire both before rebuilding, so only one is visible.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child == visualRoot || child.name == "Piston Visual") TileModelUtility.Release(child);
            }
            visualRoot = new GameObject("Piston Visual").transform;
            visualRoot.SetParent(transform, false);
            Vector2 direction = new PistonPlacementSettings { QuarterTurns = quarterTurns }.Direction;
            visualRoot.localPosition = new Vector3(-direction.x * 0.5f, -direction.y * 0.5f, 0.5f);
            visualRoot.localRotation = Quaternion.Euler(0f, 0f, -quarterTurns * 90f);
            Spawn(BaseModel, "Base", visualRoot).localPosition = Vector3.right * 0.1f;
            bodies.Clear();
            int bodyCount = UseThinCover ? maxLength : maxLength - 1;
            for (int i = 0; i < bodyCount; i++) bodies.Add(Spawn(BodyModel, "Body " + i, visualRoot));
            tip = Spawn(TipModel, "Tip", visualRoot);
            SetExtended(GetComponent<PhaseBlock>().SolidWhenActive == (WorldPhaseState.Existing != null && WorldPhaseState.Existing.Active), true);
        }

        private static Transform Spawn(GameObject model, string label, Transform parent)
        {
            var segment = new GameObject(label);
            segment.transform.SetParent(parent, false);
            if (model != null) Instantiate(model, segment.transform, false);
            return segment.transform;
        }

        public void SetExtended(bool extended, bool immediate)
        {
            target = extended ? 1f : 0f;
            if (immediate || AnimationDuration <= 0f || !Application.isPlaying) extension = target;
            ApplyVisual();
        }

        private void Update()
        {
            if (Mathf.Approximately(extension, target)) return;
            extension = Mathf.MoveTowards(extension, target, Time.deltaTime / Mathf.Max(0.001f, AnimationDuration));
            ApplyVisual();
        }

        private void ApplyVisual()
        {
            float length = maxLength * extension;
            if (UseThinCover)
            {
                float thickness = Mathf.Clamp(CoverThickness, 0.001f, 1f);
                float bodyEnd = Mathf.Max(0f, length - thickness);
                for (int i = 0; i < bodies.Count; i++)
                    Segment(bodies[i], i, Mathf.Clamp01(bodyEnd - i));
                // The cover model spans x=0..1; its fixed scale sets the configured thickness.
                // At rest it sits on the base; at full extension its front face meets the collider end.
                if (tip != null)
                {
                    tip.gameObject.SetActive(true);
                    tip.localPosition = new Vector3(bodyEnd, 0f, 0f);
                    tip.localScale = new Vector3(thickness, 1f, 1f);
                }
                return;
            }
            float bodyLength = Mathf.Max(0f, length - 1f);
            for (int i = 0; i < bodies.Count; i++)
                Segment(bodies[i], i, Mathf.Clamp01(bodyLength - i));
            float tipLength = Mathf.Min(1f, length);
            Segment(tip, length - tipLength, tipLength);
        }

        private static void Segment(Transform segment, float start, float length)
        {
            if (segment == null) return;
            segment.gameObject.SetActive(length > 0.0001f);
            segment.localPosition = new Vector3(start + length * 0.5f, 0f, 0f);
            segment.localScale = new Vector3(length, 1f, 1f);
        }
    }
}
