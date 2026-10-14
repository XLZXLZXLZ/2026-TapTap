using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class WorldMapSceneBuilder
    {
        public static List<string> Validate(WorldMapDefinition map)
        {
            List<string> errors = WorldMapPrefabBuilder.Validate(map);
            if (map?.Modules == null) return errors;
            foreach (WorldMapModule module in map.Modules)
            {
                if (module?.Level == null) continue;
                LevelPlacement entry = module.Level.EntryPlacement;
                if (entry?.Brush == null || entry.Brush.HandlerId != "checkpoint")
                    errors.Add(module.DisplayName + "：请在单关编辑器指定入口复活点。");
                if (!module.Level.Placements.Exists(placement => placement?.Brush?.HandlerId == "endpoint"))
                    errors.Add(module.DisplayName + "：缺少终点。");
            }
            return errors;
        }

        public static string Generate(WorldMapDefinition map, bool openScene = true)
        {
            if (openScene && !Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
            string path = WorldMapSceneSync.Synchronize(map);
            WorldMapMenuSceneBuilder.SaveSelection(map);
            if (openScene && !Application.isBatchMode)
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.SaveScene(scene, path);
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                SceneManager.SetActiveScene(scene);
            }
            return path;
        }
    }
}
