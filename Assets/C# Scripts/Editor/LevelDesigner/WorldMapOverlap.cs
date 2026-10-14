using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    // A shared cell is compatible when every non-annotation brush type matches.
    // Empty interiors therefore match each other, but not occupied cells.
    public static class WorldMapOverlap
    {
        private sealed class GridTypes
        {
            public LevelFootprint Footprint;
            public int Version, PlacementCount;
            public Dictionary<Vector2Int, HashSet<string>> Cells;
        }

        private static readonly ConditionalWeakTable<LevelDefinition, GridTypes> Grids =
            new ConditionalWeakTable<LevelDefinition, GridTypes>();

        public static bool HasConflict(LevelDefinition a, LevelFootprint footprintA, Vector2Int originA,
            LevelDefinition b, LevelFootprint footprintB, Vector2Int originB)
        {
            if (a == null || b == null || footprintA == null || footprintB == null) return false;
            if (!new RectInt(footprintA.Bounds.position + originA, footprintA.Bounds.size)
                .Overlaps(new RectInt(footprintB.Bounds.position + originB, footprintB.Bounds.size))) return false;
            if (footprintA.Cells.Count > footprintB.Cells.Count)
                return HasConflict(b, footprintB, originB, a, footprintA, originA);

            Dictionary<Vector2Int, HashSet<string>> typesA = Types(a), typesB = Types(b);
            foreach (Vector2Int cellA in footprintA.Cells)
            {
                Vector2Int cellB = cellA + originA - originB;
                if (!footprintB.Contains(cellB)) continue;
                typesA.TryGetValue(cellA, out HashSet<string> brushesA);
                typesB.TryGetValue(cellB, out HashSet<string> brushesB);
                if (brushesA == null ? brushesB != null : brushesB == null || !brushesA.SetEquals(brushesB)) return true;
            }
            return false;
        }

        public static List<Vector2Int> ConflictCells(LevelDefinition a, LevelFootprint footprintA, Vector2Int originA,
            LevelDefinition b, LevelFootprint footprintB, Vector2Int originB)
        {
            var conflicts = new List<Vector2Int>();
            if (a == null || b == null || !new RectInt(footprintA.Bounds.position + originA, footprintA.Bounds.size)
                .Overlaps(new RectInt(footprintB.Bounds.position + originB, footprintB.Bounds.size))) return conflicts;
            var typesA = Types(a); var typesB = Types(b);
            foreach (Vector2Int cell in footprintA.Cells)
            {
                Vector2Int other = cell + originA - originB;
                if (!footprintB.Contains(other)) continue;
                typesA.TryGetValue(cell, out var first); typesB.TryGetValue(other, out var second);
                if (first == null ? second != null : second == null || !first.SetEquals(second)) conflicts.Add(cell + originA);
            }
            return conflicts;
        }

        private static Dictionary<Vector2Int, HashSet<string>> Types(LevelDefinition level)
        {
            GridTypes grid = Grids.GetValue(level, _ => new GridTypes());
            LevelFootprint footprint = level.Footprint;
            int version = EditorUtility.GetDirtyCount(level), count = level.Placements?.Count ?? 0;
            if (grid.Cells != null && grid.Footprint == footprint && grid.Version == version && grid.PlacementCount == count)
                return grid.Cells;
            grid.Footprint = footprint; grid.Version = version; grid.PlacementCount = count;
            grid.Cells = new Dictionary<Vector2Int, HashSet<string>>();
            if (level.Placements != null)
                foreach (LevelPlacement placement in level.Placements)
                {
                    if (placement?.Brush == null || placement.Brush.HandlerId == "annotation") continue;
                    if (!grid.Cells.TryGetValue(placement.Cell, out HashSet<string> brushes))
                        grid.Cells.Add(placement.Cell, brushes = new HashSet<string>());
                    string prefab = AssetDatabase.GetAssetPath(placement.Brush.Prefab);
                    if (string.IsNullOrEmpty(prefab)) prefab = "instance:" + (placement.Brush.Prefab != null
                        ? placement.Brush.Prefab.GetInstanceID() : placement.Brush.GetInstanceID());
                    string settings = placement.Settings == null || placement.Settings is EmptyPlacementSettings ? "{}"
                        : placement.Settings.GetType().FullName + JsonUtility.ToJson(placement.Settings);
                    brushes.Add(prefab + "|" + placement.Brush.HandlerId + "|" + placement.Brush.LayerId + "|"
                        + JsonUtility.ToJson(placement.Brush.Anchor) + "|" + settings);
                }
            return grid.Cells;
        }
    }
}
