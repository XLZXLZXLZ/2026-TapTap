using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    // Bind an existing authored screen. Map synchronization never regenerates UI assets.
    public static class NeonWorldMapSceneBinding
    {
        public static void BindExisting(Scene scene, GameObject owner, WorldMapDefinition map)
        {
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
