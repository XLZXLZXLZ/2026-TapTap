using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    // Editor-only tools: use the selected world asset, the active scene's world, or the main campaign.
    public static class WorldProgressDebug
    {
        private const string UnlockMenu = "TapTap/调试/关卡进度/解锁全部关卡";
        private const string ClearMenu = "TapTap/调试/关卡进度/清除解锁与通关进度";
        private const string DefaultWorldPath = "Assets/Runtime/WorldMaps/World_Character.asset";

        [MenuItem(UnlockMenu, false, 100)]
        public static void UnlockAll()
        {
            if (!CanChangeProgress()) return;
            WorldMapDefinition world = ResolveWorld();
            if (world == null) return;
            var modules = world.OrderedModules();
            foreach (WorldMapModule module in modules) WorldMapProgressStore.Unlock(world, module);
            Refresh(world);
            Debug.Log("已解锁 " + world.name + " 的全部 " + modules.Count + " 个关卡，保留原有通关记录。", world);
        }

        [MenuItem(ClearMenu, false, 101)]
        public static void ClearProgress()
        {
            if (!CanChangeProgress()) return;
            WorldMapDefinition world = ResolveWorld();
            if (world == null) return;
            WorldMapProgressStore.Clear(world);
            Refresh(world);
            Debug.Log("已清除 " + world.name + " 的解锁与通关进度，恢复为仅首关可选。", world);
        }

        [MenuItem(UnlockMenu, true)]
        [MenuItem(ClearMenu, true)]
        private static bool CanChangeProgress()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
                return false;
            if (EditorApplication.isPlaying && (WorldMapSceneRouter.IsLoading || GameStageProgression.InputBlocked
                || WorldMapProgression.Current != null && WorldMapProgression.Current.IsTransitioning)) return false;
            return ResolveWorld() != null;
        }

        private static WorldMapDefinition ResolveWorld()
        {
            if (Selection.activeObject is WorldMapDefinition selected) return selected;
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.isLoaded)
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    var menu = root.GetComponentInChildren<WorldLevelMenu>(true);
                    if (menu != null && menu.World != null) return menu.World;
                    var map = root.GetComponentInChildren<WorldMapRoot>(true);
                    if (map != null && map.Source != null) return map.Source;
                }
            return AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(DefaultWorldPath);
        }

        private static void Refresh(WorldMapDefinition world)
        {
            WorldLevelMenu menu = WorldLevelMenu.Current;
            if (menu != null && menu.World == world) menu.RefreshProgress();
            WorldMapProgression progression = WorldMapProgression.Current;
            if (progression != null && progression.GetComponent<WorldMapRoot>().Source == world)
                progression.RefreshSavedProgress();
            EditorApplication.RepaintHierarchyWindow();
            SceneView.RepaintAll();
        }
    }
}
