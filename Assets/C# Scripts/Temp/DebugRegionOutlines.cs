#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TapTap
{
    // Editor-only overlay; no saved scene changes or dependency from gameplay scripts.
    [ExecuteAlways]
    public sealed class DebugRegionOutlines : MonoBehaviour
    {
        private static DebugRegionOutlines current;
        private static double nextEditorUpdate;
        [SerializeField, Min(0.001f)] private float lineWidth = 0.035f;
        [SerializeField, Tooltip("线框的世界坐标 Z，默认放在模型前方。")]
        private float lineZ = -0.1f;
        private readonly Dictionary<LevelRegion, Outline> outlines = new Dictionary<LevelRegion, Outline>();
        private readonly List<LevelRegion> removed = new List<LevelRegion>();
        private Material material;
        private float nextScan;

        private sealed class Outline
        {
            public LevelFootprint Footprint;
            public Vector2Int Size;
            public GameObject Root;
            public readonly List<LineRenderer> Lines = new List<LineRenderer>();
            public readonly List<Vector2> Points = new List<Vector2>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (current == null) current = FindObjectOfType<DebugRegionOutlines>();
            if (current == null)
                current = new GameObject("Debug Region Outlines (Editor Only)")
                    { hideFlags = HideFlags.DontSave }.AddComponent<DebugRegionOutlines>();
        }

        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeEditor()
        {
            UnityEditor.EditorApplication.update -= EditorUpdate;
            UnityEditor.EditorApplication.update += EditorUpdate;
        }

        private static void EditorUpdate()
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode
                || UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating) return;
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            if (now < nextEditorUpdate) return;
            nextEditorUpdate = now + 0.1;
            Bootstrap();
            if (!current.isActiveAndEnabled) return;
            current.Refresh((float)now);
            UnityEditor.SceneView.RepaintAll();
        }

        private void Awake()
        {
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            current = this;
            nextScan = 0f;
            material = new Material(Shader.Find("Hidden/Internal-Colored"))
                { name = "Debug region outline", hideFlags = HideFlags.HideAndDontSave };
            material.renderQueue = 5000;
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)CompareFunction.Always);
        }

        private void LateUpdate()
        {
            if (Application.isPlaying) Refresh(Time.unscaledTime);
        }

        private void Refresh(float now)
        {
            if (now >= nextScan)
            {
                nextScan = now + 0.5f;
                ScanRegions();
            }
            foreach (var entry in outlines)
            {
                LevelRegion region = entry.Key;
                if (region == null) continue;
                entry.Value.Root.SetActive(region.isActiveAndEnabled);
                for (int i = 0; i < entry.Value.Lines.Count; i++)
                {
                    LineRenderer line = entry.Value.Lines[i];
                    line.widthMultiplier = lineWidth * region.UnitSize;
                    Vector3 start = region.transform.TransformPoint(entry.Value.Points[i * 2] * region.UnitSize);
                    Vector3 end = region.transform.TransformPoint(entry.Value.Points[i * 2 + 1] * region.UnitSize);
                    start.z = end.z = lineZ;
                    line.SetPosition(0, start);
                    line.SetPosition(1, end);
                }
            }
        }

        private void ScanRegions()
        {
            removed.Clear();
            foreach (var entry in outlines)
                if (entry.Key == null || !entry.Key.gameObject.activeInHierarchy) removed.Add(entry.Key);
            foreach (LevelRegion region in removed)
            {
                RemoveObject(outlines[region].Root);
                outlines.Remove(region);
            }
            foreach (LevelRegion region in FindObjectsOfType<LevelRegion>())
            {
                LevelFootprint footprint = region.Source != null ? region.Source.Footprint : null;
                if (outlines.TryGetValue(region, out Outline old))
                {
                    if (old.Footprint == footprint && old.Size == region.Size) continue;
                    RemoveObject(old.Root);
                }
                outlines[region] = CreateOutline(region, footprint);
            }
        }

        private Outline CreateOutline(LevelRegion region, LevelFootprint footprint)
        {
            var outline = new Outline { Footprint = footprint, Size = region.Size,
                Root = new GameObject(region.name + " Debug Boundary") { hideFlags = HideFlags.HideAndDontSave } };
            outline.Root.transform.SetParent(transform, false);
            IReadOnlyList<LevelBoundaryEdge> edges;
            if (footprint != null) edges = footprint.Edges;
            else
            {
                Vector2Int size = region.Size;
                edges = new[]
                {
                    new LevelBoundaryEdge(Vector2Int.zero, new Vector2Int(size.x, 0), Vector2Int.down),
                    new LevelBoundaryEdge(new Vector2Int(0, size.y), size, Vector2Int.up),
                    new LevelBoundaryEdge(Vector2Int.zero, new Vector2Int(0, size.y), Vector2Int.left),
                    new LevelBoundaryEdge(new Vector2Int(size.x, 0), size, Vector2Int.right)
                };
            }
            Color color = Color.HSVToRGB(Mathf.Repeat(region.GetInstanceID() * 0.618034f, 1f), 0.65f, 1f);
            foreach (LevelBoundaryEdge edge in edges)
            {
                Vector2 tangent = ((Vector2)(edge.End - edge.Start)).normalized;
                if (tangent == Vector2.zero) continue;
                Vector2 inward = -(Vector2)edge.Outward * 0.5f;
                Vector2Int step = Vector2Int.RoundToInt(tangent);
                Vector2Int first = Vector2Int.FloorToInt((Vector2)edge.Start + tangent * 0.5f + inward);
                Vector2Int last = Vector2Int.FloorToInt((Vector2)edge.End - tangent * 0.5f + inward);
                // Convex corners shorten by half a cell; concave corners extend to meet the next inset edge.
                bool before = footprint != null && footprint.Contains(first - step);
                bool after = footprint != null && footprint.Contains(last + step);
                Vector2 start = (Vector2)edge.Start + inward + tangent * (before ? -0.5f : 0.5f);
                Vector2 end = (Vector2)edge.End + inward + tangent * (after ? 0.5f : -0.5f);
                if ((start - end).sqrMagnitude < 0.000001f) continue;
                var lineObject = new GameObject("Inset edge");
                lineObject.transform.SetParent(outline.Root.transform, false);
                var line = lineObject.AddComponent<LineRenderer>();
                line.sharedMaterial = material;
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.startColor = line.endColor = color;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.lightProbeUsage = LightProbeUsage.Off;
                line.reflectionProbeUsage = ReflectionProbeUsage.Off;
                line.sortingOrder = 32000;
                outline.Lines.Add(line);
                outline.Points.Add(start);
                outline.Points.Add(end);
            }
            return outline;
        }

        private void OnDisable()
        {
            foreach (Outline outline in outlines.Values)
                if (outline.Root != null) RemoveObject(outline.Root);
            outlines.Clear();
            if (material != null) RemoveObject(material);
            material = null;
        }

        private void OnDestroy()
        {
            if (current == this) current = null;
        }

        private static void RemoveObject(Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
#endif
