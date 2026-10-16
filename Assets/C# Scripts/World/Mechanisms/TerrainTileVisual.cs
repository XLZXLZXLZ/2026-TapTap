using UnityEngine;

namespace TapTap
{
    // Physics stays on the common terrain root; styles replace only its visual child.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class TerrainTileVisual : MonoBehaviour
    {
        public GameObject DefaultModel;
        public GameObject LegacyVisual;
        private GameObject appliedModel;
        private GameObject visual;

        private void OnEnable()
        {
            LevelRegion region = GetComponentInParent<LevelRegion>(true);
            ApplyModel(region != null && region.Source != null ? region.Source.Style?.TerrainModel : DefaultModel);
        }

        public void ApplyModel(GameObject model, bool forceRefresh = false)
        {
            if (!gameObject.scene.IsValid()) return;
            if (LegacyVisual != null) LegacyVisual.SetActive(model == null);
            if (!forceRefresh && visual != null && appliedModel == model) { visual.SetActive(true); return; }
            ReleaseVisual();
            appliedModel = model;
            if (model == null) return;
            visual = Instantiate(model, transform, false);
            visual.name = "Styled Terrain Visual";
            // Regenerate from the style asset on load, keeping per-tile model copies out of scenes.
            visual.hideFlags = HideFlags.DontSave;
        }

        private void OnDisable() { if (visual != null) visual.SetActive(false); }
        private void OnDestroy() => ReleaseVisual();
        private void ReleaseVisual()
        {
            if (visual == null) return;
            visual.SetActive(false);
            if (Application.isPlaying) Destroy(visual); else DestroyImmediate(visual);
            visual = null;
        }
    }
}
