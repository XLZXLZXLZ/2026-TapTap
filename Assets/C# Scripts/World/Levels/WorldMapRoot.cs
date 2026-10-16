using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-80)]
    public sealed class WorldMapRoot : MonoBehaviour
    {
        public WorldMapDefinition Source;
        private Transform generated;
        private Transform templates;
        private GameObject initialLayout;
        private readonly Dictionary<string, LevelRegion> regions = new Dictionary<string, LevelRegion>();
        private Bounds cachedBounds;
        private bool hasBounds;
        public Bounds WorldBounds
        {
            get
            {
                if (hasBounds) return cachedBounds;
                Bounds bounds = default;
                bool first = true;
                foreach (WorldMapModule module in Source.OrderedModules())
                {
                    LevelRegion region = FindRegion(module);
                    if (region == null) continue;
                    if (first) { bounds = region.WorldBounds; first = false; }
                    else bounds.Encapsulate(region.WorldBounds);
                }
                cachedBounds = bounds; hasBounds = true;
                return bounds;
            }
        }

        private void Awake()
        {
            generated = transform.Find("Generated");
            if (generated == null || Source == null) return;
            var storage = new GameObject("Initial World State");
            storage.transform.SetParent(transform, false);
            storage.SetActive(false);
            templates = storage.transform;
            // Snapshot the authored hierarchy before any simulation, including Custom objects and overrides.
            initialLayout = Instantiate(generated.gameObject, templates, false);
            PrepareExploration(new HashSet<string>());
            if (GetComponent<WorldMapProgression>() == null) gameObject.AddComponent<WorldMapProgression>();
        }

        public LevelRegion FindRegion(WorldMapModule module)
        {
            if (regions.TryGetValue(module.Id, out LevelRegion cached) && cached != null) return cached;
            Transform content = generated != null ? generated : transform.Find("Generated");
            if (content == null) return null;
            foreach (LevelRegion region in content.GetComponentsInChildren<LevelRegion>(true))
            {
                if (region.WorldModuleId == module.Id) { regions[module.Id] = region; return region; }
                if (string.IsNullOrEmpty(region.WorldModuleId) && region.Source == module.Level
                    && Vector2.Distance(region.transform.localPosition,
                        (Vector2)module.Position * region.UnitSize) < 0.01f)
                { regions[module.Id] = region; return region; }
            }
            return null;
        }

        public void InvalidateRegions() { regions.Clear(); hasBounds = false; }

        public void PrepareExploration(ISet<string> discovered)
        {
            foreach (WorldMapModule item in Source.OrderedModules())
            {
                LevelRegion region = FindRegion(item);
                if (region == null) continue;
                RegionRevealView view = region.GetComponent<RegionRevealView>();
                if (view == null) view = region.gameObject.AddComponent<RegionRevealView>();
                view.SetDiscovered(discovered.Contains(item.Id), true);
                region.gameObject.SetActive(true);
            }
            WorldExplorationMask mask = GetComponent<WorldExplorationMask>();
            if (mask == null) mask = gameObject.AddComponent<WorldExplorationMask>();
            mask.RefreshRegions(this);
            Physics2D.SyncTransforms();
        }

        public void RefreshLevels()
        {
            if (initialLayout == null) return;
            generated.gameObject.SetActive(false);
            Destroy(generated.gameObject);
            // Instantiate under an inactive parent so hidden rooms never briefly register or simulate.
            GameObject fresh = Instantiate(initialLayout, templates, false);
            fresh.name = "Generated";
            foreach (LevelRegion region in fresh.GetComponentsInChildren<LevelRegion>(true))
                region.gameObject.SetActive(false);
            fresh.transform.SetParent(transform, false);
            generated = fresh.transform;
            regions.Clear();
            hasBounds = false;
        }
    }
}
