using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Hints
{
    // All knowledge of the existing game flow stays at this boundary, never in clip/ghost data.
    public static class HintReplayContext
    {
        public static T Find<T>(Scene scene) where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (T candidate in root.GetComponentsInChildren<T>())
                    if (candidate.gameObject.activeInHierarchy) return candidate;
            return null;
        }

        public static WorldMapProgression Progression(Scene scene)
        {
            WorldMapProgression map = WorldMapProgression.Current;
            return map != null && map.gameObject.scene == scene ? map : Find<WorldMapProgression>(scene);
        }

        public static LevelRegion Region(Scene scene, PlayerController player)
        {
            WorldMapProgression map = Progression(scene);
            if (map != null) return map.CurrentRegion;
            LevelRegion only = null;
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (LevelRegion candidate in root.GetComponentsInChildren<LevelRegion>())
                {
                    only = candidate; count++;
                    if (player != null && player.Body != null && candidate.ContainsWorldPoint(player.Body.Motor.Position))
                        return candidate;
                }
            return count == 1 ? only : null;
        }

        public static bool Paused => Time.timeScale <= 0f || PauseMenu.BlocksInput
            || (!Application.isBatchMode && !Application.isFocused);

        public static bool Transitioning(Scene scene)
        {
            WorldMapProgression map = Progression(scene);
            return WorldMapSceneRouter.IsLoading || WorldLevelMenu.IsOpen || GameStageProgression.InputBlocked
                || (map != null && (map.IsTransitioning || map.IsComplete))
                || (SceneResetManager.Existing != null && SceneResetManager.Existing.IsResetting)
                || GameplayActionLocks.IsLocked(GameplayAction.Input | GameplayAction.PlayerControls, scene);
        }
    }
}
