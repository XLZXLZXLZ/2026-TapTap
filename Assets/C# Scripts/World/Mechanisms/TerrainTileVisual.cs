using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    // Physics stays on the common terrain root; styles replace only its visual child.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class TerrainTileVisual : MonoBehaviour
    {
        public GameObject DefaultModel;
        public GameObject LegacyVisual;
        [SerializeField, HideInInspector] private string placementId;
        private GameObject appliedModel;
        private GameObject visual;
        private readonly Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();

        public string PlacementId
        {
            get
            {
                if (!string.IsNullOrEmpty(placementId)) return placementId;
                // Compatibility with already generated prefabs. New builds serialize the ID explicitly.
                string label = gameObject.name;
                string suffix = label.Length >= 32 ? label.Substring(label.Length - 32) : "";
                return Guid.TryParseExact(suffix, "N", out _) ? suffix : null;
            }
            set { placementId = value; }
        }

        public Material PaintMaterial
        {
            get
            {
                LevelRegion region = GetComponentInParent<LevelRegion>(true);
                return region != null && region.Source != null && region.Source.TerrainPaint != null
                    ? region.Source.TerrainPaint.GetPaint(region.WorldModuleId, PlacementId) : null;
            }
        }

        private void OnEnable()
        {
            LevelRegion region = GetComponentInParent<LevelRegion>(true);
            ApplyModel(region != null && region.Source != null ? region.Source.Style?.TerrainModel : DefaultModel);
        }

        public void ApplyModel(GameObject model, bool forceRefresh = false)
        {
            if (!gameObject.scene.IsValid()) return;
            if (LegacyVisual != null) LegacyVisual.SetActive(model == null);
            if (!forceRefresh && visual != null && appliedModel == model)
            { visual.SetActive(true); RefreshPaint(); return; }
            ReleaseVisual();
            appliedModel = model;
            if (model != null)
            {
                visual = Instantiate(model, transform, false);
                visual.name = "Styled Terrain Visual";
                // Regenerate from the style asset on load, keeping per-tile model copies out of scenes.
                visual.hideFlags = HideFlags.DontSave;
            }
            CaptureMaterials();
            RefreshPaint();
        }

        private void CaptureMaterials()
        {
            originalMaterials.Clear();
            GameObject target = visual != null ? visual : LegacyVisual;
            if (target == null) return;
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
                originalMaterials[renderer] = renderer.sharedMaterials;
        }

        public bool CanPaint
        {
            get
            {
                LevelRegion region = GetComponentInParent<LevelRegion>(true);
                TerrainPaintProfile profile = region != null ? region.Source?.TerrainPaint : null;
                if (profile == null || profile.BaseMaterial == null) return false;
                foreach (Material[] materials in originalMaterials.Values)
                    if (Array.IndexOf(materials, profile.BaseMaterial) >= 0) return true;
                return false;
            }
        }

        public void RefreshPaint()
        {
            LevelRegion region = GetComponentInParent<LevelRegion>(true);
            TerrainPaintProfile profile = region != null ? region.Source?.TerrainPaint : null;
            Material replacement = profile != null ? profile.GetPaint(region.WorldModuleId, PlacementId) : null;
            foreach (var pair in originalMaterials)
            {
                if (pair.Key == null) continue;
                var materials = (Material[])pair.Value.Clone();
                if (replacement != null)
                    for (int slot = 0; slot < materials.Length; slot++)
                        if (materials[slot] == profile.BaseMaterial) materials[slot] = replacement;
                pair.Key.sharedMaterials = materials;
            }
        }

        private void OnDisable() { if (visual != null) visual.SetActive(false); }
        private void OnDestroy() => ReleaseVisual();
        private void ReleaseVisual()
        {
            foreach (var pair in originalMaterials)
                if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            originalMaterials.Clear();
            if (visual == null) return;
            visual.SetActive(false);
            if (Application.isPlaying) Destroy(visual); else DestroyImmediate(visual);
            visual = null;
        }
    }
}
