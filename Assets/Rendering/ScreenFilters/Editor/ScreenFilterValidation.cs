using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ScreenFilters.Editor
{
    public static class ScreenFilterValidation
    {
        private const string Output = "Logs/ScreenFilters";
        private const string PlayKey = "ScreenFilters.Validation.Play";

        [MenuItem("Tools/Rendering/验证屏幕滤镜")]
        public static void Validate()
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/results.txt", "Unity " + Application.unityVersion + "\n");
            ValidateImages();
            ValidateCameraState();
            ValidatePreviewRenderState();
            Pass("Workbench draft parameters are editable; preview restores render target and sRGB state");
            Debug.Log("Screen filters: validation passed. See " + Output);
        }

        public static void RunPreviewBatch()
        {
            Directory.CreateDirectory(Output);
            try
            {
                ValidatePreviewRenderState();
                File.WriteAllText(Output + "/preview-results.txt", "PASS: Workbench integer/float parameters are editable and retain changes; preview restores render target and sRGB state.\n");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                File.WriteAllText(Output + "/preview-results.txt", "FAIL: " + error + "\n");
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static void ValidatePreviewRenderState()
        {
            var window = ScriptableObject.CreateInstance<ScreenFilterPreviewWindow>();
            RenderTexture previous = RenderTexture.active;
            bool previousSRGB = GL.sRGBWrite;
            var editorTarget = RenderTexture.GetTemporary(32, 24, 0);
            try
            {
                var windowState = new SerializedObject(window);
                var draft = (ScreenFilterProfile)windowState.FindProperty("draft").objectReferenceValue;
                Check((draft.hideFlags & HideFlags.NotEditable) == 0, "Preview draft is marked read-only");
                var parameters = new SerializedObject(draft);
                SerializedProperty pixelSize = parameters.FindProperty("Pixelation.Size");
                SerializedProperty blockSize = parameters.FindProperty("Brush.BlockSize");
                Check(pixelSize.editable && blockSize.editable, "Workbench parameter controls are read-only");
                pixelSize.intValue = 7;
                blockSize.floatValue = 18.5f;
                parameters.ApplyModifiedPropertiesWithoutUndo();
                parameters.Update();
                Check(pixelSize.intValue == 7 && Mathf.Approximately(blockSize.floatValue, 18.5f)
                    && draft.Pixelation.Size == 7 && Mathf.Approximately(draft.Brush.BlockSize, 18.5f),
                    "Workbench parameter changes did not persist");
                RenderTexture.active = editorTarget;
                GL.sRGBWrite = !previousSRGB;
                window.RenderPreview();
                Check(RenderTexture.active == editorTarget, "Preview changed the editor render target");
                Check(GL.sRGBWrite == !previousSRGB, "Preview changed the editor sRGB write state");
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSRGB;
                UnityEngine.Object.DestroyImmediate(window);
                RenderTexture.ReleaseTemporary(editorTarget);
            }
        }

        public static void RunBatch()
        {
            try
            {
                Validate();
                ValidateIntegration();
                SessionState.SetBool(PlayKey, true);
                SessionState.SetFloat(PlayKey + ".Start", (float)EditorApplication.timeSinceStartup);
                SubscribePlay();
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Fail(error); }
        }

        [InitializeOnLoadMethod]
        private static void SubscribePlay()
        {
            if (!SessionState.GetBool(PlayKey, false)) return;
            EditorApplication.update -= TickPlay;
            EditorApplication.update += TickPlay;
        }

        private static void TickPlay()
        {
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(PlayKey + ".Start", 0f) > 120d)
            {
                EditorApplication.update -= TickPlay;
                Fail(new Exception("Play validation timed out"));
                return;
            }
            if (!EditorApplication.isPlaying || Time.frameCount < 12) return;
            EditorApplication.update -= TickPlay;
            SessionState.SetBool(PlayKey, false);
            try
            {
                ValidateGameplayCamera("Play");
                Pass("Actual Play mode camera rendering and shared profile binding");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Fail(error); }
        }

        private static void ValidateImages()
        {
            var shader = Resources.Load<Shader>(ScreenFilterRenderer.ShaderResource);
            Check(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Shader import/support");
            var profile = ScriptableObject.CreateInstance<ScreenFilterProfile>();
            using (var source = new FilterPreviewSource())
            using (var renderer = new ScreenFilterRenderer())
            {
                try
                {
                    Color32[] baseline = Render(renderer, source.Chart, profile, 641, 359, 0.05f, "Original");
                    for (int i = 0; i < 5; i++)
                    {
                        FilterProfileGUI.ApplyPreset(profile, i + 1);
                        Color32[] changed = Render(renderer, source.Chart, profile, 641, 359, 0.05f, FilterProfileGUI.Names[i]);
                        Check(Difference(baseline, changed) > 100, "Filter changes image: " + (ScreenFilterKind)i);
                        Color32[] repeat = Render(renderer, source.Chart, profile, 641, 359, 0.05f);
                        Check(Difference(changed, repeat) == 0, "Deterministic fixed clock/seed: " + i);
                        profile.GetSettings((ScreenFilterKind)i).Strength = 0f;
                        Color32[] zero = Render(renderer, source.Chart, profile, 641, 359, 0.05f);
                        Check(Difference(baseline, zero) == 0, "Zero strength identity: " + i);
                    }
                    Pass("Five filters alter the image; fixed time/seed is deterministic; zero strength is identity");

                    FilterProfileGUI.ApplyPreset(profile, 1);
                    profile.Pixelation.Size = 1;
                    Check(Difference(baseline, Render(renderer, source.Chart, profile, 641, 359, 0f)) == 0, "Pixel size 1 identity");
                    foreach (int size in new[] { 4, 7, 8 })
                    {
                        profile.Pixelation.Size = size;
                        Color32[] pixels = Render(renderer, source.Chart, profile, 641, 359, 0f);
                        for (int y = 0; y < 359; y++)
                        for (int x = 0; x < 641; x++)
                            Check(pixels[y * 641 + x].Equals(pixels[(y / size * size) * 641 + x / size * size]), "Exact pixel block at odd resolution");
                    }
                    Pass("Pixel size 1 identity and uniform 4/7/8 pixel blocks at 641x359");
                    ValidateLegacyPixel(renderer, source.Chart, profile);

                    FilterProfileGUI.ApplyPreset(profile, 2);
                    Color32[] seedA = Render(renderer, source.Chart, profile, 641, 359, 0.05f);
                    profile.Seed++;
                    Check(Difference(seedA, Render(renderer, source.Chart, profile, 641, 359, 0.05f)) > 0, "Seed controls brush pattern");
                    Check(Difference(seedA, Render(renderer, source.Chart, profile, 641, 359, 2f)) > 0, "Animated brush");
                    profile.Brush.MotionSpeed = 0f;
                    Check(Difference(Render(renderer, source.Chart, profile, 641, 359, 0f),
                        Render(renderer, source.Chart, profile, 641, 359, 2f)) == 0, "Static brush when speed is zero");

                    FilterProfileGUI.ApplyPreset(profile, 1);
                    profile.CRT.Enabled = true;
                    Color32[] first = Render(renderer, source.Chart, profile, 641, 359, 0.05f);
                    profile.Order.Reverse();
                    Check(Difference(first, Render(renderer, source.Chart, profile, 641, 359, 0.05f)) > 0, "Order affects composition");
                    foreach (ScreenFilterKind kind in Enum.GetValues(typeof(ScreenFilterKind))) profile.GetSettings(kind).Enabled = true;
                    Render(renderer, source.Chart, profile, 960, 600, 0.05f, "Combined");
                    Render(renderer, source.Chart, profile, 853, 480, 0.12f);
                    profile.Enabled = false;
                    Check(Difference(baseline, Render(renderer, source.Chart, profile, 641, 359, 0.05f)) == 0, "Master switch identity");
                    profile.Order.Add(ScreenFilterKind.CRT); profile.Order.Add((ScreenFilterKind)99); profile.Normalize();
                    Check(profile.Order.Count == 5, "Order normalized without duplicate effects");
                    Check(!ShaderUtil.ShaderHasError(shader), "All shader passes compile");
                    Pass("Seed, animation freeze, arbitrary ordering, all-filter composition, resizing and master bypass");
                }
                finally { Undo.ClearUndo(profile); UnityEngine.Object.DestroyImmediate(profile); }
            }
        }

        private static void ValidateLegacyPixel(ScreenFilterRenderer renderer, Texture source, ScreenFilterProfile profile)
        {
            // Optional compatibility check: the standalone module also works without this legacy asset.
            Shader legacy = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ArtStyleTest/Shaders/CrispPixelation.shader");
            if (legacy == null) return;
            var material = new Material(legacy);
            var raw = RenderTexture.GetTemporary(641, 359, 0, RenderTextureFormat.ARGB32);
            var target = RenderTexture.GetTemporary(641, 359, 0, RenderTextureFormat.ARGB32);
            try
            {
                profile.Pixelation.Size = 7;
                material.SetFloat("_PixelSize", 7f);
                Graphics.Blit(source, raw); Graphics.Blit(raw, target, material);
                Color32[] before = Read(target, null);
                Color32[] after = Render(renderer, source, profile, 641, 359, 0f);
                for (int i = 0; i < before.Length; i++)
                    Check(Mathf.Abs(before[i].r - after[i].r) <= 1 && Mathf.Abs(before[i].g - after[i].g) <= 1
                        && Mathf.Abs(before[i].b - after[i].b) <= 1, "Legacy pixel shader compatibility");
                Pass("New pixel shader matches the legacy shader within one channel value");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
                RenderTexture.ReleaseTemporary(raw); RenderTexture.ReleaseTemporary(target);
            }
        }

        private static void ValidateCameraState()
        {
            var owner = new GameObject("Filter Camera Validation");
            var profile = ScriptableObject.CreateInstance<ScreenFilterProfile>();
            try
            {
                Camera camera = owner.AddComponent<Camera>(); camera.allowMSAA = true;
                var binding = owner.AddComponent<ScreenFilterCamera>(); binding.SetProfile(profile);
                Check(camera.allowMSAA, "Non-pixel effects preserve MSAA");
                profile.Brush.Enabled = true; binding.SetProfile(profile);
                Check(camera.allowMSAA, "Brush preserves MSAA");
                profile.Pixelation.Enabled = true; binding.SetProfile(profile);
                Check(!camera.allowMSAA, "Pixel disables MSAA");
                profile.Pixelation.Enabled = false; binding.SetProfile(profile);
                Check(camera.allowMSAA, "Pixel off restores MSAA");
                profile.Pixelation.Enabled = true; binding.SetProfile(profile); binding.enabled = false;
                Check(camera.allowMSAA, "Component disable restores MSAA");
                Pass("MSAA ownership is limited to active pixelation and restored on disable");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(profile); }
        }

        private static void ValidateIntegration()
        {
            var settings = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Rendering/Settings/TapTapRenderer.asset");
            int rewind = -1, filters = -1;
            for (int i = 0; i < settings.rendererFeatures.Count; i++)
            {
                Check(settings.rendererFeatures[i] != null, "Renderer feature reference survives import");
                if (settings.rendererFeatures[i].GetType().Name == "TapTapScreenEffectsFeature") rewind = i;
                if (settings.rendererFeatures[i] is ScreenFiltersFeature) filters = i;
            }
            Check(rewind >= 0 && filters > rewind, "Rewind renders before generic filters");
            var profile = AssetDatabase.LoadAssetAtPath<ScreenFilterProfile>(ScreenFilterPreviewWindow.DefaultProfilePath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Runtime/GameplayRuntime.prefab");
            Check(profile != null && prefab.GetComponentInChildren<ScreenFilterCamera>(true)?.Profile == profile, "Runtime prefab shared profile");
            EditorSceneManager.OpenScene("Assets/Scenes/WorldMaps/World_Character_Play.unity");
            ValidateGameplayCamera("Edit");
            Pass("Renderer feature ordering and gameplay prefab/scene binding");
        }

        private static void ValidateGameplayCamera(string mode)
        {
            Camera camera = Camera.main;
            Check(camera != null, "Gameplay camera exists");
            var binding = camera.GetComponent<ScreenFilterCamera>();
            Check(binding != null && binding.Profile != null, "Gameplay camera inherits shared profile");
            ScreenFilterProfile original = binding.Profile;
            var working = UnityEngine.Object.Instantiate(original);
            try
            {
                working.Enabled = false; binding.SetProfile(working);
                Color32[] baseline = Capture(camera, 641, 359, mode + "_Gameplay_Original");
                FilterProfileGUI.ApplyPreset(working, 1); working.Pixelation.Size = 7; binding.SetProfile(working);
                Color32[] result = Capture(camera, 641, 359, mode + "_Gameplay_Pixel");
                Check(Difference(baseline, result) > 100, "Gameplay feature applies pixelation");
                for (int y = 0; y < 359; y++)
                for (int x = 0; x < 641; x++)
                    Check(result[y * 641 + x].Equals(result[(y / 7 * 7) * 641 + x / 7 * 7]), "Gameplay exact pixel blocks");
                for (int i = 1; i < 5; i++)
                {
                    FilterProfileGUI.ApplyPreset(working, i + 1); binding.SetProfile(working);
                    working.Tear.Frequency = 0f;
                    Capture(camera, 960, 600, mode + "_Gameplay_" + (ScreenFilterKind)i);
                }
                Vector3 position = camera.transform.position;
                Quaternion rotation = camera.transform.rotation;
                RenderTexture originalTarget = camera.targetTexture;
                bool msaa = camera.allowMSAA;
                var previewTarget = RenderTexture.GetTemporary(640, 400, 24);
                try
                {
                    using (var preview = new FilterPreviewSource()) preview.RenderScene(camera, previewTarget);
                    Check(camera.transform.position == position && camera.transform.rotation == rotation
                        && camera.targetTexture == originalTarget && camera.allowMSAA == msaa,
                        "Preview leaves source camera unchanged");
                }
                finally { RenderTexture.ReleaseTemporary(previewTarget); }
            }
            finally
            {
                binding.SetProfile(original);
                Undo.ClearUndo(working);
                UnityEngine.Object.DestroyImmediate(working);
            }
        }

        private static Color32[] Render(ScreenFilterRenderer renderer, Texture input, ScreenFilterProfile profile,
            int width, int height, float clock, string save = null)
        {
            var source = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var commands = CommandBufferPool.Get("Filter validation");
            try
            {
                Graphics.Blit(input, source);
                renderer.Record(commands, source, target, target.descriptor, profile, clock);
                Graphics.ExecuteCommandBuffer(commands);
                return Read(target, save);
            }
            finally
            {
                CommandBufferPool.Release(commands);
                RenderTexture.ReleaseTemporary(source); RenderTexture.ReleaseTemporary(target);
            }
        }

        private static Color32[] Capture(Camera camera, int width, int height, string save)
        {
            var previous = camera.targetTexture;
            var texture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            try { camera.targetTexture = texture; camera.Render(); return Read(texture, save); }
            finally { camera.targetTexture = previous; RenderTexture.ReleaseTemporary(texture); }
        }

        private static Color32[] Read(RenderTexture texture, string save)
        {
            RenderTexture previous = RenderTexture.active;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
                if (save != null) File.WriteAllBytes(Output + "/" + save + ".png", image.EncodeToPNG());
                return image.GetPixels32();
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
        }

        private static int Difference(Color32[] a, Color32[] b)
        {
            int changed = 0;
            for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) changed++;
            return changed;
        }

        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Pass(string message) => File.AppendAllText(Output + "/results.txt", "PASS: " + message + "\n");
        private static void Fail(Exception error)
        {
            SessionState.SetBool(PlayKey, false);
            File.AppendAllText(Output + "/results.txt", "FAIL: " + error + "\n");
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }
}
