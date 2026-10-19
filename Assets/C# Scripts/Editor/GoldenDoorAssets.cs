using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Explicitly updates only the reusable door prefab. Does not save loaded gameplay scenes.
    public static class GoldenDoorAssets
    {
        public const string ModelPath = "Assets/Art/3D Resources/ReturnJourney/GoldenDoor.fbx";
        public const string MaterialFolder = "Assets/Art/Materials/ReturnJourney";
        public const string ShaderPath = "Assets/Resources/Shaders/GoldenDoorDissolve.shader";
        public const string PreviewPath = "Assets/Scenes/GoldenDoorPreview.unity";

        [MenuItem("TapTap/维护/返程/更新卡通金色溶解门")]
        public static void Build()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ReturnJourneyAssets.DoorPath);
            try
            {
                ConfigureDoor(root);
                PrefabUtility.SaveAsPrefabAsset(root, ReturnJourneyAssets.DoorPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("已更新 1×3 Unlit 金色圆角门；未保存任何游玩场景。");
        }

        public static void ConfigureDoor(GameObject root)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (model == null || shader == null) throw new InvalidOperationException("Golden door FBX or Unlit shader is missing.");
            LevelPrefabBuilder.EnsureFolder(MaterialFolder);
            Material gold = Material("GoldenDoor_Gold", shader, new Color(1f, 0.65f, 0.16f));
            Material trim = Material("GoldenDoor_Trim", shader, new Color(1f, 0.82f, 0.34f));
            Material recess = Material("GoldenDoor_Recess", shader, new Color(0.17f, 0.085f, 0.025f));
            Transform view = root.transform.Find("View");
            if (view == null) view = Child(root.transform, "View", Vector3.zero);
            for (int i = view.childCount - 1; i >= 0; i--) Object.DestroyImmediate(view.GetChild(i).gameObject);
            view.localPosition = Vector3.zero;
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.scene);
            instance.transform.SetParent(view, false);
            instance.name = "Golden Door Model";
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>())
            {
                renderer.sharedMaterial = renderer.name.Contains("Recess") ? recess
                    : renderer.name.Contains("Trim") ? trim : gold;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            var barrier = root.GetComponent<BoxCollider2D>();
            barrier.size = new Vector2(1f, 3f);
            barrier.offset = Vector2.up;
            barrier.isTrigger = false;
            Transform socket = root.transform.Find("KeySocket") ?? Child(root.transform, "KeySocket", Vector3.zero);
            socket.localPosition = new Vector3(0f, 1f, -0.08f);
            Transform sensor = root.transform.Find("Body Proximity") ?? Child(root.transform, "Body Proximity", Vector3.zero);
            sensor.localPosition = new Vector3(0.9f, 1f, 0f);
            var proximity = sensor.GetComponent<BoxCollider2D>();
            if (proximity == null) proximity = sensor.gameObject.AddComponent<BoxCollider2D>();
            proximity.isTrigger = true;
            proximity.size = new Vector2(2.5f, 3f);
            var visual = view.GetComponent<GoldenDoorVisual>();
            if (visual == null) visual = view.gameObject.AddComponent<GoldenDoorVisual>();
            visual.Renderers = instance.GetComponentsInChildren<Renderer>();
            visual.LockCentre = Vector3.up;
            visual.DissolveSparks = Sparks(view);
            var door = root.GetComponent<ReturnJourneyDoor>();
            if (door == null) door = root.AddComponent<ReturnJourneyDoor>();
            door.View = view;
            door.DoorVisual = visual;
            door.KeySocket = socket;
            door.Proximity = proximity;
            door.KeyTravelDuration = 0.55f;
            door.OpenDuration = 0.9f;
        }

        private static Material Material(string name, Shader shader, Color color)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Transform Child(Transform parent, string name, Vector3 position)
        {
            var owner = new GameObject(name);
            owner.transform.SetParent(parent, false);
            owner.transform.localPosition = position;
            return owner.transform;
        }

        private static ParticleSystem Sparks(Transform parent)
        {
            string path = MaterialFolder + "/GoldenDoor_Sparks.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                material.SetColor("_BaseColor", new Color(4f, 2.2f, 0.3f, 0.8f));
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 2f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.One);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = 3000;
                AssetDatabase.CreateAsset(material, path);
            }
            var particles = Child(parent, "Golden dissolve sparks", new Vector3(0f, 1f, -0.09f)).gameObject.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = main.loop = false;
            main.duration = 0.8f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.85f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.045f);
            main.startColor = Color.white;
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.14f;
            var fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.6f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 14;
            return particles;
        }

        // Isolated batch authoring returns saved assets + a real Unity camera preview.
        public static void BuildBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            Build();
            GoldenDoorValidation.CreatePreview();
            GoldenDoorValidation.ValidateAndCapture();
        }
    }
}
