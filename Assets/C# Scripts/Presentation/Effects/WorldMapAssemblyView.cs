using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    public sealed class WorldMapAssemblyView : MonoBehaviour
    {
        [Tooltip("场景中提前摆放的地图界面；运行时只切换显隐。")]
        public NeonWorldMapScreen SceneScreen;
        [Tooltip("当前场景独立配置的贴图界面。选关和转场只共享 Art 素材。")]
        public BlueprintWorldMapScreen BlueprintScreen;
        private BlueprintWorldMapScreen blueprint;
        public BlueprintWorldMapScreen ActiveBlueprint => blueprint;
        private NeonWorldMapScreen neonScreen;
        public bool UsesNeonScreen => neonScreen != null;
        public NeonWorldMapScreen NeonScreen => neonScreen;
        public static List<WorldMapModule> SelectableModules(WorldMapDefinition world)
        {
            BlueprintMapArt art = Resources.Load<BlueprintMapArt>("WorldMapSelection/BlueprintArt");
            if (art == null || art.Source != world) return NeonWorldMapScreen.SelectableModules(world);
            var ids = new HashSet<string>();
            foreach (var piece in art.Pieces) ids.Add(piece.ModuleId);
            return world.OrderedModules().FindAll(module => ids.Contains(module.Id));
        }

        private BlueprintWorldMapScreen FindBlueprint(WorldMapDefinition world, bool selection)
        {
            var role = selection ? BlueprintWorldMapScreen.ScreenRole.Selection : BlueprintWorldMapScreen.ScreenRole.Transition;
            if (BlueprintScreen != null && BlueprintScreen.Source == world && BlueprintScreen.Role == role) return BlueprintScreen;
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                foreach (var screen in root.GetComponentsInChildren<BlueprintWorldMapScreen>(true))
                    if (screen.Source == world && screen.Role == role) return BlueprintScreen = screen;
            return null;
        }

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
            var tiles = FindBlueprint(world, GetComponent<WorldLevelMenu>() != null);
            if (tiles != null) return tiles.AvailableModules(world);
            NeonWorldMapScreen screen = FindScreen(world);
            return screen != null ? screen.AvailableModules(world) : new List<WorldMapModule>();
        }

        private bool Prepare(WorldMapDefinition world, bool selection)
        {
            var tiles = FindBlueprint(world, selection);
            if (tiles != null)
            {
                Hide();
                blueprint = tiles;
                blueprint.Initialize(selection);
                return true;
            }
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
            {
                if (blueprint != null) yield return blueprint.Play(passed, incoming, hold, departing);
                else yield return neonScreen.Play(passed, incoming, duration, hold, departing);
            }
            else
                yield return ScreenFadeOverlay.Instance.FadeTo(0f, 0.2f);
        }

        public void ShowSelection(WorldMapDefinition world) => Prepare(world, true);
        public void SetMenuSelection(WorldMapModule module, string notice = null, bool focus = false)
        {
            if (blueprint != null) blueprint.SetSelected(module, focus);
            else neonScreen?.SetSelected(module, notice);
        }
        public bool CanSelect(WorldMapModule module) => blueprint != null ? blueprint.CanSelect(module)
            : neonScreen != null && neonScreen.CanSelect(module);
        public void SetHovered(WorldMapModule module) => blueprint?.SetHovered(module);
        public void SetPressed(WorldMapModule module) => blueprint?.SetPressed(module);
        public void HandleNavigation() => blueprint?.HandleNavigation();
        public IEnumerator Confirm(WorldMapModule module)
        {
            if (blueprint != null) yield return blueprint.Confirm(module);
        }
        public bool TryPick(Vector2 screenPoint, out WorldMapModule picked)
        {
            picked = null;
            if (blueprint != null) return blueprint.TryPick(screenPoint, out picked);
            return neonScreen != null && neonScreen.TryPick(screenPoint, out picked);
        }
        public Vector2 LabelScreenPosition(WorldMapModule module) => blueprint != null ? blueprint.LabelScreenPosition(module)
            : neonScreen != null ? neonScreen.LabelScreenPosition(module) : Vector2.zero;
        public void ShowCompletion()
        {
            if (blueprint != null) blueprint.ShowCompletion();
            else neonScreen?.ShowCompletion();
        }
        public void Hide()
        {
            if (blueprint != null) blueprint.HidePresentation();
            blueprint = null;
            if (neonScreen != null) neonScreen.HidePresentation();
            neonScreen = null;
        }
        private void OnDisable() => Hide();
    }
}
