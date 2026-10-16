using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

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
                EnablePostProcessing(camera);
                EditorUtility.SetDirty(camera);
                if (PrefabUtility.IsPartOfPrefabInstance(camera))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
            }
        }

        public static void EnablePostProcessing(Camera camera)
        {
            if (camera == null) return;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            EditorUtility.SetDirty(data);
            if (PrefabUtility.IsPartOfPrefabInstance(data))
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
        }
    }
}
