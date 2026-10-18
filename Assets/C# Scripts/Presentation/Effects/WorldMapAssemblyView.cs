using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    public sealed class WorldMapAssemblyView : MonoBehaviour
    {
        [Tooltip("场景中提前摆放的地图界面；运行时只切换显隐。")]
        public NeonWorldMapScreen SceneScreen;
        private NeonWorldMapScreen neonScreen;
        public bool UsesNeonScreen => neonScreen != null;
        public NeonWorldMapScreen NeonScreen => neonScreen;
        public static List<WorldMapModule> SelectableModules(WorldMapDefinition world) => NeonWorldMapScreen.SelectableModules(world);

        private NeonWorldMapScreen FindScreen(WorldMapDefinition world)
        {
            if (SceneScreen != null && SceneScreen.Source == world) return SceneScreen;
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                foreach (NeonWorldMapScreen screen in root.GetComponentsInChildren<NeonWorldMapScreen>(true))
                    if (screen.Source == world) return SceneScreen = screen;
            return null;
        }

        public List<WorldMapModule> AvailableModules(WorldMapDefinition world)
        {
            NeonWorldMapScreen screen = FindScreen(world);
            return screen != null ? screen.AvailableModules(world) : new List<WorldMapModule>();
        }

        private bool Prepare(WorldMapDefinition world, bool selection)
        {
            NeonWorldMapScreen screen = FindScreen(world);
            if (screen == null)
            {
                Debug.LogError("缺少已配置的选关／转场界面：" + world.name, this);
                return false;
            }
            Hide();
            neonScreen = screen;
            neonScreen.Initialize(selection);
            return true;
        }

        public IEnumerator Play(WorldMapRoot world, IReadOnlyList<WorldMapModule> modules,
            HashSet<string> passed, WorldMapModule incoming, float duration, float hold, string message = null,
            WorldMapModule departing = null)
        {
            if (Prepare(world.Source, false))
                yield return neonScreen.Play(passed, incoming, duration, hold, departing);
            else
                yield return ScreenFadeOverlay.Instance.FadeTo(0f, 0.2f);
        }

        public void ShowSelection(WorldMapDefinition world) => Prepare(world, true);
        public void SetMenuSelection(WorldMapModule module, string notice = null) => neonScreen?.SetSelected(module, notice);
        public bool TryPick(Vector2 screenPoint, out WorldMapModule picked)
        {
            picked = null;
            return neonScreen != null && neonScreen.TryPick(screenPoint, out picked);
        }
        public Vector2 LabelScreenPosition(WorldMapModule module) => neonScreen != null
            ? neonScreen.LabelScreenPosition(module) : Vector2.zero;
        public void ShowCompletion() => neonScreen?.ShowCompletion();
        public void Hide()
        {
            if (neonScreen != null) neonScreen.HidePresentation();
            neonScreen = null;
        }
        private void OnDisable() => Hide();
    }
}
