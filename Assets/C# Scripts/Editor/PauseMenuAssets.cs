using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    public static class PauseMenuAssets
    {
        public const string ScenePath = "Assets/Art/Sprite/UI/Fqh.unity";
        public const string PrefabPath = "Assets/Prefabs/UI/PauseMenu.prefab";
        public const string MusicPath = "Assets/Audio/QuietWorkshop.wav";
        public const string ClickPath = "Assets/Audio/SoftClick.wav";

        // Explicit command only: opening Unity never rewrites the artist's scene.
        [MenuItem("Tools/TapTap/UI/Build Fqh Pause Menu Prefab")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory("Assets/Prefabs/UI");
            Directory.CreateDirectory("Assets/Audio");
            GenerateAudio();
            AssetDatabase.Refresh();
            ConfigureAudio(MusicPath);
            ConfigureAudio(ClickPath);
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Canvas canvas = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Canvas>(true)).First();
            if (PrefabUtility.IsPartOfPrefabInstance(canvas))
                PrefabUtility.UnpackPrefabInstance(canvas.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // Keep the supplied Canvas and its five full-frame art layers; replace placeholder controls.
            Image[] art = canvas.GetComponentsInChildren<Image>(true)
                .Where(image => image.sprite != null && Enumerable.Range(1, 5).Any(i =>
                    AssetDatabase.GetAssetPath(image.sprite) == "Assets/Art/Sprite/UI/0" + i + ".png")).ToArray();
            if (art.Length != 5) throw new InvalidOperationException("Fqh must contain the original 01–05 art layers.");
            foreach (Image image in art) image.transform.SetParent(canvas.transform, false);
            foreach (Transform child in canvas.transform.Cast<Transform>().ToArray())
                if (!art.Any(image => image.transform == child)) Object.DestroyImmediate(child.gameObject);
            foreach (Image image in art)
            {
                foreach (Transform child in image.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
                Button oldButton = image.GetComponent<Button>();
                if (oldButton != null) Object.DestroyImmediate(oldButton);
                image.raycastTarget = false;
            }

            canvas.name = "PauseMenu";
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            canvas.transform.localScale = Vector3.one;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            RectTransform content = Rect("MenuContent", canvas.transform, Vector2.zero, Vector2.zero);
            Stretch(content);
            Image backdrop = Rect("Backdrop", content, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            Stretch(backdrop.rectTransform);
            backdrop.color = new Color(0.025f, 0.035f, 0.07f, 0.9f);
            foreach (Image image in art.OrderBy(image => image.sprite.name))
            {
                image.transform.SetParent(content, false);
                image.name = new[] { "Instructions", "PauseTitle", "ResumeArt", "MainMenuArt", "AudioSettingsArt" }
                    [int.Parse(image.sprite.name) - 1];
                Place(image.rectTransform, Vector2.zero, new Vector2(1920, 1080));
            }
            PauseMenu menu = canvas.GetComponent<PauseMenu>();
            if (menu == null) menu = canvas.gameObject.AddComponent<PauseMenu>();
            menu.Content = content.gameObject;
            menu.ResumeButton = Hotspot("ResumeButton", content, new Vector2(484, 72), new Vector2(266, 96),
                art.First(image => image.sprite.name == "03"));
            menu.MainMenuButton = Hotspot("MainMenuButton", content, new Vector2(484, -69), new Vector2(264, 96),
                art.First(image => image.sprite.name == "04"));
            menu.MusicSlider = VolumeSlider("MusicVolume", content, new Vector2(540, -264));
            menu.EffectsSlider = VolumeSlider("EffectsVolume", content, new Vector2(540, -325));
            menu.MusicValue = Label("MusicValue", content, new Vector2(697, -264), new Vector2(74, 34), "45%", 22);
            menu.EffectsValue = Label("EffectsValue", content, new Vector2(697, -325), new Vector2(74, 34), "70%", 22);
            Label("EscapeHint", content, new Vector2(484, -445), new Vector2(480, 36), "ESC 返回游戏", 24);
            menu.Notice = Label("Notice", content, new Vector2(470, -500), new Vector2(650, 50), "", 20);
            menu.BackgroundMusic = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
            menu.ClickSound = AssetDatabase.LoadAssetAtPath<AudioClip>(ClickPath);
            Navigation(menu.ResumeButton, menu.EffectsSlider, menu.MainMenuButton);
            Navigation(menu.MainMenuButton, menu.ResumeButton, menu.MusicSlider);
            Navigation(menu.MusicSlider, menu.MainMenuButton, menu.EffectsSlider);
            Navigation(menu.EffectsSlider, menu.MusicSlider, menu.ResumeButton);
            PrefabUtility.SaveAsPrefabAssetAndConnect(canvas.gameObject, PrefabPath, InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Fqh pause menu prefab and test audio saved: " + PrefabPath);
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5;
            rect.SetParent(parent, false);
            Place(rect, position, size);
            return rect;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static Button Hotspot(string name, Transform parent, Vector2 position, Vector2 size, Image art)
        {
            Image hit = Rect(name, parent, position, size).gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            Button button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = art;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.75f, 0.9f, 1f);
            colors.selectedColor = new Color(0.85f, 0.94f, 1f);
            colors.pressedColor = new Color(0.55f, 0.72f, 0.9f);
            button.colors = colors;
            return button;
        }

        private static Slider VolumeSlider(string name, Transform parent, Vector2 position)
        {
            RectTransform root = Rect(name, parent, position, new Vector2(230, 40));
            Image hit = root.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            Slider slider = root.gameObject.AddComponent<Slider>();
            RectTransform fillArea = Rect("Fill Area", root, Vector2.zero, new Vector2(230, 10));
            Image fill = Rect("Fill", fillArea, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            Stretch(fill.rectTransform);
            fill.color = new Color(0.83f, 0.88f, 0.97f); fill.raycastTarget = false;
            RectTransform handleArea = Rect("Handle Slide Area", root, Vector2.zero, new Vector2(230, 40));
            Image handle = Rect("Handle", handleArea, Vector2.zero, new Vector2(18, 32)).gameObject.AddComponent<Image>();
            // Reuse the artist's sliced knob rather than its 1920x1080 transparent source bounds.
            handle.sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Sprite/UI/06.png").OfType<Sprite>().FirstOrDefault();
            handle.color = new Color(0.94f, 0.96f, 1f);
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            // Slider stretches the handle vertically across its 40px area; keep the knob 35px tall.
            handle.rectTransform.sizeDelta = new Vector2(22, -5);
            slider.targetGraphic = handle;
            slider.minValue = 0f; slider.maxValue = 1f; slider.value = 0.5f;
            return slider;
        }

        private static Text Label(string name, Transform parent, Vector2 position, Vector2 size, string value, int fontSize)
        {
            Text text = Rect(name, parent, position, size).gameObject.AddComponent<Text>();
            text.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/ResourceHanRoundedCN-Bold.ttf");
            text.fontSize = fontSize;
            text.text = value;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.78f, 0.83f, 0.94f);
            text.raycastTarget = false;
            return text;
        }

        private static void Navigation(Selectable target, Selectable up, Selectable down)
        {
            target.navigation = new Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit, selectOnUp = up, selectOnDown = down };
        }

        private static void ConfigureAudio(string path)
        {
            AudioImporter importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.75f;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        // Original 72 BPM Cmaj7 / Am7 / Fmaj7 / G6 instrumental. Wrapped tails make a seamless loop.
        private static void GenerateAudio()
        {
            const int rate = 44100;
            const double beat = 60.0 / 72.0;
            if (!File.Exists(MusicPath))
            {
                float[] samples = new float[(int)(32 * beat * rate) * 2];
                int[][] chords = { new[] { 48, 60, 64, 67, 71 }, new[] { 45, 57, 60, 64, 67 },
                    new[] { 41, 57, 60, 64, 69 }, new[] { 43, 55, 59, 62, 64 } };
                for (int bar = 0; bar < 8; bar++)
                {
                    int[] chord = chords[bar / 2];
                    AddNote(samples, rate, bar * 4 * beat, chord[0], 3.8 * beat, 0.15, 0.5, true);
                    for (int step = 0; step < 8; step++)
                        AddNote(samples, rate, (bar * 4 + step * 0.5) * beat, chord[1 + new[] { 0, 2, 1, 3, 2, 1, 3, 2 }[step]],
                            2.4, step % 2 == 0 ? 0.13 : 0.095, 0.3 + 0.4 * (step % 3) / 2, false);
                }
                WriteWave(MusicPath, samples, rate);
            }
            if (!File.Exists(ClickPath))
            {
                float[] click = new float[(int)(rate * 0.18) * 2];
                for (int i = 0; i < click.Length / 2; i++)
                {
                    double t = (double)i / rate;
                    float value = (float)(Math.Sin(2 * Math.PI * 880 * t) * Math.Exp(-32 * t)
                        * Math.Min(1, t / 0.006) * Math.Min(1, (0.18 - t) / 0.02) * 0.24);
                    click[2 * i] = click[2 * i + 1] = value;
                }
                WriteWave(ClickPath, click, rate);
            }
        }

        private static void AddNote(float[] samples, int rate, double start, int midi, double duration, double gain, double pan, bool bass)
        {
            double frequency = 440 * Math.Pow(2, (midi - 69) / 12.0);
            int frames = samples.Length / 2;
            for (int i = 0; i < duration * rate; i++)
            {
                double t = (double)i / rate;
                double envelope = Math.Min(1, t / (bass ? 0.08 : 0.018)) * Math.Exp(-t * (bass ? 0.8 : 1.8))
                    * Math.Min(1, (duration - t) / 0.25);
                double phase = 2 * Math.PI * frequency * t;
                float value = (float)((Math.Sin(phase) + 0.22 * Math.Sin(phase * 2) * Math.Exp(-t * 3)
                    + 0.06 * Math.Sin(phase * 3)) * envelope * gain);
                int frame = ((int)(start * rate) + i) % frames;
                samples[frame * 2] += value * (float)Math.Sqrt(1 - pan);
                samples[frame * 2 + 1] += value * (float)Math.Sqrt(pan);
            }
        }

        private static void WriteWave(string path, float[] samples, int rate)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)2); writer.Write(rate); writer.Write(rate * 4);
                writer.Write((short)4); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
                foreach (float sample in samples) writer.Write((short)(Mathf.Clamp(sample, -1f, 1f) * 32767));
            }
        }
    }
}
