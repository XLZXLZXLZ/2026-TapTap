using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class GameplayLightingDefaults
    {
        // Explicit ambient fill survives runtime scene loading without baked sky probes.
        public static readonly Color AmbientColor = new Color(0.4f, 0.4f, 0.4f);

        public static void Apply(Scene scene)
        {
            Scene previous = SceneManager.GetActiveScene();
            try
            {
                SceneManager.SetActiveScene(scene);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = AmbientColor;
                EditorSceneManager.MarkSceneDirty(scene);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
    }
}
