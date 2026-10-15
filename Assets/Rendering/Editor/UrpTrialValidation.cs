using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TapTap;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class UrpTrialValidation
{
    private const string Output = "Logs/URPTrial";
    private static readonly List<string> results = new List<string>();
    private const string PlayKey = "TapTap.URPTrial.PlayValidation";
    private static int playStage;
    private static double playDeadline;

    [InitializeOnLoadMethod]
    private static void SubscribePlay()
    {
        if (!SessionState.GetBool(PlayKey, false)) return;
        playDeadline = EditorApplication.timeSinceStartup + 120d;
        EditorApplication.update -= TickPlay;
        EditorApplication.update += TickPlay;
    }

    public static void RunPlayBatch()
    {
        Directory.CreateDirectory(Output);
        EditorSceneManager.OpenScene("Assets/Scenes/WorldMaps/World_Character_Select.unity");
        File.WriteAllText(Output + "/play-results.txt", "Unity " + Application.unityVersion + "\n");
        SessionState.SetBool(PlayKey, true);
        SubscribePlay();
        EditorApplication.EnterPlaymode();
    }

    private static void TickPlay()
    {
        if (!SessionState.GetBool(PlayKey, false) || !EditorApplication.isPlaying || Time.frameCount < 10) return;
        try
        {
            Require(EditorApplication.timeSinceStartup < playDeadline, "Play validation timed out at stage " + playStage);
            if (playStage == 0)
            {
                var menu = WorldLevelMenu.Current;
                if (menu == null || menu.View == null || menu.Selected == null) return;
                var texture = (RenderTexture)typeof(WorldMapAssemblyView).GetField("texture", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(menu.View);
                Require(texture != null, "Menu preview texture missing");
                var previous = RenderTexture.active;
                var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                try
                {
                    RenderTexture.active = texture;
                    image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                    image.Apply();
                    var pixels = image.GetPixels32();
                    Require(pixels.Any(p => !p.Equals(pixels[0])), "Menu map preview blank");
                    File.WriteAllBytes(Output + "/World_Character_Select_preview.png", image.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
                File.AppendAllText(Output + "/play-results.txt", "PASS: Actual Play menu selected a module and rendered its map preview through URP.\n");
                Require(menu.EnterSelected(), "Cannot enter selected module");
                playStage = 1;
                return;
            }
            if (WorldMapSceneRouter.IsLoading || WorldLevelMenu.IsOpen) return;
            var player = UnityEngine.Object.FindObjectOfType<PlayerController>();
            if (player == null || Camera.main == null) return;
            Require(player.Body != null && player.Head != null, "Player bodies missing");
            var gameplay = Capture(Camera.main, 1024, 640);
            try
            {
                var pixels = gameplay.GetPixels32();
                Require(pixels.Any(p => !p.Equals(pixels[0])), "Playable scene blank");
                Require(pixels.Count(p => p.r > 230 && p.b > 230 && p.g < 25) < pixels.Length / 1000,
                    "Playable scene contains pink error materials");
                File.WriteAllBytes(Output + "/World_Character_Play_runtime.png", gameplay.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(gameplay); }
            File.AppendAllText(Output + "/play-results.txt", "PASS: Menu entered the authored play scene, player initialized, and gameplay camera rendered through URP.\n");
            SessionState.SetBool(PlayKey, false);
            EditorApplication.update -= TickPlay;
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            SessionState.SetBool(PlayKey, false);
            EditorApplication.update -= TickPlay;
            File.AppendAllText(Output + "/play-results.txt", "FAIL: " + error + "\n");
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    public static void RunBatch()
    {
        Directory.CreateDirectory(Output);
        results.Clear();
        try
        {
            Require(GraphicsSettings.renderPipelineAsset is UniversalRenderPipelineAsset, "Graphics pipeline missing");
            int quality = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                Require(QualitySettings.renderPipeline is UniversalRenderPipelineAsset, "Quality pipeline missing: " + i);
            }
            QualitySettings.SetQualityLevel(quality, false);
            results.Add("PASS: Graphics and all six quality levels use URP.");
            foreach (string path in new[]
            {
                "Assets/Scenes/WorldMaps/World_Character_Play.unity",
                "Assets/Scenes/Test/Demo_Hanging_Test.unity"
            })
            {
                var scene = EditorSceneManager.OpenScene(path);
                var camera = UnityEngine.Object.FindObjectsOfType<Camera>().FirstOrDefault(c => c.CompareTag("MainCamera"));
                if (camera == null) camera = UnityEngine.Object.FindObjectOfType<Camera>();
                Require(camera != null, "Scene has no camera: " + path);
                var image = Capture(camera, 1024, 640);
                try
                {
                    var pixels = image.GetPixels32();
                    int pink = pixels.Count(p => p.r > 230 && p.b > 230 && p.g < 25);
                    Require(pink < pixels.Length / 1000, "Pink pixels in " + path + ": " + pink);
                    Require(pixels.Any(p => !p.Equals(pixels[0])), "Blank render: " + path);
                    File.WriteAllBytes(Output + "/" + scene.name + ".png", image.EncodeToPNG());
                    results.Add("PASS: URP camera rendered " + scene.name + "; pink pixels " + pink + ".");
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            CheckRegionFade();
            int materials = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
            {
                if (!(asset is Material material)) continue;
                Require(material.shader != null && material.shader.isSupported, "Unsupported material: " + material.name);
                Require(!ShaderUtil.ShaderHasError(material.shader), "Shader error: " + material.shader.name);
                Require(material.shader.name != "Standard" && !material.shader.name.StartsWith("Unlit/"),
                    "Unconverted material: " + material.name);
                materials++;
            }
            results.Add("PASS: " + materials + " material assets supported; no Standard/legacy Unlit materials remain.");
            File.WriteAllLines(Output + "/edit-results.txt", results);
            Debug.Log("URP_TRIAL_VALIDATION_OK");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            results.Add("FAIL: " + error);
            File.WriteAllLines(Output + "/edit-results.txt", results);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void CheckRegionFade()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Fade camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 1f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        var texture = new Texture2D(2, 2);
        texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f, 2f);
        var renderer = new GameObject("Fade sprite").AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        var material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
        renderer.sharedMaterial = material;
        renderer.color = new Color(1f, 0.3f, 0.1f, 1f);
        var reveal = renderer.gameObject.AddComponent<RegionRevealView>();
        Color Sample()
        {
            var image = Capture(camera, 128, 128);
            Color color = image.GetPixel(64, 64);
            UnityEngine.Object.DestroyImmediate(image);
            return color;
        }
        try
        {
            Color full = Sample();
            reveal.SetDiscovered(false, true);
            Color hidden = Sample();
            reveal.SetDiscovered(true);
            Color visible = Sample();
            Require(full.r > 0.8f && full.r > full.g && full.g > full.b, "Sprite tint lost");
            Require(hidden.r < 0.02f, "Undiscovered region still visible");
            Require(Mathf.Abs(visible.r - full.r) < 0.01f && visible.r > visible.g && visible.g > visible.b,
                "Reveal lost opacity/texture/tint: " + visible);
            reveal.SetDiscovered(true, true);
            Color restored = Sample();
            Require(Mathf.Abs(restored.r - full.r) < 0.01f && renderer.sharedMaterial == material, "Original material not restored");
            results.Add("PASS: Instant region reveal preserves sprite tint, texture and material; undiscovered sprites are hidden.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(renderer.gameObject);
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static Texture2D Capture(Camera camera, int width, int height)
    {
        var target = new RenderTexture(width, height, 24);
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            Require(RenderPipelineManager.currentPipeline is UniversalRenderPipeline, "Camera did not render through URP");
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            return image;
        }
        catch { UnityEngine.Object.DestroyImmediate(image); throw; }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    public static void BuildBatch()
    {
        Directory.CreateDirectory(Output);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = "Builds/URPTrial/TapTap-URP.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        File.WriteAllText(Output + "/build-results.txt", report.summary.result + "\nErrors: "
            + report.summary.totalErrors + "\nWarnings: " + report.summary.totalWarnings + "\n");
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
