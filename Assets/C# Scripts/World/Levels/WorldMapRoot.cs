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
            if (GetComponent<WorldMapProgression>() == null) gameObject.AddComponent<WorldMapProgression>();
        }

        public LevelRegion FindRegion(WorldMapModule module)
        {
            Transform content = generated != null ? generated : transform.Find("Generated");
            if (content == null) return null;
            foreach (LevelRegion region in content.GetComponentsInChildren<LevelRegion>(true))
            {
                if (region.WorldModuleId == module.Id) return region;
                if (string.IsNullOrEmpty(region.WorldModuleId) && region.Source == module.Level
                    && Vector2.Distance(region.transform.localPosition,
                        (Vector2)module.Position * region.UnitSize) < 0.01f) return region;
            }
            return null;
        }

        public void ShowOnly(WorldMapModule module)
        {
            foreach (WorldMapModule item in Source.OrderedModules())
            {
                LevelRegion region = FindRegion(item);
                if (region != null) region.gameObject.SetActive(item == module);
            }
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
        }
    }
}
