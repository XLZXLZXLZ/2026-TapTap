using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    // Authoring footprint is the maximum occupied area, independent of switch/animation state.
    public static class LevelPlacementGeometry
    {
        public static RectInt Bounds(LevelPlacement placement)
        {
            Vector2Int end = placement.Cell;
            if (placement.Settings is PistonPlacementSettings piston)
                end += piston.Direction * (Mathf.Clamp(piston.MaxLength, 1, PistonPlacementSettings.MaximumLength) - 1);
            Vector2Int min = Vector2Int.Min(placement.Cell, end);
            return new RectInt(min, Vector2Int.Max(placement.Cell, end) - min + Vector2Int.one);
        }

        public static IEnumerable<Vector2Int> Cells(LevelPlacement placement)
        {
            foreach (Vector2Int cell in Bounds(placement).allPositionsWithin) yield return cell;
        }
        public static bool Contains(LevelPlacement placement, Vector2Int cell) => Bounds(placement).Contains(cell);
        public static bool Intersects(LevelPlacement placement, RectInt area) => Bounds(placement).Overlaps(area);
        public static bool Fits(LevelPlacement placement, RectInt canvas)
        {
            RectInt bounds = Bounds(placement);
            return canvas.Contains(bounds.min) && canvas.Contains(bounds.max - Vector2Int.one);
        }

        public static Vector2 PistonOrigin(LevelPlacement placement) => (Vector2)placement.Cell + Vector2.one * 0.5f
            - (Vector2)((PistonPlacementSettings)placement.Settings).Direction * 0.5f;
    }
}
