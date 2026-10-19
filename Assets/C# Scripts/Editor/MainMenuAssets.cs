using System;
using System.IO;
using System.Linq;
using ScreenFilters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    public static class MainMenuAssets
    {
        public const string ScenePath = "Assets/Scenes/MainMenu/MainMenu.unity";
        public const string PrefabPath = "Assets/Prefabs/UI/MainMenu.prefab";
        public const string AtlasPath = "Assets/Art/Sprite/UI/MainMenu/微信图片_20261019182128_3164_262.png";
        private const float ArtScale = 1.045f;

        [MenuItem("TapTap/UI/Main Menu/Build Main Menu %&m")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before building the menu.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                throw new InvalidOperationException("Open MainMenu before building; the builder preserves the current camera pose.");

            Directory.CreateDirectory("Logs/MainMenu");
            File.Copy(ScenePath, "Logs/MainMenu/MainMenu-before-build.unity", true);
            SliceAtlas();
            var sprites = AssetDatabase.LoadAllAssetsAtPath(AtlasPath).OfType<Sprite>().ToDictionary(s => s.name);
            GameObject root = new GameObject("Main Menu UI", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(MainMenuController));
            try
            {
                root.layer = 5;
                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10;
                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                // Fit the complete composition on narrow screens; retain its left placement on ultrawide screens.
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                MainMenuController menu = root.GetComponent<MainMenuController>();
                CreateArt(root.transform, sprites["MainMenu_Logo"], "Logo", new Rect(0, 20, 960, 590), false);
                menu.StartButton = CreateArt(root.transform, sprites["MainMenu_Start"], "Start", new Rect(358, 634, 224, 94), true).GetComponent<Button>();
                menu.SettingsButton = CreateArt(root.transform, sprites["MainMenu_Settings"], "Settings", new Rect(368, 750, 214, 93), true).GetComponent<Button>();
                menu.QuitButton = CreateArt(root.transform, sprites["MainMenu_Quit"], "Quit", new Rect(356, 863, 224, 94), true).GetComponent<Button>();
                Button[] buttons = { menu.StartButton, menu.SettingsButton, menu.QuitButton };
                for (int i = 0; i < buttons.Length; i++)
                    buttons[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                        selectOnUp = buttons[(i + 2) % 3], selectOnDown = buttons[(i + 1) % 3] };
                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                foreach (MainMenuController old in Object.FindObjectsOfType<MainMenuController>(true))
                    if (old.gameObject != root && old.gameObject.scene == scene) Object.DestroyImmediate(old.gameObject);
                PrefabUtility.InstantiatePrefab(prefab, scene);
            }
            finally { Object.DestroyImmediate(root); }

            Camera camera = Camera.main;
            if (camera == null || camera.gameObject.scene != scene)
                throw new InvalidOperationException("MainMenu requires its authored MainCamera.");
            if (camera.GetComponent<MainMenuCameraMotion>() == null) camera.gameObject.AddComponent<MainMenuCameraMotion>();
            if (camera.GetComponent<MainMenuBlurCamera>() == null) camera.gameObject.AddComponent<MainMenuBlurCamera>();
            InstallBlurFeature(camera);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            // The executable opens on the main menu; existing relative scene order is retained.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }
                .Concat(EditorBuildSettings.scenes.Where(s => s.path != ScenePath)).ToArray();
            AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/MainMenu/build.txt", "PASS: atlas sliced, editable UI prefab and camera effects saved; MainMenu is the startup scene.\n");
            Debug.Log("Main menu built. Camera pose preserved. TapTap/UI/Main Menu/Validate checks rendering and scene navigation.");
        }

        private static void SliceAtlas()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            var textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(textureSettings);
            importer.spritesheet = new[]
            {
                Slice("MainMenu_Logo", new Rect(0, 20, 960, 590)),
                Slice("MainMenu_Start", new Rect(358, 634, 224, 94)),
                Slice("MainMenu_Settings", new Rect(368, 750, 214, 93)),
                Slice("MainMenu_Quit", new Rect(356, 863, 224, 94))
            };
            importer.SaveAndReimport();
        }

        private static SpriteMetaData Slice(string name, Rect topLeftRect)
        {
            return new SpriteMetaData { name = name, alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f), rect = new Rect(topLeftRect.x, 1080f - topLeftRect.yMax,
                    topLeftRect.width, topLeftRect.height) };
        }

        private static Image CreateArt(Transform parent, Sprite sprite, string name, Rect rect, bool button)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = 5;
            var transform = (RectTransform)go.transform;
            transform.SetParent(parent, false);
            transform.anchorMin = transform.anchorMax = transform.pivot = new Vector2(0f, 1f);
            transform.anchoredPosition = new Vector2(rect.x, -rect.y) * ArtScale;
            transform.sizeDelta = rect.size * ArtScale;
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = button;
            image.preserveAspect = true;
            if (button)
            {
                // Scale around the visual center while preserving the artwork's reference placement.
                transform.pivot = new Vector2(0.5f, 0.5f);
                transform.anchoredPosition += new Vector2(rect.width, -rect.height) * (ArtScale * 0.5f);
                var shadow = go.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
                shadow.effectDistance = new Vector2(0f, -4f);
                Button control = go.AddComponent<Button>();
                control.targetGraphic = image;
                control.transition = Selectable.Transition.None;
                go.AddComponent<UIInteractionScale>().Target = transform;
            }
            return image;
        }

        private static void InstallBlurFeature(Camera camera)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Rendering/ScreenFilters/Shaders/MainMenuBlur.shader");
            if (shader == null) throw new InvalidOperationException("Main menu blur shader is missing.");
            // Install on this camera's renderer in every quality tier, so changing quality keeps the effect.
            int index = new SerializedObject(camera.GetUniversalAdditionalCameraData()).FindProperty("m_RendererIndex").intValue;
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                var serialized = new SerializedObject(pipeline);
                SerializedProperty list = serialized.FindProperty("m_RendererDataList");
                int selected = index < 0 ? serialized.FindProperty("m_DefaultRendererIndex").intValue : index;
                if (selected >= list.arraySize) continue;
                var renderer = list.GetArrayElementAtIndex(selected).objectReferenceValue as ScriptableRendererData;
                if (renderer == null) continue;
                MainMenuBlurFeature feature = renderer.rendererFeatures.OfType<MainMenuBlurFeature>().FirstOrDefault();
                if (feature == null)
                {
                    feature = ScriptableObject.CreateInstance<MainMenuBlurFeature>();
                    feature.name = "Main Menu Diagonal Blur";
                    AssetDatabase.AddObjectToAsset(feature, renderer);
                    renderer.rendererFeatures.Add(feature);
                }
                feature.BlurShader = shader;
                feature.SetActive(true);
                feature.Create();
                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(renderer);
                renderer.SetDirty();
            }
        }
    }
}
