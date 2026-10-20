using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ReusableTextEffects;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    public static class TutorialPromptAssets
    {
        public const string Folder = "Assets/Prefabs/UI/Tutorial";
        public const string ArtFolder = "Assets/Art/UI/Tutorial";
        public const string DemoPath = "Assets/Scenes/Demo/TutorialPrompts_Demo.unity";
        public const string FontPath = "Assets/Fonts/text(SMALL) SDF.asset";
        public static readonly string[] Names = { "01_Hover", "02_Recall", "03_Rewind", "04_BulletTime" };
        public static string PromptPath(int index) => Folder + "/" + Names[index] + ".prefab";

        [MenuItem("Tools/TapTap/UI/Tutorial/Build Assets and Demo")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(ArtFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(DemoPath));
            AssetDatabase.Refresh();
            TMP_SpriteAsset keys = BuildKeys();
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null) throw new InvalidOperationException("缺少 text(SMALL) SDF 字体。");
            const string allText = "按住使头部悬浮再向吸附操作失误时，回溯下放慢时间方便精细 ";
            string newCharacters = new string(allText.Where(character => !font.HasCharacter(character)).Distinct().ToArray());
            if (newCharacters.Length > 0) font.TryAddCharacters(newCharacters);
            string missing = new string(allText.Where(character => !font.HasCharacter(character)).Distinct().ToArray());
            if (missing.Length > 0) throw new InvalidOperationException("指定字体缺少教学字符：" + missing);
            EditorUtility.SetDirty(font);
            Material panel = Graphic.defaultGraphicMaterial;
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                TutorialPromptPresenter presenter = BuildPresenter(preview);
                var views = new TutorialPromptView[4];
                views[1] = BuildPrompt(preview, 1, "再按 ", TutorialPromptKey.Space, " 向头部吸附", TutorialPromptCompletion.Press, font, keys, panel, null);
                views[0] = BuildPrompt(preview, 0, "按住 ", TutorialPromptKey.Space, " 使头部悬浮", TutorialPromptCompletion.ReleaseAfterHold, font, keys, panel, views[1]);
                views[2] = BuildPrompt(preview, 2, "操作失误时，按住 ", TutorialPromptKey.Z, " 回溯", TutorialPromptCompletion.ReleaseAfterHold, font, keys, panel, null);
                views[3] = BuildPrompt(preview, 3, "按下 ", TutorialPromptKey.Control, " 放慢时间，方便精细操作", TutorialPromptCompletion.Press, font, keys, panel, null);
                foreach (int index in new[] { 0, 2, 3 }) BuildTrigger(preview, index, views[index], presenter);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            BuildDemo();
            AssetDatabase.SaveAssets();
            Debug.Log("教学提示：四条提示、三组触发器、播放 Canvas、按键矢量素材与演示场景已生成。");
        }

        private static TutorialPromptPresenter BuildPresenter(Scene scene)
        {
            var root = new GameObject("Tutorial Prompts", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.layer = 5;
            SceneManager.MoveGameObjectToScene(root, scene);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            root.AddComponent<TutorialPromptPresenter>();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/TutorialPresenter.prefab");
            Object.DestroyImmediate(root);
            return saved.GetComponent<TutorialPromptPresenter>();
        }

        private static TutorialPromptView BuildPrompt(Scene scene, int index, string prefix, TutorialPromptKey key,
            string suffix, TutorialPromptCompletion completion, TMP_FontAsset font, TMP_SpriteAsset keys,
            Material panelMaterial, TutorialPromptView next)
        {
            RectTransform root = Rect(Names[index], null, new Vector2(1320f, 198f));
            SceneManager.MoveGameObjectToScene(root.gameObject, scene);
            TutorialPromptView view = root.gameObject.AddComponent<TutorialPromptView>();
            view.Prefix = prefix; view.Key = key; view.Suffix = suffix; view.Completion = completion;
            view.NextPromptPrefab = next;
            view.AnimatedRoot = Rect("Animated Content", root, new Vector2(1320f, 198f));
            RectTransform panel = Rect("Panel", view.AnimatedRoot, new Vector2(1320f, 198f));
            Image image = panel.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Sprite/UI/PausePanel/Panel.png");
            image.material = panelMaterial;
            image.color = new Color(0.025f, 0.04f, 0.065f, 0.92f);
            image.raycastTarget = false;
            view.PanelGroup = panel.gameObject.AddComponent<CanvasGroup>();
            view.PanelGroup.blocksRaycasts = view.PanelGroup.interactable = false;
            RectTransform label = Rect("Prompt Text (TMP)", view.AnimatedRoot, new Vector2(1160f, 108f));
            view.TextGroup = label.gameObject.AddComponent<CanvasGroup>();
            view.TextGroup.blocksRaycasts = view.TextGroup.interactable = false;
            view.Label = label.gameObject.AddComponent<TextMeshProUGUI>();
            view.Label.font = font;
            view.Label.spriteAsset = keys;
            view.Label.fontSize = 42f;
            view.Label.enableAutoSizing = true;
            view.Label.fontSizeMin = 28f;
            view.Label.fontSizeMax = 42f;
            view.Label.alignment = TextAlignmentOptions.Center;
            view.Label.color = new Color(0.94f, 0.98f, 1f);
            view.Label.enableWordWrapping = false;
            view.Label.overflowMode = TextOverflowModes.Overflow;
            view.Label.raycastTarget = false;
            view.Label.text = view.RichText;
            view.Shatter = label.gameObject.AddComponent<TMPTextShatter>();
            view.Shatter.Configure(view.Label);
            var settings = new SerializedObject(view.Shatter);
            settings.FindProperty("durationRange").vector2Value = new Vector2(0.42f, 0.5f);
            settings.FindProperty("launchDirection").vector2Value = Vector2.up;
            settings.FindProperty("launchSpeedRange").vector2Value = new Vector2(260f, 380f);
            settings.FindProperty("gravity").vector2Value = new Vector2(0f, -5000f);
            settings.FindProperty("perpendicularScatter").floatValue = 300f;
            settings.FindProperty("rotationRange").vector2Value = new Vector2(-65f, 65f);
            settings.FindProperty("characterDelay").floatValue = 0.004f;
            settings.FindProperty("endScale").floatValue = 1f;
            settings.FindProperty("fadeStart").floatValue = 1f;
            settings.FindProperty("characterOrder").enumValueIndex = (int)TMPTextShatter.CharacterOrder.Random;
            settings.ApplyModifiedPropertiesWithoutUndo();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root.gameObject, PromptPath(index));
            Object.DestroyImmediate(root.gameObject);
            return saved.GetComponent<TutorialPromptView>();
        }

        private static void BuildTrigger(Scene scene, int index, TutorialPromptView view, TutorialPromptPresenter presenter)
        {
            var root = new GameObject("Tutorial Trigger " + Names[index]);
            SceneManager.MoveGameObjectToScene(root, scene);
            BoxCollider2D box = root.AddComponent<BoxCollider2D>();
            box.size = new Vector2(2f, 3f); box.isTrigger = true;
            TutorialPromptTrigger2D trigger = root.AddComponent<TutorialPromptTrigger2D>();
            trigger.PromptPrefab = view; trigger.PresenterPrefab = presenter;
            PrefabUtility.SaveAsPrefabAsset(root, Folder + "/Trigger_" + Names[index] + ".prefab");
            Object.DestroyImmediate(root);
        }

        private static Material PanelMaterial()
        {
            string path = ArtFolder + "/TutorialPanel.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("TapTap/UI/Tutorial Panel");
            if (shader == null) throw new InvalidOperationException("找不到教学背景 Shader。");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static TMP_SpriteAsset BuildKeys()
        {
            string[] names = { "Space", "Z", "Control" };
            string[] labels = { "SPACE", "Z", "CTRL" };
            int[] widths = { 208, 104, 160 };
            int[] positions = { 0, 216, 328 };
            var atlas = new Texture2D(512, 128, TextureFormat.RGBA32, false);
            atlas.SetPixels(new Color[512 * 128]);
            for (int i = 0; i < names.Length; i++)
            {
                var strokes = LetterStrokes(labels[i], widths[i]);
                File.WriteAllText(ArtFolder + "/Key_" + names[i] + ".svg", KeySvg(widths[i], strokes), new UTF8Encoding(false));
                for (int y = 0; y < 104; y++)
                    for (int x = 0; x < widths[i]; x++)
                    {
                        Color sum = Color.clear;
                        // Analytic vector geometry, supersampled for a clean small UI sprite.
                        for (int sy = 0; sy < 2; sy++)
                            for (int sx = 0; sx < 2; sx++)
                                sum += KeyPixel(new Vector2(x + (sx + 0.5f) / 2, y + (sy + 0.5f) / 2), widths[i], strokes) * 0.25f;
                        if (sum.a > 0f) { sum.r /= sum.a; sum.g /= sum.a; sum.b /= sum.a; }
                        atlas.SetPixel(positions[i] + x, 12 + y, sum);
                    }
            }
            atlas.Apply();
            string atlasPath = ArtFolder + "/Keyboard.png";
            File.WriteAllBytes(atlasPath, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            AssetDatabase.Refresh();
            var importer = (TextureImporter)AssetImporter.GetAtPath(atlasPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = true;
            importer.spritesheet = Enumerable.Range(0, 3).Select(i => new SpriteMetaData
            {
                name = names[i], rect = new Rect(positions[i], 12, widths[i], 104),
                alignment = (int)SpriteAlignment.Center, pivot = Vector2.one * 0.5f
            }).ToArray();
            importer.SaveAndReimport();
            string path = ArtFolder + "/Keyboard.asset";
            TMP_SpriteAsset asset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
                AssetDatabase.CreateAsset(asset, path);
            }
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("m_Version").stringValue = "1.1.0";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            asset.spriteSheet = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            if (asset.material == null)
            {
                asset.material = new Material(Shader.Find("TextMeshPro/Sprite")) { name = "Keyboard Material" };
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }
            asset.material.mainTexture = asset.spriteSheet;
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(atlasPath).OfType<Sprite>().ToArray();
            asset.spriteGlyphTable.Clear(); asset.spriteCharacterTable.Clear();
            for (int i = 0; i < names.Length; i++)
            {
                var glyph = new TMP_SpriteGlyph((uint)i,
                    new GlyphMetrics(widths[i], 104, 0, 84, widths[i] + 8),
                    new GlyphRect(positions[i], 12, widths[i], 104), 1f, 0)
                    { sprite = sprites.Single(sprite => sprite.name == names[i]) };
                asset.spriteGlyphTable.Add(glyph);
                asset.spriteCharacterTable.Add(new TMP_SpriteCharacter((uint)(0xE000 + i), asset, glyph) { name = names[i], scale = 1f });
            }
            asset.UpdateLookupTables();
            EditorUtility.SetDirty(asset); EditorUtility.SetDirty(asset.material);
            return asset;
        }

        // Each pair is a line segment in a five-by-seven letter grid. Shared by SVG and atlas generation.
        private static List<Vector4> LetterStrokes(string label, float width)
        {
            var letters = new Dictionary<char, string>
            {
                ['S'] = "4,0 0,0 0,3 4,3 4,6 0,6", ['P'] = "0,6 0,0 4,0 4,3 0,3",
                ['A'] = "0,6 0,0 4,0 4,6|0,3 4,3", ['C'] = "4,0 0,0 0,6 4,6",
                ['E'] = "4,0 0,0 0,6 4,6|0,3 3,3", ['Z'] = "0,0 4,0 0,6 4,6",
                ['T'] = "0,0 4,0|2,0 2,6", ['R'] = "0,6 0,0 4,0 4,3 0,3|2,3 4,6",
                ['L'] = "0,0 0,6 4,6"
            };
            var result = new List<Vector4>();
            float scale = 4.3f;
            float start = (width - (label.Length * 6 - 2) * scale) * 0.5f;
            for (int i = 0; i < label.Length; i++)
                foreach (string path in letters[label[i]].Split('|'))
                {
                    Vector2[] points = path.Split(' ').Select(pair =>
                    {
                        string[] xy = pair.Split(',');
                        return new Vector2(start + i * 6 * scale + int.Parse(xy[0]) * scale,
                            36f + int.Parse(xy[1]) * scale);
                    }).ToArray();
                    for (int j = 1; j < points.Length; j++)
                        result.Add(new Vector4(points[j - 1].x, points[j - 1].y, points[j].x, points[j].y));
                }
            return result;
        }

        private static string KeySvg(int width, List<Vector4> strokes)
        {
            string F(float n) => n.ToString("0.###", CultureInfo.InvariantCulture);
            var svg = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"104\" viewBox=\"0 0 {width} 104\">\n");
            svg.AppendLine($"<rect x=\"5\" y=\"11\" width=\"{width - 10}\" height=\"88\" rx=\"18\" fill=\"#7b97b0\"/>");
            svg.AppendLine($"<rect x=\"5\" y=\"5\" width=\"{width - 10}\" height=\"88\" rx=\"18\" fill=\"#ecf6ff\"/>");
            svg.AppendLine($"<rect x=\"11\" y=\"11\" width=\"{width - 22}\" height=\"76\" rx=\"13\" fill=\"none\" stroke=\"#aecbdf\" stroke-width=\"2\"/>");
            svg.AppendLine("<g fill=\"none\" stroke=\"#263e56\" stroke-width=\"4\" stroke-linecap=\"round\" stroke-linejoin=\"round\">");
            foreach (Vector4 s in strokes) svg.AppendLine($"<path d=\"M{F(s.x)} {F(s.y)} L{F(s.z)} {F(s.w)}\"/>");
            svg.AppendLine("</g></svg>");
            return svg.ToString();
        }

        private static Color KeyPixel(Vector2 point, float width, List<Vector4> strokes)
        {
            bool Round(float x, float y, float w, float h, float r)
            {
                Vector2 q = new Vector2(Mathf.Abs(point.x - x - w / 2), Mathf.Abs(point.y - y - h / 2)) - new Vector2(w / 2 - r, h / 2 - r);
                return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) <= r;
            }
            Color color = Color.clear;
            if (Round(5, 5, width - 10, 88, 18)) color = new Color32(123, 151, 176, 255);
            if (Round(5, 11, width - 10, 88, 18)) color = new Color32(236, 246, 255, 255);
            if (Round(10, 16, width - 20, 78, 14) && !Round(12, 18, width - 24, 74, 12)) color = new Color32(174, 203, 223, 255);
            Vector2 topDown = new Vector2(point.x, 104 - point.y);
            foreach (Vector4 s in strokes)
            {
                Vector2 a = new Vector2(s.x, s.y), b = new Vector2(s.z, s.w);
                float t = Mathf.Clamp01(Vector2.Dot(topDown - a, b - a) / (b - a).sqrMagnitude);
                if ((topDown - Vector2.Lerp(a, b, t)).sqrMagnitude <= 4f) return new Color32(38, 62, 86, 255);
            }
            return color;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 size)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.layer = 5;
            RectTransform rect = root.GetComponent<RectTransform>();
            if (parent != null) rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            return rect;
        }

        private static void BuildDemo()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene demo = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(demo);
            try
            {
                var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cameraObject.tag = "MainCamera";
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true; camera.orthographicSize = 6f;
                camera.transform.position = new Vector3(10f, 3.7f, -20f);
                camera.backgroundColor = new Color(0.035f, 0.055f, 0.09f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                var light = new GameObject("Demo Light", typeof(Light)).GetComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.4f;
                light.transform.rotation = Quaternion.Euler(35f, -25f, 0);
                var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Player.prefab"), demo);
                player.GetComponent<PlayerController>().TeleportJoined(new Vector2(0f, 0.9f));
                LevelPrefabBuilder.RecordInstanceOverrides(player);
                new GameObject("Rewind Manager", typeof(RewindManager));
                PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/TutorialPresenter.prefab"), demo);
                Platform("Ground", new Vector2(10f, -0.5f), new Vector2(30f, 1f), false);
                Platform("Head Landing Platform", new Vector2(4f, 2.8f), new Vector2(4f, 0.25f), true);
                int[] indices = { 0, 2, 3 };
                float[] locations = { 3f, 9f, 15f };
                for (int i = 0; i < indices.Length; i++)
                {
                    var trigger = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Trigger_" + Names[indices[i]] + ".prefab"), demo);
                    trigger.transform.position = new Vector3(locations[i], 1.5f, 0f);
                    if (indices[i] == 0) trigger.GetComponent<BoxCollider2D>().size = new Vector2(1f, 3f);
                    LevelPrefabBuilder.RecordInstanceOverrides(trigger);
                }
                if (!EditorSceneManager.SaveScene(demo, DemoPath)) throw new IOException("无法保存教学演示场景。");
            }
            finally
            {
                if (!Application.isBatchMode) EditorSceneManager.CloseScene(demo, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        private static void Platform(string name, Vector2 position, Vector2 size, bool oneWay)
        {
            var root = new GameObject(name, typeof(BoxCollider2D), typeof(WorldSurface), typeof(SpriteRenderer));
            root.transform.position = position;
            root.GetComponent<BoxCollider2D>().size = size;
            root.GetComponent<WorldSurface>().Kind = oneWay ? SurfaceKind.OneWay : SurfaceKind.Solid;
            SpriteRenderer renderer = root.GetComponent<SpriteRenderer>();
            renderer.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            renderer.drawMode = SpriteDrawMode.Sliced; renderer.size = size;
            renderer.color = oneWay ? new Color(0.3f, 0.75f, 0.9f) : new Color(0.12f, 0.22f, 0.32f);
        }
    }
}
