using UnityEngine;

namespace TapTap
{
    public enum ConveyorPart { Left, Middle, Right }

    [DisallowMultipleComponent, RequireComponent(typeof(WorldSurface)), DefaultExecutionOrder(50)]
    public sealed class ConveyorTileVisual : MonoBehaviour
    {
        public string ConnectionGroup = "conveyor";
        public GameObject LeftModel;
        public GameObject MiddleModel;
        public GameObject RightModel;
        public GameObject[] LegacyVisuals;
        public Material ArrowGlowMaterial;
        [Min(0.01f)] public float FlashPeriod = 1.5f;
        [Min(0f)] public float FlashDuration = 0.6f;
        public bool FacingLeftWhenStopped;
        [SerializeField] private ConveyorPart part = ConveyorPart.Middle;
        [SerializeField] private Transform visual;
        private WorldSurface surface;
        private ConveyorArrowPulse[] arrows;
        public ConveyorPart Part => part;
        public bool FacingLeft => Surface.ConveyorSpeed < 0f || Surface.ConveyorSpeed == 0f && FacingLeftWhenStopped;
        private WorldSurface Surface => surface != null ? surface : surface = GetComponent<WorldSurface>();

        public void ApplyPart(ConveyorPart value)
        {
            GameObject model = value == ConveyorPart.Left ? LeftModel : value == ConveyorPart.Right ? RightModel : MiddleModel;
            model = LevelStyle.ModelFor(transform, model);
            if (model == null) return;
            part = value;
            if (visual != null) TileModelUtility.Release(visual);
            if (LegacyVisuals != null)
                foreach (GameObject legacy in LegacyVisuals) if (legacy != null) legacy.SetActive(false);
            visual = new GameObject("Conveyor Visual (" + value + ")").transform;
            visual.SetParent(transform, false);
            Instantiate(model, visual, false);
            arrows = visual.GetComponentsInChildren<ConveyorArrowPulse>(true);
            RefreshMotion();
        }

        private void OnEnable() => RefreshMotion();
        private void LateUpdate() => RefreshMotion();

        public void RefreshMotion()
        {
            if (visual == null) return;
            if (Surface.ConveyorSpeed != 0f) FacingLeftWhenStopped = Surface.ConveyorSpeed < 0f;
            bool left = FacingLeft;
            visual.localScale = new Vector3(part == ConveyorPart.Middle && left ? -1f : 1f, 1f, 1f);
            if (arrows == null) arrows = visual.GetComponentsInChildren<ConveyorArrowPulse>(true);
            foreach (ConveyorArrowPulse arrow in arrows)
                if (arrow != null) arrow.Configure(ArrowGlowMaterial, FlashPeriod, FlashDuration, left, Surface.ConveyorSpeed != 0f);
        }
    }
}
