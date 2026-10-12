using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    [Serializable]
    public sealed class LevelOutlineLoop
    {
        public Vector2[] Points;
        public LevelOutlineLoop(Vector2[] points) { Points = points; }
    }

    // Baked grid-space contours. Shared by every placement of the same level.
    public sealed class LevelOutlineData : ScriptableObject
    {
        public List<LevelOutlineLoop> Loops = new List<LevelOutlineLoop>();
        public Vector2 LabelPosition;
        public RectInt Bounds;

        public void Build(LevelFootprint footprint)
        {
            Bounds = footprint.Bounds;
            Loops.Clear();
            var remaining = new List<LevelBoundaryEdge>();
            foreach (LevelBoundaryEdge edge in footprint.Edges)
            {
                // Keep the occupied area to the left of each directed edge.
                remaining.Add(edge.Outward == Vector2Int.up || edge.Outward == Vector2Int.left
                    ? new LevelBoundaryEdge(edge.End, edge.Start, edge.Outward) : edge);
            }
            while (remaining.Count > 0)
            {
                LevelBoundaryEdge edge = remaining[0];
                remaining.RemoveAt(0);
                Vector2Int start = edge.Start;
                var points = new List<Vector2> { start };
                while (edge.End != start)
                {
                    points.Add(edge.End);
                    int next = -1;
                    float best = float.NegativeInfinity;
                    Vector2 direction = edge.End - edge.Start;
                    for (int i = 0; i < remaining.Count; i++)
                    {
                        if (remaining[i].Start != edge.End) continue;
                        Vector2 outgoing = remaining[i].End - remaining[i].Start;
                        float turn = Mathf.Atan2(direction.x * outgoing.y - direction.y * outgoing.x,
                            Vector2.Dot(direction, outgoing));
                        // At a shared corner, a left turn keeps diagonal islands separate.
                        if (turn > best) { best = turn; next = i; }
                    }
                    if (next < 0) throw new InvalidOperationException("关卡轮廓不是闭合边界。");
                    edge = remaining[next];
                    remaining.RemoveAt(next);
                }
                for (int i = points.Count - 1; i >= 0 && points.Count > 3; i--)
                {
                    Vector2 before = points[(i + points.Count - 1) % points.Count];
                    Vector2 after = points[(i + 1) % points.Count];
                    Vector2 a = points[i] - before, b = after - points[i];
                    if (Mathf.Abs(a.x * b.y - a.y * b.x) < 0.001f && Vector2.Dot(a, b) > 0f)
                        points.RemoveAt(i);
                }
                Loops.Add(new LevelOutlineLoop(points.ToArray()));
            }
            float closest = float.PositiveInfinity;
            LabelPosition = Bounds.center;
            foreach (Vector2Int cell in footprint.Cells)
            {
                Vector2 center = (Vector2)cell + Vector2.one * 0.5f;
                float distance = (center - Bounds.center).sqrMagnitude;
                if (distance < closest) { closest = distance; LabelPosition = center; }
            }
        }
    }
}
