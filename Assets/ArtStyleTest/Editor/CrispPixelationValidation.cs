using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ArtStyleTest;

namespace ArtStyleTestTools
{
    [InitializeOnLoad]
    public static class CrispPixelationValidation
    {
        const string Root = "Assets/ArtStyleTest/CubeFactoryStudy";
        const string BatchKey = "ArtStyleTest.PixelationValidation.Batch";

        static CrispPixelationValidation()
        {
            if (SessionState.GetBool(BatchKey, false))
                EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("Tools/Art Style Test/Validate Cube Factory Pixelation")]
        public static void Validate()
        {
            // Do not replace an open scene with unsaved edits.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(CubeFactoryDesignBuilder.ScenePath);
            CheckAndRender("Edit");
        }

        public static void RunBatch()
        {
            try
            {
                EditorSceneManager.OpenScene(CubeFactoryDesignBuilder.ScenePath);
                File.WriteAllText(Root + "/PixelationValidation.txt", "Unity " + Application.unityVersion + "\n");
                CheckAndRender("Edit");
                SessionState.SetBool(BatchKey, true);
                EditorApplication.playModeStateChanged += OnPlayMode;
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Fail(error); }
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            SessionState.SetBool(BatchKey, false);
            try
            {
                CheckAndRender("Play");
                File.AppendAllText(Root + "/PixelationValidation.txt", "PASS: actual Play mode camera rendering.\n");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Fail(error); }
        }

        static void Fail(Exception error)
        {
            Debug.LogException(error);
            File.AppendAllText(Root + "/PixelationValidation.txt", "FAIL: " + error + "\n");
            EditorApplication.Exit(1);
        }

        static void CheckAndRender(string mode)
        {
            var camera = Camera.main;
            var effect = camera ? camera.GetComponent<CrispPixelation>() : null;
            if (!effect || !effect.enabled) throw new Exception("Missing enabled pixelation on Main Camera.");
            var shader = Shader.Find("Hidden/ArtStyleTest/CrispPixelation");
            if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new Exception("Pixelation shader unsupported or has compile errors.");
            if (camera.allowMSAA) throw new Exception("Camera MSAA must be off while pixelation is enabled.");

            int originalSize = effect.PixelSize;
            try
            {
                effect.enabled = false;
                var baseline = Render(camera, 1536, 1000);
                effect.enabled = true;
                effect.PixelSize = 1;
                var identity = Render(camera, 1536, 1000);
                var a = baseline.GetPixels32();
                var b = identity.GetPixels32();
                int maxDifference = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    maxDifference = Mathf.Max(maxDifference, Mathf.Abs(a[i].r - b[i].r),
                        Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b));
                }
                UnityEngine.Object.DestroyImmediate(baseline);
                UnityEngine.Object.DestroyImmediate(identity);
                if (maxDifference > 1) throw new Exception("Size 1 changed original image: " + maxDifference);
                foreach (int size in new[] { 4, 8 })
                    CheckBlocks(camera, effect, mode, 1536, 1000, size);
                CheckBlocks(camera, effect, mode, 641, 359, 7);
                effect.PixelSize = 0;
                if (effect.PixelSize != 1) throw new Exception("Minimum pixel size clamp failed.");
                effect.PixelSize = 100;
                if (effect.PixelSize != 64) throw new Exception("Maximum pixel size clamp failed.");
                File.AppendAllText(Root + "/PixelationValidation.txt",
                    mode + ": PASS size 1 identity (max channel delta " + maxDifference
                    + "); exact solid blocks at 4/8/7; odd resolution; runtime size clamping.\n");
            }
            finally { effect.enabled = true; effect.PixelSize = originalSize; }
        }

        static void CheckBlocks(Camera camera, CrispPixelation effect, string mode, int width, int height, int size)
        {
            effect.PixelSize = size;
            var image = Render(camera, width, height);
            try
            {
                var pixels = image.GetPixels32();
                int differences = 0, transitions = 0;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Color32 color = pixels[y * width + x];
                    Color32 anchor = pixels[(y / size * size) * width + x / size * size];
                    if (!color.Equals(anchor)) differences++;
                    if (x > 0 && !color.Equals(pixels[y * width + x - 1])) transitions++;
                }
                if (differences != 0) throw new Exception("Nonuniform pixel blocks: " + differences);
                if (transitions < 100) throw new Exception("Render is blank or has no scene detail.");
                File.WriteAllBytes(Root + "/Preview/Pixelation_" + mode + "_" + size + ".png", image.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }

        static Texture2D Render(Camera camera, int width, int height)
        {
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(width, height, 24) { antiAliasing = 1 };
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                return image;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
