using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    // Scene-view preset navigation and explicit alignment of the main scene camera.
    public static class DebugCameraPresetShortcuts
    {
        private static WorldMapRoot currentWorld;
        private static LevelRegion currentRegion;
        private static GameObject lastSelection;

        [Shortcut("TapTap/Debug Camera/Previous Preset", KeyCode.LeftBracket)]
        [MenuItem("Tools/TapTap/调试镜头/上一子关卡预设")]
        private static void Previous() => Navigate(-1);

        [Shortcut("TapTap/Debug Camera/Next Preset", KeyCode.RightBracket)]
        [MenuItem("Tools/TapTap/调试镜头/下一子关卡预设")]
        private static void Next() => Navigate(1);

        [Shortcut("TapTap/Debug Camera/Align Main Camera With View", typeof(SceneView), KeyCode.Quote)]
        [MenuItem("Tools/TapTap/调试镜头/将 MainCamera 对齐当前视角")]
        private static void AlignMainCameraWithView()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorGUIUtility.editingTextField) return;
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null) return;
            Camera mainCamera = FindMainCamera();
            if (mainCamera == null)
            {
                sceneView.ShowNotification(new GUIContent("请先打开包含 MainCamera 标签相机的场景。"));
                return;
            }

            Transform target = mainCamera.transform;
            Undo.RecordObject(target, "将 MainCamera 对齐当前视角");
            target.SetPositionAndRotation(sceneView.camera.transform.position, sceneView.camera.transform.rotation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            EditorSceneManager.MarkSceneDirty(mainCamera.gameObject.scene);
            sceneView.ShowNotification(new GUIContent("MainCamera 已对齐当前视角（可 Ctrl+Z 撤销）"));
            sceneView.Repaint();
        }

        private static Camera FindMainCamera()
        {
            Camera fallback = null;
            Scene activeScene = SceneManager.GetActiveScene();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                    {
                        if (!camera.CompareTag("MainCamera")) continue;
                        if (scene == activeScene) return camera;
                        if (fallback == null) fallback = camera;
                    }
            }
            return fallback;
        }

        private static void Navigate(int direction)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null) sceneView = EditorWindow.GetWindow<SceneView>();
            GameObject selected = Selection.activeGameObject;
            LevelRegion selectedRegion = selected != null ? selected.GetComponentInParent<LevelRegion>() : null;
            WorldMapRoot selectedWorld = selected != null ? selected.GetComponentInParent<WorldMapRoot>() : null;
            WorldMapRoot world = IsLoaded(selectedWorld) ? selectedWorld : IsLoaded(currentWorld) ? currentWorld : FindWorld();
            if (world == null || world.Source == null)
            {
                sceneView.ShowNotification(new GUIContent("请先打开包含大世界的场景。"));
                return;
            }

            var modules = world.Source.OrderedModules();
            LevelRegion anchor = currentWorld == world ? currentRegion : null;
            if (selected != lastSelection || currentWorld != world || anchor == null)
                if (selectedRegion != null && selectedWorld == world) anchor = selectedRegion;
            int index = anchor != null ? modules.FindIndex(module => world.FindRegion(module) == anchor) : -1;
            if (index < 0)
            {
                // Start from the preset closest to the Scene camera when no region was selected.
                float nearest = float.PositiveInfinity;
                for (int i = 0; i < modules.Count; i++)
                {
                    LevelRegion region = world.FindRegion(modules[i]);
                    if (region == null || !region.gameObject.activeInHierarchy) continue;
                    var shot = LevelCameraShot.Evaluate(region, null, sceneView.camera.aspect);
                    float distance = (shot.Position - sceneView.camera.transform.position).sqrMagnitude;
                    if (distance < nearest) { nearest = distance; index = i; }
                }
            }
            currentWorld = world;
            lastSelection = selected;
            if (index < 0) return;
            currentRegion = world.FindRegion(modules[index]);
            for (int next = index + direction; next >= 0 && next < modules.Count; next += direction)
            {
                LevelRegion region = world.FindRegion(modules[next]);
                if (region == null || !region.gameObject.activeInHierarchy) continue;
                var shot = LevelCameraShot.Evaluate(region, null, sceneView.camera.aspect);
                sceneView.in2DMode = false;
                sceneView.orthographic = false;
                sceneView.cameraSettings.fieldOfView = shot.FieldOfView;
                sceneView.camera.fieldOfView = shot.FieldOfView;
                var target = new GameObject("Temporary camera preset alignment") { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    target.transform.SetPositionAndRotation(shot.Position, shot.Rotation);
                    sceneView.AlignViewToObject(target.transform);
                }
                finally { Object.DestroyImmediate(target); }
                currentRegion = region;
                sceneView.ShowNotification(new GUIContent("镜头 " + world.Source.SequenceNumber(modules[next])
                    + " · " + modules[next].DisplayName));
                sceneView.Repaint();
                return;
            }
            sceneView.ShowNotification(new GUIContent(direction < 0 ? "已到第一个镜头预设" : "已到最后一个镜头预设"));
        }

        private static bool IsLoaded(WorldMapRoot world) => world != null && world.gameObject.scene.IsValid()
            && world.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(world)
            && !EditorSceneManager.IsPreviewScene(world.gameObject.scene);

        private static WorldMapRoot FindWorld()
        {
            WorldMapRoot fallback = null;
            foreach (WorldMapRoot world in Object.FindObjectsOfType<WorldMapRoot>())
            {
                if (!IsLoaded(world)) continue;
                if (world.gameObject.scene == SceneManager.GetActiveScene()) return world;
                if (fallback == null) fallback = world;
            }
            return fallback;
        }
    }
}
