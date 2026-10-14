using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    [ExecuteAlways, DisallowMultipleComponent]
    [DefaultExecutionOrder(-120)]
    [AddComponentMenu("TapTap/World/Progress Key")]
    [RequireComponent(typeof(BoxCollider2D), typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ProgressKey : MonoBehaviour
    {
        [Header("拾取强调（真实秒数）")]
        [Range(0.05f, 0.6f)] public float ZoomAmount = 0.2f;
        [Min(0.01f)] public float ZoomInDuration = 0.18f;
        [Min(0f)] public float ZoomHoldDuration = 0.1f;
        [Min(0.01f)] public float ZoomOutDuration = 0.42f;
        [Range(0.01f, 1f)] public float SlowMotionScale = 0.15f;
        public Color Tint = new Color(1f, 0.82f, 0.3f);
        private Mesh mesh;
        private MeshRenderer visual;
        private BoxCollider2D trigger;
        private bool visualDirty;
        public bool IsCollected { get; private set; }

        private void OnEnable()
        {
            RebuildVisual();
            ApplyStage();
        }

        public void ApplyStage() => SetCollected(Application.isPlaying && GameStageProgression.HasProgressedIn(gameObject.scene));

        public bool TryCollect(PlayerController player) => !IsCollected
            && GameStageProgression.ForScene(gameObject.scene).Collect(this, player);

        public void SetCollected(bool value)
        {
            IsCollected = value;
            if (visual == null) visual = GetComponent<MeshRenderer>();
            if (trigger == null) trigger = GetComponent<BoxCollider2D>();
            visual.enabled = trigger.enabled = !value;
        }

        public void RebuildVisual()
        {
            if (mesh == null) mesh = new Mesh { name = "Progress key silhouette", hideFlags = HideFlags.HideAndDontSave };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            const int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float b = (i + 1) * Mathf.PI * 2f / segments;
                Vector2 center = new Vector2(-0.23f, 0.12f);
                Vector2 va = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 vb = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                Quad(vertices, triangles, center + va * 0.23f, center + va * 0.13f,
                    center + vb * 0.13f, center + vb * 0.23f);
            }
            Quad(vertices, triangles, new Vector2(-0.04f, 0.17f), new Vector2(-0.04f, 0.07f),
                new Vector2(0.43f, 0.07f), new Vector2(0.43f, 0.17f));
            for (int i = 0; i < 2; i++)
            {
                float x = 0.2f + i * 0.14f;
                Quad(vertices, triangles, new Vector2(x, 0.08f), new Vector2(x, -0.12f),
                    new Vector2(x + 0.09f, -0.12f), new Vector2(x + 0.09f, 0.08f));
            }
            var colors = new Color[vertices.Count];
            for (int i = 0; i < colors.Length; i++) colors[i] = Tint;
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.colors = colors;
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            visual = GetComponent<MeshRenderer>();
            if (visual.sharedMaterial == null) visual.sharedMaterial = Resources.Load<Material>("Materials/IndicatorVisual");
            visual.sortingOrder = 12;
            trigger = GetComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(0.94f, 0.52f);
            trigger.offset = new Vector2(-0.01f, 0.09f);
        }

        private static void Quad(List<Vector3> vertices, List<int> triangles, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int index = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
            triangles.Add(index); triangles.Add(index + 2); triangles.Add(index + 3);
        }

        private void OnValidate() => visualDirty = true;
        private void Update()
        {
            if (!visualDirty) return;
            visualDirty = false;
            RebuildVisual();
        }
        private void OnDestroy()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        }
    }
}
