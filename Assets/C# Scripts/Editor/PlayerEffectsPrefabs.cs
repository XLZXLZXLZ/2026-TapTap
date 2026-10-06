using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class PlayerEffectsPrefabs
    {
        public const string Folder = "Assets/Prefabs/Effects";
        private static string Path(string name) => Folder + "/" + name + ".prefab";

        public static void EnsurePrefabs(PlayerVisualConfig settings)
        {
            LevelPrefabBuilder.EnsureFolder(Folder);
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                Ensure("LandingParticles", preview, root => Particle(root, settings, false));
                Ensure("AssemblyParticles", preview, root => Particle(root, settings, true));
                Ensure("MagneticConnection", preview, root =>
                {
                    LineRenderer line = Line(root, settings.EffectsMaterial, settings.MagneticWidth, settings.MagneticColor);
                    line.positionCount = settings.MagneticSegments + 1;
                    line.SetPosition(0, Vector3.zero);
                    for (int i = 1; i < line.positionCount; i++) line.SetPosition(i, Vector3.up * i / (line.positionCount - 1) * 2f);
                    var dots = new SpriteRenderer[settings.MagneticDotCount];
                    for (int i = 0; i < dots.Length; i++)
                    {
                        var child = new GameObject("Flux mote " + i);
                        child.transform.SetParent(root.transform, false);
                        child.transform.localPosition = Vector3.up * (i + 1f) / (dots.Length + 1f) * 2f;
                        child.transform.localScale = Vector3.one * settings.MagneticDotSize;
                        dots[i] = child.AddComponent<SpriteRenderer>();
                        dots[i].sprite = settings.DotSprite;
                        dots[i].color = new Color(settings.MagneticColor.r, settings.MagneticColor.g, settings.MagneticColor.b, 0.75f);
                        dots[i].sortingOrder = 8;
                    }
                    root.AddComponent<MagneticConnectionView>().Configure(line, dots, settings.MagneticAmplitude, settings.MagneticSpeed, settings.MagneticSegments);
                });
                Ensure("RecallGuide", preview, root =>
                {
                    var line = Line(root, settings.EffectsMaterial, 0.025f, new Color(0.25f, 0.65f, 1f, 0.75f));
                    line.positionCount = 2; line.SetPosition(0, Vector3.zero); line.SetPosition(1, Vector3.down * 2f);
                });
                Ensure("RespawnOrb", preview, root =>
                {
                    root.transform.localScale = Vector3.one * settings.RespawnDotSize;
                    var sprite = root.AddComponent<SpriteRenderer>();
                    sprite.sprite = settings.DotSprite; sprite.color = new Color(0.35f, 0.94f, 0.88f); sprite.sortingOrder = 15;
                    var trail = root.AddComponent<TrailRenderer>();
                    trail.sharedMaterial = settings.EffectsMaterial; trail.time = settings.RespawnTrailTime;
                    trail.startWidth = settings.RespawnDotSize * 0.75f; trail.endWidth = 0f;
                    trail.startColor = sprite.color; trail.endColor = new Color(sprite.color.r, sprite.color.g, sprite.color.b, 0f);
                    trail.minVertexDistance = 0.025f; trail.numCapVertices = 4; trail.sortingOrder = 14; trail.emitting = false;
                });
                Ensure("GhostBlockOutline", preview, root =>
                {
                    var filter = root.AddComponent<MeshFilter>();
                    var renderer = root.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = settings.EffectsMaterial; renderer.sortingOrder = 3;
                    root.AddComponent<PhaseBlockOutline>().ConfigureRenderer(filter, renderer);
                });
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static void Ensure(string name, Scene scene, System.Action<GameObject> configure)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Path(name)) != null) return;
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            configure(root);
            PrefabUtility.SaveAsPrefabAsset(root, Path(name));
            Object.DestroyImmediate(root);
        }

        private static LineRenderer Line(GameObject root, Material material, float width, Color color)
        {
            var line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = material; line.useWorldSpace = true; line.widthMultiplier = width;
            line.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
            line.startColor = line.endColor = color;
            line.numCapVertices = 3; line.numCornerVertices = 2; line.sortingOrder = 7;
            return line;
        }

        private static void Particle(GameObject root, PlayerVisualConfig settings, bool assembly)
        {
            var system = root.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = false; main.loop = false; main.duration = 0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 64; main.gravityModifier = 0.12f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(settings.ParticleLifetime * 0.75f, settings.ParticleLifetime * 1.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(settings.ParticleSize * 0.7f, settings.ParticleSize * 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(settings.ParticleSpeed * 0.65f, settings.ParticleSpeed * 1.15f);
            main.startColor = assembly ? new Color(0.65f, 0.92f, 1f, 0.9f) : new Color(0.84f, 0.94f, 1f, 0.7f);
            var emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(assembly ? settings.AssemblyParticles : settings.LandingParticles)) });
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = settings.ParticleConeAngle; shape.radius = 0.025f;
            var fade = system.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var size = system.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = settings.ParticleMaterial; renderer.sortingOrder = 13;
        }

        private static GameObject Child(Transform parent, string name, string prefab)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing.gameObject;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Path(prefab)), parent);
            instance.name = name;
            return instance;
        }

        public static void BindPlayer(GameObject root, PlayerVisualConfig settings)
        {
            EnsurePrefabs(settings);
            Transform effects = root.transform.Find("Effects");
            if (effects == null) { effects = new GameObject("Effects").transform; effects.SetParent(root.transform, false); }
            var landing = Child(effects, "LandingParticles", "LandingParticles").GetComponent<ParticleSystem>();
            var assembly = Child(effects, "AssemblyParticles", "AssemblyParticles").GetComponent<ParticleSystem>();
            var magnet = Child(effects, "MagneticConnection", "MagneticConnection").GetComponent<MagneticConnectionView>();
            var guide = Child(effects, "RecallGuide", "RecallGuide").GetComponent<LineRenderer>();
            var bodyOrb = Child(effects, "Body respawn orb", "RespawnOrb");
            var headOrb = Child(effects, "Head respawn orb", "RespawnOrb");
            bodyOrb.SetActive(false); headOrb.SetActive(false);
            var contact = root.GetComponent<ContactParticles>();
            if (contact == null) contact = root.AddComponent<ContactParticles>();
            contact.Configure(landing, assembly);
            root.GetComponent<PlayerView>().ConfigureEffects(magnet, contact, guide);
            var returns = root.GetComponent<RespawnView>();
            if (returns == null) returns = root.AddComponent<RespawnView>();
            returns.ConfigureOrbs(bodyOrb.transform, headOrb.transform);
            LevelPrefabBuilder.RecordInstanceOverrides(root);
        }

        public static void BindRuntime(GameObject root, PlayerVisualConfig settings)
        {
            EnsurePrefabs(settings);
            var state = root.GetComponentInChildren<WorldPhaseState>(true);
            if (state == null) return;
            var old = state.GetComponent<PhaseBlockOutline>();
            var outline = Child(state.transform, "GhostBlockOutline", "GhostBlockOutline").GetComponent<PhaseBlockOutline>();
            if (old != null && old != outline)
            {
                EditorUtility.CopySerialized(old, outline);
                outline.ConfigureRenderer(outline.GetComponent<MeshFilter>(), outline.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(old);
            }
            LevelPrefabBuilder.RecordInstanceOverrides(root);
        }

        [MenuItem("TapTap/Prepare Effect Prefabs")]
        public static void PrepareAssets()
        {
            var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Characters/Player.prefab");
            try
            {
                GameplayVisualAssets.ConfigurePlayerVisuals(root);
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Characters/Player.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            LevelTestSceneBuilder.EnsureRuntimePrefab();
            AssetDatabase.SaveAssets();
            Debug.Log("Effect prefabs saved and bound to Player and GameplayRuntime.");
        }
    }
}
