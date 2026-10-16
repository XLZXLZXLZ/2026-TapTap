using UnityEngine;

namespace TapTap
{
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(LevelRegion)), DefaultExecutionOrder(-150)]
    public sealed class LevelStyleView : MonoBehaviour
    {
        private LevelRegion region;
        private LevelDefinition cachedSource;
        private LevelStyle cachedStyle;
        private bool hadModelOverrides;
#if UNITY_EDITOR
        private int styleVersion;
#endif
        private void OnEnable() => Refresh();

        private void LateUpdate()
        {
            if (region == null) region = GetComponent<LevelRegion>();
            LevelStyle style = region.Source != null ? region.Source.Style : null;
            bool changed = cachedSource != region.Source || cachedStyle != style;
#if UNITY_EDITOR
            changed |= style != null && styleVersion != UnityEditor.EditorUtility.GetDirtyCount(style);
#endif
            if (changed) Refresh();
        }

        public void Refresh()
        {
            if (!gameObject.scene.IsValid()) return;
            if (region == null) region = GetComponent<LevelRegion>();
            LevelStyle style = region.Source != null ? region.Source.Style : null;
            Transform generated = transform.Find("Generated");
            cachedSource = region.Source;
            cachedStyle = style;
#if UNITY_EDITOR
            styleVersion = style != null ? UnityEditor.EditorUtility.GetDirtyCount(style) : 0;
#endif
            if (generated == null) return;
            foreach (TerrainTileVisual tile in generated.GetComponentsInChildren<TerrainTileVisual>(true))
                tile.ApplyModel(style != null ? style.TerrainModel : null);
            bool modelOverrides = style != null && style.HasModelOverrides;
            if (modelOverrides || hadModelOverrides)
            {
                foreach (PlatformTileVisual tile in generated.GetComponentsInChildren<PlatformTileVisual>(true)) tile.ApplyPart(tile.Part);
                foreach (ConveyorTileVisual tile in generated.GetComponentsInChildren<ConveyorTileVisual>(true)) tile.ApplyPart(tile.Part);
                foreach (PhasePiston piston in generated.GetComponentsInChildren<PhasePiston>(true)) piston.RebuildVisual();
            }
            hadModelOverrides = modelOverrides;
            if (Application.isPlaying) GetComponent<RegionRevealView>()?.RefreshVisuals();
        }
    }
}
