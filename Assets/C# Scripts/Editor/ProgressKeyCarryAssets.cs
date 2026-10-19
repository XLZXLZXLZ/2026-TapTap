using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    public static class ProgressKeyCarryAssets
    {
        public const string PrefabPath = "Assets/Resources/Prefabs/CarriedProgressKey.prefab";
        public const string MeshPath = "Assets/Resources/Models/CarriedProgressKey.asset";
        public const string MaterialPath = "Assets/Resources/Materials/CarriedProgressKeyGlow.mat";

        [MenuItem("TapTap/维护/返程/创建携带钥匙荧光预制体")]
        public static void Build()
        {
            LevelPrefabBuilder.EnsureFolder("Assets/Resources/Prefabs");
            LevelPrefabBuilder.EnsureFolder("Assets/Resources/Models");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var source = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(ReturnJourneyAssets.KeyPath), scene);
                var key = source.GetComponent<ProgressKey>();
                key.RebuildVisual();
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
                if (mesh == null)
                {
                    mesh = Object.Instantiate(source.GetComponent<MeshFilter>().sharedMesh);
                    mesh.name = "Carried Progress Key";
                    mesh.hideFlags = HideFlags.None;
                    // Tint is supplied by the instance property block at runtime.
                    var colors = new Color[mesh.vertexCount];
                    for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
                    mesh.colors = colors;
                    AssetDatabase.CreateAsset(mesh, MeshPath);
                }
                Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                if (material == null)
                {
                    material = new Material(Resources.Load<Material>(ProgressKey.OverlayMaterialPath));
                    material.name = "CarriedProgressKeyGlow";
                    material.SetFloat("_GlowIntensity", 3f);
                    AssetDatabase.CreateAsset(material, MaterialPath);
                }
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                {
                    var root = new GameObject("Carried Progress Key");
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                    root.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = root.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.sortingOrder = 30;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.allowOcclusionWhenDynamic = false;
                    var effects = new GameObject("Particle Effects").transform;
                    effects.SetParent(root.transform, false);
                    CreateGlow(effects, key.GlowParticleMaterial);
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }

            GameObject contents = PrefabUtility.LoadPrefabContents(ReturnJourneyAssets.KeyPath);
            try
            {
                var key = contents.GetComponent<ProgressKey>();
                if (key.CarryVisualPrefab == null)
                {
                    key.CarryVisualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                    PrefabUtility.SaveAsPrefabAsset(contents, ReturnJourneyAssets.KeyPath);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            AssetDatabase.SaveAssets();
        }

        private static void CreateGlow(Transform parent, Material material)
        {
            var owner = new GameObject("White Glow");
            owner.transform.SetParent(parent, false);
            owner.transform.localPosition = new Vector3(-0.01f, 0.09f, -0.04f);
            var particles = owner.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.startColor = Color.white;
            main.maxParticles = 24;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = particles.emission;
            emission.rateOverTime = 8f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.28f;
            shape.radiusThickness = 1f;
            var fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.65f, 0.2f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = owner.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 31;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public static void BuildBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            Build();
        }
    }
}
