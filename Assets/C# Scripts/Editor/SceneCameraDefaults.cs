using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    [InitializeOnLoad]
    public static class SceneCameraDefaults
    {
        public const float Size = 9f;

        static SceneCameraDefaults()
        {
            EditorSceneManager.newSceneCreated += OnNewSceneCreated;
        }

        private static void OnNewSceneCreated(Scene scene, NewSceneSetup setup, NewSceneMode mode)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                Apply(root);
        }

        public static void Apply(GameObject root)
        {
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
            {
                camera.orthographicSize = Size;
                EditorUtility.SetDirty(camera);
                if (PrefabUtility.IsPartOfPrefabInstance(camera))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
            }
        }
    }
}
