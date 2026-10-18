using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Explicit prefab authoring only. This class never opens or saves the production gameplay scene.
    public static class ReturnJourneyAssets
    {
        public const string Folder = "Assets/Prefabs/ReturnJourney";
        public const string DeparturePath = Folder + "/ReturnJourneyDeparture.prefab";
        public const string PlatformPath = Folder + "/ReturnJourneyPlatform.prefab";
        public const string DoorPath = Folder + "/ReturnJourneyDoor.prefab";
        public const string FeedbackPath = Folder + "/KeyPickupFeedback.prefab";
        public const string PreviewPath = "Assets/Scenes/ReturnJourneyMechanicsPreview.unity";
        public const string WorldPreviewPath = "Assets/Scenes/ReturnJourneyPreview.unity";
        public const string PausePath = "Assets/Prefabs/UI/PauseMenu.prefab";
        public const string KeyPath = "Assets/Prefabs/Interactables/ProgressKey.prefab";
        private const string BurstPath = "Assets/Prefabs/Effects/LightBurstParticle.prefab";

        [MenuItem("TapTap/维护/返程/创建缺失预制体并接入钥匙和暂停菜单")]
        public static void Build()
        {
            LevelPrefabBuilder.EnsureFolder(Folder);
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                Material material = EnsureMaterial();
                if (AssetDatabase.LoadAssetAtPath<GameObject>(FeedbackPath) == null) CreateFeedback(preview);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PlatformPath) == null) CreatePlatform(preview, material);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(DoorPath) == null) CreateDoor(preview, material);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(DeparturePath) == null) CreateDeparture(preview);
                ConfigureKey();
                ConfigurePause();
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            Debug.Log("返程预制体已准备完成；未修改任何游玩场景。");
        }
        public static void BuildBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            Build();
        }

        [MenuItem("TapTap/维护/返程/创建独立演示场景")]
        public static void CreatePreview()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PreviewPath) != null)
            {
                Debug.Log("独立返程演示场景已存在，不覆盖手工配置：" + PreviewPath);
                return;
            }
            Build();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                Material material = EnsureMaterial();
                GameObject playerObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Player.prefab"), scene);
                var player = playerObject.GetComponent<PlayerController>();
                // Save a Transform pose: Rigidbody2D.Teleport is a runtime operation, not scene authoring.
                playerObject.transform.position = new Vector3(-8f, 0.6f, 0f);
                GameObject floor = Root("Floor", scene);
                floor.transform.position = new Vector3(4f, -0.5f, 0f);
                floor.AddComponent<BoxCollider2D>().size = new Vector2(30f, 1f);
                floor.AddComponent<WorldSurface>();
                Cube(floor.transform, new Vector3(0f, 0f, 0.5f), new Vector3(30f, 1f, 1f), material);
                var departure = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DeparturePath), scene);
                var key = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(KeyPath), scene);
                key.transform.position = new Vector3(4f, 0.65f, 0f);
                var door = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DoorPath), scene);
                door.transform.position = new Vector3(10f, 0.5f, 0f);
                PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PausePath), scene);
                GameObject regionRoot = Root("Preview Fixed Shot", scene);
                var region = regionRoot.AddComponent<LevelRegion>();
                region.Size = new Vector2Int(30, 8);
                region.CameraSettings.HasPreset = true;
                region.CameraSettings.LocalPosition = new Vector3(1f, 2f, -28f);
                region.CameraSettings.FieldOfView = 32f;
                GameObject rigObject = Root("Gameplay Camera Rig", scene);
                Transform motion = Child("Shake", rigObject.transform, Vector3.zero);
                Camera camera = Child("Camera", motion, Vector3.zero).gameObject.AddComponent<Camera>();
                camera.tag = "MainCamera"; camera.allowHDR = true;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.15f, 0.2f);
                camera.gameObject.AddComponent<AudioListener>();
                camera.gameObject.AddComponent<RewindScreenEffect>();
                var rig = rigObject.AddComponent<DemoCamera>();
                rig.Configure(player, camera, motion); rig.SetRegion(region);
                departure.GetComponent<ReturnJourneyPresentation>().TargetCamera = camera;
                GameObject lamp = Root("Preview Light", scene);
                Light lighting = lamp.AddComponent<Light>(); lighting.type = LightType.Directional;
                lamp.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
                var guide = Root("Return Guide", scene).AddComponent<IndicatorLight>();
                guide.transform.position = new Vector3(4f, 4f, 0.1f);
                GameplayLightingDefaults.Apply(scene);
                EditorSceneManager.SaveScene(scene, PreviewPath);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
            Debug.Log("已创建独立返程演示场景：" + PreviewPath);
        }
        private static GameObject Root(string name, Scene scene)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }
        private static Transform Child(string name, Transform parent, Vector3 position)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = position;
            return child.transform;
        }
        private static Material EnsureMaterial()
        {
            string path = Folder + "/ReturnJourneyMetal.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", new Color(0.56f, 0.64f, 0.67f));
            material.SetFloat("_Smoothness", 0.2f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        private static Transform Cube(Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Model";
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = scale;
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            return cube.transform;
        }
        private static void CreateFeedback(Scene scene)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(BurstPath);
            var root = Object.Instantiate(source);
            root.name = "KeyPickupFeedback";
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one * 0.4f;
            foreach (ParticleSystem particles in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.playOnAwake = false;
                main.loop = false;
                main.startLifetimeMultiplier *= 0.65f;
                var emission = particles.emission;
                for (int i = 0; i < emission.burstCount; i++)
                {
                    var burst = emission.GetBurst(i);
                    burst.count = new ParticleSystem.MinMaxCurve(Mathf.Max(1f, burst.count.constantMax * 0.3f));
                    emission.SetBurst(i, burst);
                }
                emission.rateOverTimeMultiplier *= 0.3f;
            }
            PrefabUtility.SaveAsPrefabAsset(root, FeedbackPath);
            Object.DestroyImmediate(root);
        }
        private static void CreatePlatform(Scene scene, Material material)
        {
            GameObject root = Root("ReturnJourneyPlatform", scene);
            var collider = root.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(3f, 0.3f);
            root.AddComponent<WorldSurface>().Kind = SurfaceKind.OneWay;
            Transform view = Child("View (starts one grid above collision)", root.transform, Vector3.up);
            Cube(view, new Vector3(0f, 0f, 0.5f), new Vector3(3f, 0.3f, 1f), material);
            var platform = root.AddComponent<ReturnJourneyPlatform>();
            platform.View = view;
            GameObject burst = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BurstPath), root.transform);
            burst.name = "CollapseBurst";
            burst.transform.localPosition = Vector3.up;
            burst.transform.localScale = Vector3.one * 0.65f;
            platform.CollapseBurst = burst.GetComponent<ParticleSystem>();
            platform.CollapseBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = platform.CollapseBurst.main;
            main.playOnAwake = false; main.loop = false;
            PrefabUtility.SaveAsPrefabAsset(root, PlatformPath);
            Object.DestroyImmediate(root);
        }
        private static void CreateDoor(Scene scene, Material material)
        {
            GameObject root = Root("ReturnJourneyDoor", scene);
            root.AddComponent<BoxCollider2D>().size = Vector2.one;
            root.AddComponent<WorldSurface>();
            Transform view = Child("View", root.transform, Vector3.zero);
            Cube(view, new Vector3(0f, 0f, 0.5f), new Vector3(0.96f, 1f, 1f), material);
            Transform socket = Child("KeySocket", root.transform, new Vector3(0.22f, 0f, -0.1f));
            Transform sensor = Child("Body Proximity", root.transform, new Vector3(0.9f, 0f, 0f));
            var trigger = sensor.gameObject.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true; trigger.size = new Vector2(2.5f, 2f);
            var door = root.AddComponent<ReturnJourneyDoor>();
            door.View = view; door.KeySocket = socket; door.Proximity = trigger;
            PrefabUtility.SaveAsPrefabAsset(root, DoorPath);
            Object.DestroyImmediate(root);
        }
        private static void CreateDeparture(Scene scene)
        {
            GameObject root = Root("ReturnJourneyDeparture", scene);
            root.AddComponent<GameStageProgression>();
            var presentation = root.AddComponent<ReturnJourneyPresentation>();
            presentation.SignalShader = Resources.Load<Shader>("Shaders/ReturnJourneySignal");
            GameObject platformObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlatformPath), scene);
            platformObject.transform.SetParent(root.transform, false);
            platformObject.transform.localPosition = new Vector3(2f, 0f, 0f);
            var platform = platformObject.GetComponent<ReturnJourneyPlatform>();
            for (int i = 0; i < 3; i++)
            {
                Transform owner = Child(i == 2 ? "3 Begin Return" : (i + 1) + " Warning", root.transform, new Vector3(-6f + i * 3f, 1f, 0f));
                var area = owner.gameObject.AddComponent<BoxCollider2D>();
                area.isTrigger = true; area.size = new Vector2(0.5f, 5f);
                var trigger = owner.gameObject.AddComponent<ReturnJourneyTrigger>();
                trigger.CueId = "departure-" + (i + 1);
                trigger.Cue = i == 2 ? ReturnJourneyCue.BeginReturn : ReturnJourneyCue.Warning;
                trigger.Presentation = presentation;
                trigger.Platform = i == 2 ? platform : null;
            }
            PrefabUtility.SaveAsPrefabAsset(root, DeparturePath);
            Object.DestroyImmediate(root);
        }
        private static void ConfigureKey()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(KeyPath);
            try
            {
                var key = root.GetComponent<ProgressKey>();
                if (key.PickupFeedbackPrefab == null)
                {
                    key.PickupFeedbackPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FeedbackPath).GetComponent<ParticleSystem>();
                    PrefabUtility.SaveAsPrefabAsset(root, KeyPath);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static void ConfigurePause()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PausePath);
            try
            {
                var menu = root.GetComponent<PauseMenu>();
                var existing = root.GetComponent<ReturnJourneyPausePortrait>();
                if (existing != null)
                {
                    if (existing.OriginalArt == null)
                    {
                        existing.OriginalArt = menu.LeftArt.GetComponentInChildren<Image>(true);
                        PlaceBlueprint(existing.ReturnArt, existing.OriginalArt.gameObject.layer);
                        PrefabUtility.SaveAsPrefabAsset(root, PausePath);
                    }
                    return;
                }
                var original = menu.LeftArt.GetComponentInChildren<Image>(true);
                var owner = new GameObject("Return Journey Blueprint", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rect = (RectTransform)owner.transform;
                rect.SetParent(menu.LeftArt, false);
                var image = owner.GetComponent<Image>();
                PlaceBlueprint(image, original.gameObject.layer);
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/RobotLevelBlueprint.png");
                image.preserveAspect = true; image.raycastTarget = false;
                owner.SetActive(false);
                var portrait = root.AddComponent<ReturnJourneyPausePortrait>();
                portrait.OriginalArt = original;
                portrait.ReturnArt = image;
                portrait.TearShader = Resources.Load<Shader>("Shaders/ReturnPortraitTear");
                PrefabUtility.SaveAsPrefabAsset(root, PausePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static void PlaceBlueprint(Image image, int layer)
        {
            image.gameObject.layer = layer;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            // The original 1920x1080 artwork has transparent padding; its paper occupies the left 800x960 area.
            rect.anchoredPosition = new Vector2(-310f, 0f);
            rect.sizeDelta = new Vector2(800f, 960f);
        }
    }
}
