using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    public struct LevelBoundaryEdge
    {
        public Vector2Int Start, End, Outward;
        public LevelBoundaryEdge(Vector2Int start, Vector2Int end, Vector2Int outward)
        { Start = start; End = end; Outward = outward; }
    }

    // Grid occupancy, rather than its enclosing rectangle, defines a room's area.
    public sealed class LevelFootprint
    {
        private static readonly Vector2Int[] Directions =
            { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up };
        private readonly HashSet<Vector2Int> occupied;
        public IReadOnlyList<Vector2Int> Cells { get; }
        public IReadOnlyList<RectInt> Runs { get; }
        public IReadOnlyList<LevelBoundaryEdge> Edges { get; }
        public RectInt Bounds { get; }
        public bool Contains(Vector2Int cell) => occupied.Contains(cell);

        public LevelFootprint(IEnumerable<Vector2Int> cells)
        {
            occupied = new HashSet<Vector2Int>(cells);
            var ordered = new List<Vector2Int>(occupied);
            ordered.Sort((a, b) => a.y == b.y ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            Cells = ordered;
            Bounds = GetBounds(ordered);
            var runs = new List<RectInt>();
            foreach (Vector2Int cell in ordered)
            {
                if (runs.Count > 0 && runs[runs.Count - 1].y == cell.y && runs[runs.Count - 1].xMax == cell.x)
                {
                    RectInt run = runs[runs.Count - 1]; run.width++; runs[runs.Count - 1] = run;
                }
                else runs.Add(new RectInt(cell, Vector2Int.one));
            }
            Runs = runs;
            var edges = new List<LevelBoundaryEdge>();
            foreach (Vector2Int cell in ordered)
            {
                if (!Contains(cell + Vector2Int.down)) edges.Add(new LevelBoundaryEdge(cell, cell + Vector2Int.right, Vector2Int.down));
                if (!Contains(cell + Vector2Int.up)) edges.Add(new LevelBoundaryEdge(cell + Vector2Int.up, cell + Vector2Int.one, Vector2Int.up));
                if (!Contains(cell + Vector2Int.left)) edges.Add(new LevelBoundaryEdge(cell, cell + Vector2Int.up, Vector2Int.left));
                if (!Contains(cell + Vector2Int.right)) edges.Add(new LevelBoundaryEdge(cell + Vector2Int.right, cell + Vector2Int.one, Vector2Int.right));
            }
            edges.Sort((a, b) =>
            {
                int normal = a.Outward.x.CompareTo(b.Outward.x);
                if (normal == 0) normal = a.Outward.y.CompareTo(b.Outward.y);
                if (normal != 0) return normal;
                int line = a.Outward.x == 0 ? a.Start.y.CompareTo(b.Start.y) : a.Start.x.CompareTo(b.Start.x);
                return line != 0 ? line : a.Outward.x == 0 ? a.Start.x.CompareTo(b.Start.x) : a.Start.y.CompareTo(b.Start.y);
            });
            var merged = new List<LevelBoundaryEdge>();
            foreach (LevelBoundaryEdge edge in edges)
            {
                if (merged.Count > 0 && merged[merged.Count - 1].Outward == edge.Outward && merged[merged.Count - 1].End == edge.Start)
                {
                    LevelBoundaryEdge last = merged[merged.Count - 1]; last.End = edge.End; merged[merged.Count - 1] = last;
                }
                else merged.Add(edge);
            }
            Edges = merged;
        }

        public static HashSet<Vector2Int> PlacedCells(LevelDefinition level)
        {
            var cells = new HashSet<Vector2Int>();
            if (level.Placements != null)
                foreach (LevelPlacement placement in level.Placements)
                    if (placement?.Brush != null && placement.Brush.HandlerId != "annotation") cells.Add(placement.Cell);
            return cells;
        }

        public static LevelFootprint Build(LevelDefinition level)
        {
            HashSet<Vector2Int> cells = PlacedCells(level);
            if (level.UseCustomBoundary)
            {
                if (level.BoundaryCells != null) cells.UnionWith(level.BoundaryCells);
                if (cells.Count > 0) return new LevelFootprint(cells);
            }
            if (cells.Count == 0)
            {
                foreach (Vector2Int cell in level.CanvasBounds.allPositionsWithin) cells.Add(cell);
                return new LevelFootprint(cells);
            }
            // Enclosed room interiors belong to the level; open concavities remain outside.
            RectInt bounds = GetBounds(cells);
            var floodBounds = new RectInt(bounds.min - Vector2Int.one, bounds.size + Vector2Int.one * 2);
            var exterior = new HashSet<Vector2Int> { floodBounds.min };
            var queue = new Queue<Vector2Int>(); queue.Enqueue(floodBounds.min);
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int next = cell + direction;
                    if (floodBounds.Contains(next) && !cells.Contains(next) && exterior.Add(next)) queue.Enqueue(next);
                }
            }
            foreach (Vector2Int cell in bounds.allPositionsWithin) if (!exterior.Contains(cell)) cells.Add(cell);
            return new LevelFootprint(cells);
        }

        private static RectInt GetBounds(IEnumerable<Vector2Int> cells)
        {
            bool any = false; Vector2Int min = Vector2Int.zero, max = Vector2Int.zero;
            foreach (Vector2Int cell in cells)
            {
                if (!any) { min = max = cell; any = true; }
                else { min = Vector2Int.Min(min, cell); max = Vector2Int.Max(max, cell); }
            }
            return any ? new RectInt(min, max - min + Vector2Int.one) : new RectInt();
        }

        public bool Overlaps(LevelFootprint other, Vector2Int position, Vector2Int otherPosition)
        {
            if (!new RectInt(Bounds.position + position, Bounds.size).Overlaps(new RectInt(other.Bounds.position + otherPosition, other.Bounds.size))) return false;
            if (Cells.Count > other.Cells.Count) return other.Overlaps(this, otherPosition, position);
            foreach (Vector2Int cell in Cells) if (other.Contains(cell + position - otherPosition)) return true;
            return false;
        }
    }
}
