using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    // Bind an existing authored screen. Map synchronization never regenerates UI assets.
    public static class NeonWorldMapSceneBinding
    {
        public static void BindExisting(Scene scene, GameObject owner, WorldMapDefinition map)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (var candidate in root.GetComponentsInChildren<BlueprintWorldMapScreen>(true))
                    if (candidate.Source == map)
                    {
                        var binding = owner.GetComponent<WorldMapAssemblyView>();
                        if (binding == null) binding = owner.AddComponent<WorldMapAssemblyView>();
                        if (binding.BlueprintScreen == candidate) return;
                        binding.BlueprintScreen = candidate;
                        LevelPrefabBuilder.RecordInstanceOverrides(binding.gameObject);
                        return;
                    }
            NeonWorldMapScreen screen = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (NeonWorldMapScreen candidate in root.GetComponentsInChildren<NeonWorldMapScreen>(true))
                    if (candidate.Source == map) screen = candidate;
            if (screen == null) return;
            WorldMapAssemblyView view = owner.GetComponent<WorldMapAssemblyView>();
            if (view == null) view = owner.AddComponent<WorldMapAssemblyView>();
            if (view.SceneScreen == screen) return;
            view.SceneScreen = screen;
            LevelPrefabBuilder.RecordInstanceOverrides(view.gameObject);
        }
    }
}
