using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Explicit authoring only: never opens/saves the production world, edits audio, or replaces configured assets.
    public static class RadioAssets
    {
        public const string RadioPath = "Assets/Prefabs/Interactables/Radio.prefab";
        public const string ModelPath = "Assets/Prefabs/Models/RadioModel.prefab";
        public const string SubtitlePath = "Assets/Resources/UI/VoiceSubtitles.prefab";
        public const string SequenceFolder = "Assets/Audio/Voice/Sequences";
        public const string FirstPath = SequenceFolder + "/FirstEncounter.asset";
        public const string ReturnPath = SequenceFolder + "/ReturnJourney.asset";
        public const string DemoPath = "Assets/Scenes/Demo/Radio_Demo.unity";
        public const string MaterialFolder = "Assets/Art/Materials/Radio";
        public const string FontPath = "Assets/Fonts/text(SMALL) SDF.asset";

        [MenuItem("TapTap/语音与收音机/创建缺失资源")]
        public static void Build()
        {
            foreach (string path in new[] { RadioPath, ModelPath, SubtitlePath, FirstPath, DemoPath, MaterialFolder + "/RadioCase.mat" })
                Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.Refresh();
            EnsureSequence(FirstPath);
            EnsureSequence(ReturnPath);
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null) BuildModel(preview);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(SubtitlePath) == null) BuildSubtitles(preview);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(RadioPath) == null) BuildRadio(preview);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            AssetDatabase.SaveAssets();
            Debug.Log("收音机、字幕和两套空白语音SO已准备完成；请手动配置分段音频与中英文字幕。");
        }

        public static void BuildBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            // Unity cannot create an additive scene while the initial untitled scene is unsaved.
            Directory.CreateDirectory("Assets/RadioValidation");
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/RadioValidation/Bootstrap.unity");
            Build();
            CreateDemo();
        }

        private static void EnsureSequence(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<VoiceSequence>(path) != null) return;
            var sequence = ScriptableObject.CreateInstance<VoiceSequence>();
            sequence.Segments = new[] { new VoiceSegment() };
            AssetDatabase.CreateAsset(sequence, path);
        }

        private static Material Material(string name, Color color)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("Missing URP Unlit shader.");
            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static GameObject Root(string name, Scene scene)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private static Transform Part(Transform parent, string name, Vector3 position, Vector3 scale,
            Material material, PrimitiveType type = PrimitiveType.Cube)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return part.transform;
        }

        private static void BuildModel(Scene scene)
        {
            Material body = Material("RadioCase", new Color32(113, 144, 145, 255));
            Material trim = Material("RadioTrim", new Color32(216, 199, 172, 255));
            Material dark = Material("RadioGrille", new Color32(48, 63, 73, 255));
            Material slat = Material("RadioSlats", new Color32(99, 120, 123, 255));
            Material amber = Material("RadioDial", new Color32(231, 171, 100, 255));
            Material steel = Material("RadioAntenna", new Color32(176, 190, 187, 255));
            GameObject root = Root("RadioModel", scene);
            Part(root.transform, "Case", new Vector3(0f, 0.36f, 0f), new Vector3(0.98f, 0.62f, 0.34f), body);
            Part(root.transform, "Inset Face", new Vector3(0f, 0.36f, -0.178f), new Vector3(0.91f, 0.55f, 0.025f), trim);
            Part(root.transform, "Speaker", new Vector3(-0.17f, 0.34f, -0.2f), new Vector3(0.51f, 0.39f, 0.025f), dark);
            for (int i = 0; i < 7; i++)
                Part(root.transform, "Speaker Slat " + i, new Vector3(-0.17f, 0.19f + i * 0.05f, -0.217f),
                    new Vector3(0.46f, 0.014f, 0.012f), slat);
            Part(root.transform, "Frequency Window", new Vector3(0.27f, 0.54f, -0.202f), new Vector3(0.26f, 0.09f, 0.025f), dark);
            for (int i = 0; i < 5; i++)
                Part(root.transform, "Frequency Mark " + i, new Vector3(0.18f + i * 0.045f, 0.54f, -0.22f),
                    new Vector3(0.008f, i % 2 == 0 ? 0.048f : 0.028f, 0.008f), trim);
            Part(root.transform, "Tuning Needle", new Vector3(0.29f, 0.54f, -0.229f), new Vector3(0.012f, 0.062f, 0.008f), amber);
            for (int i = 0; i < 2; i++)
            {
                float y = 0.36f - i * 0.18f;
                Transform knob = Part(root.transform, "Knob " + i, new Vector3(0.29f, y, -0.227f),
                    Vector3.one * 0.12f, i == 0 ? amber : slat, PrimitiveType.Cylinder);
                knob.localScale = new Vector3(0.12f, 0.018f, 0.12f);
                knob.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Part(root.transform, "Knob Tick " + i, new Vector3(0.29f, y + 0.025f, -0.25f),
                    new Vector3(0.012f, 0.045f, 0.008f), dark);
                Part(root.transform, "Foot " + i, new Vector3(i == 0 ? -0.33f : 0.33f, 0.035f, 0f),
                    new Vector3(0.16f, 0.07f, 0.24f), dark);
                Part(root.transform, "Handle Post " + i, new Vector3(i == 0 ? -0.28f : 0.28f, 0.735f, 0.015f),
                    new Vector3(0.06f, 0.14f, 0.095f), dark);
            }
            Part(root.transform, "Handle Grip", new Vector3(0f, 0.81f, 0.015f), new Vector3(0.62f, 0.065f, 0.11f), dark);
            Part(root.transform, "Handle Inset", new Vector3(0f, 0.817f, -0.045f), new Vector3(0.38f, 0.035f, 0.014f), trim);
            var antenna = new GameObject("Antenna Pivot").transform;
            antenna.SetParent(root.transform, false);
            antenna.localPosition = new Vector3(0.38f, 0.68f, 0.06f);
            antenna.localRotation = Quaternion.Euler(0f, 0f, -18f);
            Part(antenna, "Antenna", new Vector3(0f, 0.2f, 0f), new Vector3(0.018f, 0.4f, 0.018f), steel);
            Part(antenna, "Antenna Tip", new Vector3(0f, 0.4f, 0f), Vector3.one * 0.033f, trim, PrimitiveType.Sphere);
            PrefabUtility.SaveAsPrefabAsset(root, ModelPath);
            Object.DestroyImmediate(root);
        }

        private static void BuildSubtitles(Scene scene)
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null) throw new InvalidOperationException("Missing text(SMALL) SDF font.");
            var root = new GameObject("VoiceSubtitles", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.layer = 5;
            SceneManager.MoveGameObjectToScene(root, scene);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 800;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            VoiceSubtitlePresenter presenter = root.AddComponent<VoiceSubtitlePresenter>();
            RectTransform panel = Rect("Subtitle Background", root.transform);
            panel.anchorMin = new Vector2(0.15f, 0f);
            panel.anchorMax = new Vector2(0.85f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0f, 64f);
            panel.sizeDelta = new Vector2(0f, 88f);
            Image background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.58f);
            background.raycastTarget = false;
            CanvasGroup group = panel.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = group.interactable = false;
            RectTransform label = Rect("Subtitle Text (TMP)", panel);
            label.anchorMin = Vector2.zero;
            label.anchorMax = Vector2.one;
            label.offsetMin = new Vector2(32f, 18f);
            label.offsetMax = new Vector2(-32f, -18f);
            var text = label.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = 32f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;
            text.raycastTarget = false;
            text.text = "";
            presenter.Group = group;
            presenter.Label = text;
            presenter.Panel = panel;
            PrefabUtility.SaveAsPrefabAsset(root, SubtitlePath);
            Object.DestroyImmediate(root);
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var owner = new GameObject(name, typeof(RectTransform));
            owner.layer = 5;
            var rect = owner.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void BuildRadio(Scene scene)
        {
            GameObject root = Root("Radio", scene);
            var box = root.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = Vector2.one * 4f;
            box.offset = Vector2.up;
            var receiver = root.AddComponent<RadioReceiver>();
            receiver.FirstEncounter = AssetDatabase.LoadAssetAtPath<VoiceSequence>(FirstPath);
            receiver.ReturnJourney = AssetDatabase.LoadAssetAtPath<VoiceSequence>(ReturnPath);
            var player = root.GetComponent<VoicePlayer>();
            player.SubtitlePrefab = AssetDatabase.LoadAssetAtPath<VoiceSubtitlePresenter>(SubtitlePath);
            AudioSource source = root.GetComponent<AudioSource>();
            source.playOnAwake = source.loop = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.priority = 64;
            source.volume = 0f;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), scene);
            model.transform.SetParent(root.transform, false);
            receiver.BreathingTarget = model.transform;
            PrefabUtility.SaveAsPrefabAsset(root, RadioPath);
            Object.DestroyImmediate(root);
        }

        [MenuItem("TapTap/语音与收音机/创建独立演示场景")]
        public static void CreateDemo()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoPath) != null) return;
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            {
                Debug.LogWarning("请先保存当前未命名场景，再创建独立收音机演示场景。");
                return;
            }
            Build();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                GameObject floor = Root("Demo Floor", scene);
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                floor.AddComponent<BoxCollider2D>().size = new Vector2(24f, 1f);
                floor.AddComponent<WorldSurface>();
                Part(floor.transform, "Floor View", Vector3.zero, new Vector3(24f, 1f, 1f),
                    Material("RadioDemoFloor", new Color32(75, 88, 91, 255)));
                var player = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Player.prefab"), scene);
                player.transform.position = new Vector3(-5f, 0.7f, 0f);
                PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RadioPath), scene);
                GameObject cameraObject = Root("Main Camera", scene);
                cameraObject.tag = "MainCamera";
                Camera camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                camera.transform.position = new Vector3(0f, 2.5f, -12f);
                camera.orthographic = true;
                camera.orthographicSize = 4f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(153, 173, 174, 255);
                Root("Game Stage Progression", scene).AddComponent<GameStageProgression>();
                EditorSceneManager.SaveScene(scene, DemoPath);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }

    [CustomEditor(typeof(VoiceSequence))]
    public sealed class VoiceSequenceInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (!((VoiceSequence)target).IsConfigured)
                EditorGUILayout.HelpBox("Segments 配置完整音频和段后间隔；Subtitles 配置相对本段的 Start、End 与中英文字幕。时间点须有序、不重叠并在音频范围内。未配置完整时不会消耗收听次数。", MessageType.Info);
        }
    }
}
