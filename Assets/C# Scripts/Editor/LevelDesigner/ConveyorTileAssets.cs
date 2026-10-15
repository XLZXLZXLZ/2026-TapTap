using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class ConveyorTileAssets
    {
        public const string PrefabPath = "Assets/Prefabs/Terrain/Grid/GridConveyor.prefab";
        public const string ModelsFolder = "Assets/Prefabs/Terrain/Grid/SmartModels";
        public const string GlowPath = ModelsFolder + "/ConveyorArrowGlow.mat";

        public static void Ensure()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return;
            if (prefab.GetComponent<ConveyorTileVisual>() != null) { EnsureGlowPreset(); return; }
            LevelPrefabBuilder.EnsureFolder(ModelsFolder);
            Material normal = Material("ConveyorArrow", new Color(0.35f, 0.27f, 0.1f), Color.black);
            Material glow = Material("ConveyorArrowGlow", new Color(0.02f, 1f, 0.65f), new Color(0.16f, 8f, 5.2f));
            Mesh arrowMesh = ArrowMesh();
            GameObject left = Model("ConveyorLeft", true, false, null, null);
            GameObject middle = Model("ConveyorMiddle", false, false, arrowMesh, normal);
            GameObject right = Model("ConveyorRight", false, true, null, null);
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var view = root.AddComponent<ConveyorTileVisual>();
                view.LeftModel = left; view.MiddleModel = middle; view.RightModel = right;
                view.ArrowGlowMaterial = glow;
                var legacy = new List<GameObject>();
                foreach (Transform child in root.transform)
                    if (child.GetComponentInChildren<Renderer>(true) != null) legacy.Add(child.gameObject);
                view.LegacyVisuals = legacy.ToArray();
                view.ApplyPart(ConveyorPart.Middle);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            EnsureGlowPreset();
        }

        private static void EnsureGlowPreset()
        {
            Material glow = AssetDatabase.LoadAssetAtPath<Material>(GlowPath);
            if (glow == null || !glow.HasProperty("_EmissionColor") || glow.GetColor("_EmissionColor").maxColorComponent <= 0f) return;
            if (glow.IsKeywordEnabled("_EMISSION") && (glow.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0) return;
            EnableEmission(glow);
        }

        private static void EnableEmission(Material material)
        {
            // URP's first-import material upgrade can reset keywords; apply again to the imported asset.
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            MaterialEditor.FixupEmissiveFlag(material);
            material.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        }

        private static Material Material(string name, Color color, Color emission)
        {
            string path = ModelsFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find("TapTap/ConveyorArrow");
            if (shader == null) throw new System.InvalidOperationException("缺少 ConveyorArrow Shader。");
            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetColor("_EmissionColor", emission);
            material.globalIlluminationFlags = emission.maxColorComponent > 0f
                ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            if (emission.maxColorComponent > 0f) material.EnableKeyword("_EMISSION");
            AssetDatabase.CreateAsset(material, path);
            if (emission.maxColorComponent > 0f) EnableEmission(material);
            return material;
        }

        private static Mesh ArrowMesh()
        {
            string path = ModelsFolder + "/ConveyorArrowMesh.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < 2; i++)
            {
                float x = -0.2f + i * 0.27f;
                Stroke(new Vector2(x - 0.075f, 0.1f), new Vector2(x + 0.075f, 0f), vertices, triangles);
                Stroke(new Vector2(x + 0.075f, 0f), new Vector2(x - 0.075f, -0.1f), vertices, triangles);
            }
            mesh = new Mesh { name = "Conveyor >>" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static void Stroke(Vector2 from, Vector2 to, List<Vector3> vertices, List<int> triangles)
        {
            Vector2 delta = (to - from).normalized;
            Vector2 normal = new Vector2(-delta.y, delta.x) * 0.026f;
            int start = vertices.Count;
            vertices.Add(from - normal); vertices.Add(from + normal); vertices.Add(to - normal); vertices.Add(to + normal);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start + 2); triangles.Add(start + 1); triangles.Add(start + 3);
        }

        private static GameObject Model(string name, bool left, bool right, Mesh arrowMesh, Material arrowMaterial)
        {
            string path = ModelsFolder + "/" + name + ".prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject(name);
                SceneManager.MoveGameObjectToScene(root, preview);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Sprite/Prototype/Square.png");
                Color color = new Color(0.38f, 0.3f, 0.56f), edge = new Color(0.7f, 0.62f, 0.82f);
                Sprite(root.transform, "Body", sprite, Vector2.zero, Vector2.one, color);
                Sprite(root.transform, "Top edge", sprite, new Vector2(0f, 0.475f), new Vector2(1f, 0.05f), edge);
                if (left) Sprite(root.transform, "Left cap", sprite, new Vector2(-0.46f, 0f), new Vector2(0.08f, 1f), edge);
                if (right) Sprite(root.transform, "Right cap", sprite, new Vector2(0.46f, 0f), new Vector2(0.08f, 1f), edge);
                if (arrowMesh != null)
                {
                    var arrow = new GameObject("Arrow >>");
                    arrow.transform.SetParent(root.transform, false);
                    arrow.transform.localPosition = new Vector3(0.06f, 0.28f, -0.04f);
                    arrow.AddComponent<MeshFilter>().sharedMesh = arrowMesh;
                    MeshRenderer renderer = arrow.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = arrowMaterial;
                    renderer.sortingOrder = 3;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    ConveyorArrowPulse pulse = arrow.AddComponent<ConveyorArrowPulse>();
                    pulse.ArrowMaterials = new[] { new ConveyorArrowMaterialSlot { Renderer = renderer, MaterialIndex = 0 } };
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static void Sprite(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 size, Color color)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false); child.transform.localPosition = position;
            child.transform.localScale = new Vector3(size.x, size.y, 1f);
            var renderer = child.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.color = color;
            renderer.sortingOrder = name == "Body" ? 0 : 1;
        }
    }
}
