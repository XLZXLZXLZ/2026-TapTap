using System;
using System.IO;
using System.Linq;
using ScreenFilters;
using TapTap.Art.LocalFog;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class LocalFogDepthValidation
{
    private const string Output = "Logs/SceneRendering";

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated project.");
        Directory.CreateDirectory(Output);
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/WorldMaps/World_Character_Play.unity");
            Camera camera = Camera.main;
            var data = camera.GetUniversalAdditionalCameraData();
            var binding = camera.GetComponent<ScreenFilterCamera>();
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Rendering/Settings/TapTapRenderer.asset");
            var feature = renderer.rendererFeatures.OfType<LocalFogDepthFeature>().Single();
            Require(LocalFogVolume.HasVisibleVolumes(camera), "Saved camera has no visible fog");
            Require(renderer.copyDepthMode == CopyDepthMode.AfterOpaques, "Test must use the normal depth-copy renderer setting");
            binding.enabled = false;
            data.renderPostProcessing = false;
            camera.cameraType = CameraType.SceneView;
            Color32[] reference = Capture(camera, 720, 400, "Reference");
            camera.cameraType = CameraType.Game;
            feature.SetActive(false);
            Color32[] before = Capture(camera, 720, 400, "Before_Unfiltered");
            float beforeError = Difference(reference, before);
            Require(beforeError > 2, "Original Scene/Game fog discrepancy did not reproduce: " + beforeError);
            feature.SetActive(true);
            float maximumError = 0;
            foreach (bool msaa in new[] { true, false })
            {
                camera.allowMSAA = msaa;
                foreach (Vector2Int size in new[] { new Vector2Int(720, 400), new Vector2Int(853, 480) })
                {
                    camera.cameraType = CameraType.SceneView;
                    reference = Capture(camera, size.x, size.y, null);
                    camera.cameraType = CameraType.Game;
                    for (int frame = 0; frame < 3; frame++)
                    {
                        Color32[] actual = Capture(camera, size.x, size.y, frame == 0 ? "After_" + size.x + "_MSAA_" + msaa : null);
                        float error = Difference(reference, actual);
                        maximumError = Mathf.Max(maximumError, error);
                        Require(error < 0.1f, "Scene/Game depth differs after fix: " + error);
                    }
                }
            }
            // Retain the authored brush style and Bloom in the delivered before/after.
            camera.allowMSAA = true;
            binding.enabled = true;
            data.renderPostProcessing = true;
            feature.SetActive(false);
            Capture(camera, 1180, 640, "Before");
            feature.SetActive(true);
            Capture(camera, 1180, 640, "After");
            // No depth prepass is requested once the fog is inactive or outside the view.
            Vector3 position = camera.transform.position;
            camera.transform.position += Vector3.right * 10000;
            Require(!LocalFogVolume.HasVisibleVolumes(camera), "Offscreen fog requests a prepass");
            camera.transform.position = position;
            foreach (var fog in Object.FindObjectsOfType<LocalFogVolume>()) fog.enabled = false;
            Require(!LocalFogVolume.HasVisibleVolumes(camera), "Disabled fog requests a prepass");
            File.WriteAllText(Output + "/results.txt", "Unity " + Application.unityVersion
                + "\nPASS: Reproduced original fog discrepancy, mean RGB error " + beforeError.ToString("F4")
                + ".\nPASS: Scene/Game fog agrees across MSAA on/off, two resolutions and three consecutive renders; maximum RGB error " + maximumError.ToString("F4")
                + ".\nPASS: Authored brush and post processing retained in before/after images.\nPASS: Offscreen/disabled fog does not request a prepass.\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Output + "/results.txt", "FAIL: " + error);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static Color32[] Capture(Camera camera, int width, int height, string name)
    {
        var target = new RenderTexture(width, height, 24);
        var previous = RenderTexture.active;
        var previousTarget = camera.targetTexture;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            if (name != null) File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previous;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
        }
    }

    private static float Difference(Color32[] a, Color32[] b)
    {
        double total = 0;
        for (int i = 0; i < a.Length; i++) total += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
        return (float)(total / (a.Length * 3));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
