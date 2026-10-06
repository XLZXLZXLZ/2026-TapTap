#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TapTap.Editor
{
    public static class PrototypeVerification
    {
        public static void BuildAndVerify()
        {
            PrototypeBuilder.BuildPrototype();
            VerifyExisting();
        }

        public static void VerifyExisting()
        {
            EditorSceneManager.OpenScene(PrototypeBuilder.DemoScenePath);
            PlayerController player = UnityEngine.Object.FindObjectOfType<PlayerController>();
            if (player == null || player.Body == null || player.Head == null || player.Config == null)
                throw new InvalidOperationException("Demo player references are incomplete.");
            string[] prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { PrototypeBuilder.PrefabDirectory });
            if (prefabs.Length < 8) throw new InvalidOperationException("Prototype prefabs are incomplete.");
            foreach (string guid in prefabs)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0)
                        throw new InvalidOperationException("Missing script on prefab " + prefab.name);
                }
            }
            if (!UnityEngine.Object.FindObjectsOfType<WorldSurface>().Any(s => s.Kind == SurfaceKind.OneWay))
                throw new InvalidOperationException("Demo has no one-way platform.");
            if (UnityEngine.Object.FindObjectsOfType<LethalZone>().Length == 0)
                throw new InvalidOperationException("Demo has no lethal terrain.");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/PrototypeAssets.txt", "PASS\nPrefabs: " + prefabs.Length + "\nScene: " + PrototypeBuilder.DemoScenePath);
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Camera camera = UnityEngine.Object.FindObjectOfType<Camera>();
                Capture(camera, new Vector3(0f, 3.5f, -10f), "Logs/PrototypeStart.png");
                Capture(camera, new Vector3(23.5f, 3.5f, -10f), "Logs/PrototypeInteractions.png");
            }
            Debug.Log("TapTap prototype asset verification PASSED.");
        }

        private static void Capture(Camera camera, Vector3 position, string path)
        {
            camera.transform.position = position;
            RenderTexture target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
#endif
