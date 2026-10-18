using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class WorldMapSceneBuilder
    {
        public static List<string> Validate(WorldMapDefinition map) => WorldMapValidation.Validate(map);

        public static string Generate(WorldMapDefinition map, bool openScene = true)
        {
            if (openScene && !Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
            string path = WorldMapSceneSync.Synchronize(map);
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
