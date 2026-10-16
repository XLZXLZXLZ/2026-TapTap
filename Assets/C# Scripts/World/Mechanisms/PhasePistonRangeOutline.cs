using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TapTap
{
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(PhasePiston))]
    public sealed class PhasePistonRangeOutline : MonoBehaviour
    {
        [SerializeField] private Material material;
        [SerializeField] private Color color = new Color(1f, 0.84f, 0.12f, 0.95f);
        [SerializeField, Min(0.005f)] private float width = 0.04f;
        [SerializeField, Min(0.01f)] private float dashLength = 0.18f;
        [SerializeField, Min(0.01f)] private float gapLength = 0.11f;
        private PhasePiston piston;
        private GameObject outline;
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private int cachedLength = -1, cachedTurns = -1;
        private bool dirty;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<Color> colors = new List<Color>();

        private void OnEnable()
        {
            if (!gameObject.scene.IsValid()) return;
            piston = GetComponent<PhasePiston>();
            Rebuild();
            // A dynamically created renderer must follow the region's discovery visibility too.
            if (Application.isPlaying) GetComponentInParent<RegionRevealView>()?.RefreshVisuals();
        }

        private void OnValidate() => dirty = true;

        private void LateUpdate()
        {
            if (piston != null && (dirty || cachedLength != piston.MaxLength || cachedTurns != piston.QuarterTurns)) Rebuild();
        }

        private void Rebuild()
        {
            if (outline == null)
            {
                outline = new GameObject("Piston Range Outline") { hideFlags = HideFlags.HideAndDontSave, layer = gameObject.layer };
                outline.transform.SetParent(transform, false);
                mesh = new Mesh { name = "Piston dashed range", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                outline.AddComponent<MeshFilter>().sharedMesh = mesh;
                meshRenderer = outline.AddComponent<MeshRenderer>();
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                meshRenderer.lightProbeUsage = LightProbeUsage.Off;
                meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                meshRenderer.sortingOrder = 7;
            }
            outline.SetActive(true);
            meshRenderer.sharedMaterial = material;
            cachedLength = piston.MaxLength;
            cachedTurns = piston.QuarterTurns;
            dirty = false;
            Vector2 direction = new PistonPlacementSettings { QuarterTurns = cachedTurns }.Direction;
            // Keep the maximum extension length, with the hint centered on the model's depth
            // and fitted to its 0.82-cell body width.
            outline.transform.localPosition = new Vector3(-direction.x * 0.5f, -direction.y * 0.5f, 0.5f);
            outline.transform.localRotation = Quaternion.Euler(0f, 0f, -cachedTurns * 90f);
            vertices.Clear(); triangles.Clear(); colors.Clear();
            float travelled = 0f;
            const float halfWidth = 0.41f;
            Edge(new Vector2(0f, -halfWidth), new Vector2(cachedLength, -halfWidth), ref travelled);
            Edge(new Vector2(cachedLength, -halfWidth), new Vector2(cachedLength, halfWidth), ref travelled);
            Edge(new Vector2(cachedLength, halfWidth), new Vector2(0f, halfWidth), ref travelled);
            Edge(new Vector2(0f, halfWidth), new Vector2(0f, -halfWidth), ref travelled);
            mesh.Clear();
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private void Edge(Vector2 from, Vector2 to, ref float travelled)
        {
            Vector2 direction = (to - from).normalized;
            float length = Vector2.Distance(from, to);
            float dash = Mathf.Max(0.01f, dashLength), period = dash + Mathf.Max(0.01f, gapLength);
            for (float cursor = 0f; cursor < length - 0.00001f;)
            {
                float phase = Mathf.Repeat(travelled + cursor, period);
                bool visible = phase < dash;
                float end = Mathf.Min(length, cursor + (visible ? dash : period) - phase);
                if (end <= cursor + 0.00001f) end = Mathf.Min(length, cursor + 0.0001f);
                if (visible)
                {
                    Vector2 a = from + direction * cursor, b = from + direction * end;
                    Vector2 normal = new Vector2(-direction.y, direction.x) * width * 0.5f;
                    int first = vertices.Count;
                    vertices.Add(a - normal); vertices.Add(a + normal); vertices.Add(b + normal); vertices.Add(b - normal);
                    for (int i = 0; i < 4; i++) colors.Add(color);
                    triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                    triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
                }
                cursor = end;
            }
            travelled += length;
        }

        private void OnDisable() { if (outline != null) outline.SetActive(false); }

        private void OnDestroy()
        {
            if (Application.isPlaying)
            {
                if (outline != null) Destroy(outline);
                if (mesh != null) Destroy(mesh);
            }
            else
            {
                if (outline != null) DestroyImmediate(outline);
                if (mesh != null) DestroyImmediate(mesh);
            }
        }
    }
}
