#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    [InitializeOnLoad]
    public static class PrototypeBuilder
    {
        public const string DemoScenePath = "Assets/Scenes/Demo/TapTapDemo.unity";
        public const string SpriteDirectory = "Assets/Art/Sprite/Prototype";
        public const string ConfigDirectory = "Assets/Runtime/Config";
        public const string PrefabDirectory = "Assets/Prefabs";
        private static readonly Color Ink = new Color(0.045f, 0.075f, 0.12f);
        private static readonly Color Floor = new Color(0.18f, 0.24f, 0.31f);
        private static readonly Color Teal = new Color(0.22f, 0.89f, 0.78f);
        private static readonly Color Cream = new Color(1f, 0.94f, 0.79f);
        private static readonly Color Coral = new Color(1f, 0.43f, 0.36f);
        private static Sprite square;
        private static Sprite rounded;
        private static Sprite triangle;
        private static Font labelFont;

        static PrototypeBuilder()
        {
            EditorApplication.delayCall += BuildMissingDemo;
        }

        private static void BuildMissingDemo()
        {
            if (Application.isBatchMode || AssetDatabase.IsAssetImportWorkerProcess()
                || EditorApplication.isPlayingOrWillChangePlaymode
                || File.Exists(DemoScenePath)
                || Enumerable.Range(0, SceneManager.sceneCount)
                    .Any(index => string.IsNullOrEmpty(SceneManager.GetSceneAt(index).path)))
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += BuildMissingDemo;
                return;
            }

            BuildPrototype();
        }

        [MenuItem("TapTap/Build Prototype")]
        public static void BuildPrototype()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before rebuilding the prototype.");

            foreach (string folder in new[] { SpriteDirectory, ConfigDirectory,
                PrefabDirectory + "/Characters", PrefabDirectory + "/Terrain",
                PrefabDirectory + "/Interactables", Path.GetDirectoryName(DemoScenePath) })
                Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            PrepareArt();

            PlayerConfig config = AssetDatabase.LoadAssetAtPath<PlayerConfig>(ConfigDirectory + "/PrototypePlayer.asset");
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<PlayerConfig>();
                AssetDatabase.CreateAsset(config, ConfigDirectory + "/PrototypePlayer.asset");
            }

            Scene previous = SceneManager.GetActiveScene();
            bool dirtySceneExists = Enumerable.Range(0, SceneManager.sceneCount)
                .Any(index => SceneManager.GetSceneAt(index).isDirty);
            bool untitledSceneExists = Enumerable.Range(0, SceneManager.sceneCount)
                .Any(index => string.IsNullOrEmpty(SceneManager.GetSceneAt(index).path));
            if (!Application.isBatchMode && untitledSceneExists)
                throw new InvalidOperationException("Save the current untitled scene before using TapTap/Build Prototype.");

            Scene demo = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(demo);

            try
            {
                GameObject playerPrefab = CreatePlayerPrefab(config);
                GameObject groundPrefab = CreateSurfacePrefab("Ground", SurfaceKind.Solid, Floor);
                GameObject solidPrefab = CreateSurfacePrefab("SolidBlock", SurfaceKind.Solid, new Color(0.28f, 0.35f, 0.44f));
                GameObject platformPrefab = CreateSurfacePrefab("OneWayPlatform", SurfaceKind.OneWay, Teal);
                GameObject conveyorPrefab = CreateSurfacePrefab("Conveyor", SurfaceKind.Solid, new Color(0.46f, 0.38f, 0.70f), 1.25f);
                GameObject spikesPrefab = CreateSpikesPrefab();
                GameObject checkpointPrefab = CreateCheckpointPrefab();
                GameObject headPrefab = CreateLooseHeadPrefab(config);

                BuildEnvironment(groundPrefab, solidPrefab, platformPrefab, conveyorPrefab,
                    spikesPrefab, checkpointPrefab, headPrefab);

                GameObject playerObject = Instance(playerPrefab, "Player", new Vector2(-5f, 0f), Vector2.one);
                PlayerController player = playerObject.GetComponent<PlayerController>();
                playerObject.GetComponent<RespawnService>().SetCheckpoint(new Vector2(-5f, 0.4f));
                BuildCamera(player);

                GameObject hud = new GameObject("Prototype HUD");
                hud.AddComponent<PrototypeHUD>().Configure(player, config);

                EditorSceneManager.SaveScene(demo, DemoScenePath);
                var otherScenes = EditorBuildSettings.scenes
                    .Where(scene => scene.path != DemoScenePath && File.Exists(scene.path));
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(DemoScenePath, true) }
                    .Concat(otherScenes).ToArray();
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
                if (demo.IsValid() && demo.isLoaded && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(demo, true);
            }

            if (!Application.isBatchMode && !dirtySceneExists)
                EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);

            Debug.Log("TapTap prototype assets saved to their art, config, prefab and scene folders.");
        }

        private static void PrepareArt()
        {
            square = SaveSprite("Square", (x, y) => Color.white);
            rounded = SaveSprite("Rounded", (x, y) =>
            {
                float radius = 0.12f;
                float dx = Mathf.Max(Mathf.Abs(x - 0.5f) - (0.5f - radius), 0f);
                float dy = Mathf.Max(Mathf.Abs(y - 0.5f) - (0.5f - radius), 0f);
                return dx * dx + dy * dy <= radius * radius ? Color.white : Color.clear;
            });
            triangle = SaveSprite("Spike", (x, y) => Mathf.Abs(x - 0.5f) <= (1f - y) * 0.5f
                ? Color.white : Color.clear);
            labelFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private static Sprite SaveSprite(string name, Func<float, float, Color> pixel)
        {
            string path = SpriteDirectory + "/" + name + ".png";
            if (!File.Exists(path))
            {
                var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                        texture.SetPixel(x, y, pixel((x + 0.5f) / 64f, (y + 0.5f) / 64f));
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 64f;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static GameObject CreatePlayerPrefab(PlayerConfig config)
        {
            GameObject root = new GameObject("Player");
            PlayerInput input = root.AddComponent<PlayerInput>();
            input.UseKeyboard = true;
            RespawnService respawn = root.AddComponent<RespawnService>();
            respawn.Configure(config);
            MovableEntity body = CreateEntity("Body", root.transform, new Vector2(0f, 0.4f), EntityPart.Body, true, config);
            MovableEntity head = CreateEntity("Head", root.transform, new Vector2(0f, 1.3f), EntityPart.Head, true, config);
            PlayerController controller = root.AddComponent<PlayerController>();
            PlayerView view = root.AddComponent<PlayerView>();
            controller.Configure(input, config, body, head, respawn, view);
            view.Configure(controller, body, head, config);
            return SavePrefab(root, "Player");
        }

        private static MovableEntity CreateEntity(string name, Transform parent, Vector2 position,
            EntityPart part, bool controlled, PlayerConfig config)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            Rigidbody2D rigidbody = root.AddComponent<Rigidbody2D>();
            rigidbody.bodyType = RigidbodyType2D.Kinematic;
            rigidbody.gravityScale = 0f;
            rigidbody.freezeRotation = true;
            rigidbody.interpolation = RigidbodyInterpolation2D.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rigidbody.useFullKinematicContacts = true;
            root.AddComponent<BoxCollider2D>().size = new Vector2(0.7f, 0.8f);
            root.AddComponent<KinematicMotor2D>();
            MovableEntity entity = root.AddComponent<MovableEntity>();
            entity.Configure(part, controlled, config.Gravity);
            entity.SpringHeight = config.SpringHeight;
            entity.SpringEnabled = part == EntityPart.Head;

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            entity.Visual = visual.transform;
            SpriteRenderer silhouette = visual.AddComponent<SpriteRenderer>();
            silhouette.sprite = rounded;
            silhouette.color = part == EntityPart.Head ? Cream : Teal;
            silhouette.sortingOrder = 10;
            visual.transform.localScale = new Vector3(0.7f, 0.8f, 1f);

            if (part == EntityPart.Head)
            {
                Visual(visual.transform, "Left eye", new Vector2(-0.20f, 0.08f), new Vector2(0.09f, 0.18f), Ink, square, 12);
                Visual(visual.transform, "Right eye", new Vector2(0.20f, 0.08f), new Vector2(0.09f, 0.18f), Ink, square, 12);
                Visual(visual.transform, "Mouth", new Vector2(0f, -0.19f), new Vector2(0.16f, 0.04f), Ink, square, 12);
            }
            else
            {
                Visual(visual.transform, "Chest core", new Vector2(0f, 0.12f), new Vector2(0.23f, 0.15f), Cream, rounded, 12);
                Visual(visual.transform, "Left foot", new Vector2(-0.23f, -0.42f), new Vector2(0.24f, 0.09f), Ink, square, 12);
                Visual(visual.transform, "Right foot", new Vector2(0.23f, -0.42f), new Vector2(0.24f, 0.09f), Ink, square, 12);
            }

            return entity;
        }

        private static GameObject CreateSurfacePrefab(string name, SurfaceKind kind, Color color, float conveyorSpeed = 0f)
        {
            var root = new GameObject(name);
            root.AddComponent<BoxCollider2D>().size = Vector2.one;
            root.AddComponent<WorldSurface>().Configure(kind, conveyorSpeed);
            Visual(root.transform, "Surface", Vector2.zero, Vector2.one, color, square, 0);
            Visual(root.transform, "Top edge", new Vector2(0f, 0.475f), new Vector2(1f, 0.05f),
                kind == SurfaceKind.OneWay ? Cream : color * 1.4f, square, 1);
            if (conveyorSpeed != 0f)
            {
                for (int index = 0; index < 4; index++)
                {
                    GameObject arrow = Visual(root.transform, "Belt arrow " + index,
                        new Vector2(-0.37f + index * 0.25f, 0f), new Vector2(0.09f, 0.34f), Cream, triangle, 2);
                    arrow.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
                }
            }
            return SavePrefab(root, name);
        }

        private static GameObject CreateSpikesPrefab()
        {
            var root = new GameObject("Spikes");
            var collider = root.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(1f, 0.38f);
            collider.offset = new Vector2(0f, -0.025f);
            collider.isTrigger = true;
            root.AddComponent<LethalZone>();
            for (int index = 0; index < 3; index++)
                Visual(root.transform, "Spike " + index, new Vector2(-0.33f + index * 0.33f, 0f),
                    new Vector2(0.34f, 0.45f), Coral, triangle, 2);
            return SavePrefab(root, "Spikes");
        }

        private static GameObject CreateCheckpointPrefab()
        {
            var root = new GameObject("Checkpoint");
            var collider = root.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(0.8f, 1.8f);
            collider.offset = new Vector2(0f, 0.8f);
            collider.isTrigger = true;
            root.AddComponent<CheckpointFlag>().SpawnOffset = new Vector2(0f, 0.4f);
            Visual(root.transform, "Pole", new Vector2(0f, 0.8f), new Vector2(0.07f, 1.6f), Cream, square, 3);
            Visual(root.transform, "Flag", new Vector2(0.29f, 1.35f), new Vector2(0.58f, 0.37f), Teal, square, 3);
            Visual(root.transform, "Base", new Vector2(0f, 0.05f), new Vector2(0.4f, 0.1f), Teal, rounded, 3);
            return SavePrefab(root, "Checkpoint");
        }

        private static GameObject CreateLooseHeadPrefab(PlayerConfig config)
        {
            MovableEntity head = CreateEntity("LooseHead", null, Vector2.zero, EntityPart.Head, false, config);
            return SavePrefab(head.gameObject, "LooseHead");
        }

        private static GameObject SavePrefab(GameObject temporary, string name)
        {
            string category = name == "Player" || name == "LooseHead" ? "Characters"
                : name == "Checkpoint" ? "Interactables" : "Terrain";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temporary,
                PrefabDirectory + "/" + category + "/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(temporary);
            return prefab;
        }

        private static GameObject Instance(GameObject prefab, string name, Vector2 position, Vector2 size)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = name;
            instance.transform.position = position;
            instance.transform.localScale = new Vector3(size.x, size.y, 1f);
            return instance;
        }

        private static void BuildEnvironment(GameObject ground, GameObject solid, GameObject oneWay,
            GameObject conveyor, GameObject spikes, GameObject flag, GameObject looseHead)
        {
            Transform world = new GameObject("Demo terrain").transform;
            Visual(world, "Backdrop", new Vector2(22f, 6f), new Vector2(78f, 40f), Ink, square, -30);
            for (int x = -10; x <= 56; x++)
                Visual(world, "Grid vertical " + x, new Vector2(x, 6f), new Vector2(0.012f, 25f),
                    new Color(0.20f, 0.38f, 0.45f, 0.10f), square, -20);
            for (int y = -3; y <= 17; y++)
                Visual(world, "Grid horizontal " + y, new Vector2(23f, y), new Vector2(68f, 0.012f),
                    new Color(0.20f, 0.38f, 0.45f, 0.10f), square, -20);

            for (int segment = 0; segment < 6; segment++)
            {
                if (segment == 3)
                    continue;
                GameObject floor = Instance(ground, "Ground " + (segment + 1),
                    new Vector2(-3f + segment * 11f, -0.75f), new Vector2(11f, 1.5f));
                floor.transform.SetParent(world, true);
            }
            Instance(ground, "Spring approach", new Vector2(27.475f, -0.75f), new Vector2(5.95f, 1.5f))
                .transform.SetParent(world, true);
            Instance(ground, "Spring exit", new Vector2(33.525f, -0.75f), new Vector2(3.95f, 1.5f))
                .transform.SetParent(world, true);
            Instance(ground, "Spring recess", new Vector2(31f, -1.55f), new Vector2(1.1f, 1.5f))
                .transform.SetParent(world, true);
            Instance(solid, "Left boundary", new Vector2(-8.75f, 3f), new Vector2(0.5f, 8f)).transform.SetParent(world, true);
            Instance(solid, "Right boundary", new Vector2(57.75f, 3f), new Vector2(0.5f, 8f)).transform.SetParent(world, true);

            Instance(flag, "Starting checkpoint", new Vector2(-5f, 0f), Vector2.one).transform.SetParent(world, true);
            Instance(oneWay, "First climb", new Vector2(5.5f, 2.65f), new Vector2(7f, 0.3f)).transform.SetParent(world, true);
            Instance(solid, "Low ceiling", new Vector2(13.5f, 3.55f), new Vector2(4f, 0.7f)).transform.SetParent(world, true);
            Instance(flag, "Middle checkpoint", new Vector2(16.5f, 0f), Vector2.one).transform.SetParent(world, true);

            Instance(conveyor, "Solid shelf with belt", new Vector2(21f, 2.8f), new Vector2(4f, 0.8f)).transform.SetParent(world, true);
            Instance(oneWay, "Shelf continuation", new Vector2(25.5f, 3.05f), new Vector2(5f, 0.3f)).transform.SetParent(world, true);
            Instance(looseHead, "Spring head", new Vector2(31f, -0.4f), Vector2.one).transform.SetParent(world, true);
            Instance(flag, "Spring checkpoint", new Vector2(29f, 0f), Vector2.one).transform.SetParent(world, true);
            for (int index = 0; index < 2; index++)
                Instance(spikes, "Hazard " + index, new Vector2(35.5f + index, 0.225f), Vector2.one)
                    .transform.SetParent(world, true);
            Instance(oneWay, "Hazard bypass", new Vector2(35.6f, 2.65f), new Vector2(4f, 0.3f))
                .transform.SetParent(world, true);

            Instance(flag, "Long recall checkpoint", new Vector2(39f, 0f), Vector2.one).transform.SetParent(world, true);
            Instance(oneWay, "High shelf access", new Vector2(41f, 2.65f), new Vector2(4f, 0.3f))
                .transform.SetParent(world, true);
            Instance(conveyor, "High solid shelf", new Vector2(43f, 6.4f), new Vector2(4f, 0.8f))
                .transform.SetParent(world, true);
            Instance(oneWay, "High recall landing", new Vector2(48f, 6.65f), new Vector2(6f, 0.3f))
                .transform.SetParent(world, true);

            Label(world, "TAP / TAP", new Vector2(-6.5f, 6.9f), Cream, 0.35f);
            Label(world, "A LITTLE HEAD GOES A LONG WAY", new Vector2(-6.5f, 6.15f), Teal, 0.13f);
            StageLabel(world, "01", "ONE-WAY CLIMB", "Hold SPACE below the mint ledge.\nRelease to land the head and pull up.", 2f, 6.3f);
            StageLabel(world, "02", "CEILING STOP", "Tap SPACE early: extension still finishes.\nA ceiling stops the rise; walking out does not restart it.", 11.5f, 6.3f);
            StageLabel(world, "03", "SEPARATE / RECALL", "Extend beside the shelf, then walk under it.\nRelease: the body hits solid terrain.\nFollow the belt head and recall under the mint ledge.", 19f, 6.3f);
            StageLabel(world, "04", "HEAD SPRING", "Landing on a loose head gives a 3-unit bounce.\nMint ledges bypass the red hazard. Flags save your return point.", 29f, 6.3f);
            StageLabel(world, "05", "LONG RECALL", "Climb the low ledge; extend beside the high shelf.\nLeave your head on its belt, then drop to the floor.\nRecall from below the high mint landing for a 2-unit launch.", 40f, 10.3f);
            Label(world, "05   LONG RECALL\nStart by climbing this low mint ledge.",
                new Vector2(39f, 1.8f), Teal, 0.10f);
            Label(world, "PROTOTYPE COMPLETE", new Vector2(51.5f, 3.5f), Teal, 0.2f);

            GameObject killPlane = new GameObject("Fall safety");
            killPlane.transform.SetParent(world, false);
            killPlane.transform.position = new Vector2(24f, -5f);
            var trigger = killPlane.AddComponent<BoxCollider2D>();
            trigger.size = new Vector2(100f, 2f);
            trigger.isTrigger = true;
            killPlane.AddComponent<LethalZone>();
        }

        private static void BuildCamera(PlayerController player)
        {
            var follow = new GameObject("Camera follow");
            follow.transform.position = new Vector3(-1.5f, 3.5f, -10f);
            var shake = new GameObject("Camera shake");
            shake.transform.SetParent(follow.transform, false);
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(shake.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.6f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Ink;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            cameraObject.AddComponent<AudioListener>();
            follow.AddComponent<DemoCamera>().Configure(player, camera, shake.transform);
        }

        private static GameObject Visual(Transform parent, string name, Vector2 position,
            Vector2 size, Color color, Sprite sprite, int sortingOrder)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            root.transform.localScale = new Vector3(size.x, size.y, 1f);
            SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return root;
        }

        private static void StageLabel(Transform parent, string number, string title, string instructions, float x, float y)
        {
            Label(parent, number + "   " + title, new Vector2(x, y), Teal, 0.18f);
            Label(parent, instructions, new Vector2(x, y - 0.55f), new Color(0.65f, 0.75f, 0.80f), 0.10f);
        }

        private static void Label(Transform parent, string text, Vector2 position, Color color, float size)
        {
            var root = new GameObject(text.Split('\n')[0]);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(position.x, position.y, -0.1f);
            TextMesh label = root.AddComponent<TextMesh>();
            label.text = text;
            label.font = labelFont;
            label.fontSize = 64;
            label.characterSize = size / 3f;
            label.anchor = TextAnchor.UpperLeft;
            label.alignment = TextAlignment.Left;
            label.color = color;
            MeshRenderer renderer = root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = labelFont.material;
            renderer.sortingOrder = 5;
        }
    }
}
#endif
