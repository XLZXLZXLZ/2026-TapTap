using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TapTap
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(600)]
    public sealed class PhaseBlockOutline : MonoBehaviour
    {
        [SerializeField, Min(0.005f)] private float width = 0.055f;
        [SerializeField, Min(0.01f)] private float dashLength = 0.18f;
        [SerializeField, Min(0.01f)] private float gapLength = 0.11f;
        [SerializeField] private Color color = new Color(0.52f, 0.85f, 1f, 0.78f);
        private const float Precision = 10000f;
        private struct Edge
        {
            public Vector2Int From;
            public Vector2Int To;
            public Edge(Vector2Int from, Vector2Int to) { From = from; To = to; }
        }
        private readonly HashSet<Edge> edges = new HashSet<Edge>();
        private readonly Dictionary<Edge, float> edgeOpacity = new Dictionary<Edge, float>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<Color> colors = new List<Color>();
        private WorldPhaseState state;
        private Mesh mesh;
        [SerializeField] private MeshFilter meshFilter;
        [SerializeField] private MeshRenderer meshRenderer;
        private bool dirty;

        internal void MarkDirty(WorldPhaseState source) { state = source; dirty = true; }
        public void ConfigureRenderer(MeshFilter filter, MeshRenderer renderer) { meshFilter = filter; meshRenderer = renderer; }

        private void LateUpdate()
        {
            if (state == null) return;
            foreach (PhaseBlock block in state.Blocks)
                if (block != null && block.transform.hasChanged)
                {
                    block.transform.hasChanged = false;
                    dirty = true;
                }
            if (!dirty) return;
            dirty = false;
            Rebuild();
        }

        private void EnsureMesh()
        {
            if (mesh != null) return;
            if (meshFilter == null || meshRenderer == null) return;
            mesh = new Mesh { name = "Ghost block perimeter", indexFormat = IndexFormat.UInt32 };
            meshFilter.sharedMesh = mesh;
        }

        private static Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.RoundToInt(p.x * Precision), Mathf.RoundToInt(p.y * Precision));
        private static Vector2 Point(Vector2Int key) => (Vector2)key / Precision;
        private void AddEdge(Vector2Int a, Vector2Int b, float opacity)
        {
            Edge opposite = new Edge(b, a);
            if (edges.Remove(opposite)) edgeOpacity.Remove(opposite);
            else { Edge edge = new Edge(a, b); edges.Add(edge); edgeOpacity[edge] = opacity; }
        }

        private void Rebuild()
        {
            EnsureMesh();
            if (mesh == null) return;
            edges.Clear(); edgeOpacity.Clear(); vertices.Clear(); triangles.Clear(); colors.Clear();
            float unit = 1f;
            foreach (PhaseBlock block in state.Blocks)
            {
                if (block == null || block.IsSolid || block.GetComponent<PhasePiston>() != null) continue;
                RegionRevealView reveal = block.GetComponentInParent<RegionRevealView>();
                float opacity = reveal != null ? reveal.Opacity : 1f;
                if (opacity <= 0f) continue;
                Bounds bounds = block.WorldBounds;
                unit = bounds.size.x;
                Vector2Int a = Key(bounds.min), c = Key(bounds.max);
                var b = new Vector2Int(c.x, a.y);
                var d = new Vector2Int(a.x, c.y);
                AddEdge(a, b, opacity); AddEdge(b, c, opacity); AddEdge(c, d, opacity); AddEdge(d, a, opacity);
            }
            var outgoing = new Dictionary<Vector2Int, List<Edge>>();
            foreach (Edge edge in edges)
            {
                if (!outgoing.TryGetValue(edge.From, out var list)) outgoing.Add(edge.From, list = new List<Edge>());
                list.Add(edge);
            }
            while (edges.Count > 0)
            {
                Edge start = default;
                foreach (Edge edge in edges) { start = edge; break; }
                Edge current = start;
                float travelled = 0f;
                while (edges.Remove(current))
                {
                    DashSegment(Point(current.From), Point(current.To), ref travelled, unit, edgeOpacity[current]);
                    if (current.To == start.From || !outgoing.TryGetValue(current.To, out var next)) break;
                    bool found = false;
                    float bestTurn = -361f;
                    Edge selected = default;
                    Vector2 heading = (Vector2)(current.To - current.From);
                    foreach (Edge candidate in next)
                    {
                        if (!edges.Contains(candidate)) continue;
                        float turn = Vector2.SignedAngle(heading, (Vector2)(candidate.To - candidate.From));
                        if (!found || turn > bestTurn) { found = true; bestTurn = turn; selected = candidate; }
                    }
                    if (!found) break;
                    current = selected;
                }
            }
            mesh.Clear();
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private void DashSegment(Vector2 from, Vector2 to, ref float travelled, float unit, float opacity)
        {
            Vector2 direction = (to - from).normalized;
            float length = Vector2.Distance(from, to);
            float dash = Mathf.Max(0.01f, dashLength) * unit;
            float period = dash + Mathf.Max(0.01f, gapLength) * unit;
            float cursor = 0f;
            while (cursor < length - 0.00001f)
            {
                float phase = Mathf.Repeat(travelled + cursor, period);
                bool visible = phase < dash;
                float end = Mathf.Min(length, cursor + (visible ? dash : period) - phase);
                if (end <= cursor + 0.00001f) end = Mathf.Min(length, cursor + 0.0001f);
                if (visible) Quad(from + direction * cursor, from + direction * end, width * unit, opacity);
                cursor = end;
            }
            travelled += length;
        }

        private void Quad(Vector2 a, Vector2 b, float thickness, float opacity)
        {
            Vector2 direction = (b - a).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) * thickness * 0.5f;
            int first = vertices.Count;
            vertices.Add(transform.InverseTransformPoint(a - normal));
            vertices.Add(transform.InverseTransformPoint(a + normal));
            vertices.Add(transform.InverseTransformPoint(b + normal));
            vertices.Add(transform.InverseTransformPoint(b - normal));
            Color tint = color; tint.a *= opacity;
            for (int i = 0; i < 4; i++) colors.Add(tint);
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
