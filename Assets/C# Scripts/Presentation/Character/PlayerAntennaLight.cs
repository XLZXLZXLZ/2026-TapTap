using UnityEngine;

namespace TapTap
{
    // The bulb owns its presentation; gameplay never drives its renderer or materials.
    [DisallowMultipleComponent]
    public sealed class PlayerAntennaLight : MonoBehaviour
    {
        [SerializeField] private PlayerController controller;
        [SerializeField] private Material joinedMaterial;
        [SerializeField] private Material separatedMaterial;
        private Renderer bulb;
        private PlayerController subscribedController;

        public void Configure(PlayerController player, Material joined, Material separated)
        {
            Unsubscribe();
            controller = player;
            joinedMaterial = joined;
            separatedMaterial = separated;
            if (Application.isPlaying && isActiveAndEnabled) Subscribe();
            RefreshState();
        }

        private void OnEnable()
        {
            if (controller == null) controller = GetComponentInParent<PlayerController>();
            Subscribe();
            RefreshState();
        }

        private void Subscribe()
        {
            subscribedController = controller;
            if (subscribedController != null) subscribedController.PhaseChanged += OnPhaseChanged;
        }

        private void Unsubscribe()
        {
            if (subscribedController != null) subscribedController.PhaseChanged -= OnPhaseChanged;
            subscribedController = null;
        }

        private void OnDisable() => Unsubscribe();
        private void OnPhaseChanged(PlayerPhase phase) => RefreshState();

        // Rewind restores snapshots without PhaseChanged. Sample again before rendering,
        // including while paused, so restored states never retain a future bulb color.
        private void LateUpdate() => RefreshState();

        public void RefreshState()
        {
            if (bulb == null) bulb = GetComponent<Renderer>();
            if (bulb == null || controller == null) return;
            Material material = controller.HasMagneticConnection ? joinedMaterial : separatedMaterial;
            if (material != null && bulb.sharedMaterial != material) bulb.sharedMaterial = material;
        }
    }
}
