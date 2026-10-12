using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public static class LevelBoundaryEditing
    {
        public static LevelFootprint ResizeEdge(LevelDefinition level, LevelFootprint original, LevelBoundaryEdge edge, int distance)
        {
            distance = Mathf.Clamp(distance, -2048, 2048);
            var cells = new HashSet<Vector2Int>(original.Cells);
            Vector2Int tangent = edge.Outward.x == 0 ? Vector2Int.right : Vector2Int.up;
            int length = (edge.End - edge.Start).x + (edge.End - edge.Start).y;
            Vector2Int inner = edge.Start;
            if (edge.Outward.x > 0) inner += Vector2Int.left;
            if (edge.Outward.y > 0) inner += Vector2Int.down;
            for (int i = 0; i < length; i++)
                for (int k = 0; k < Mathf.Abs(distance); k++)
                {
                    Vector2Int cell = inner + tangent * i + edge.Outward * (distance > 0 ? k + 1 : -k);
                    if (distance > 0) cells.Add(cell); else cells.Remove(cell);
                }
            cells.UnionWith(LevelFootprint.PlacedCells(level));
            return cells.Count > 0 ? new LevelFootprint(cells) : original;
        }

        public static void Apply(LevelDefinition level, LevelFootprint footprint)
        {
            Undo.RegisterCompleteObjectUndo(level, "调整关卡边界");
            level.UseCustomBoundary = true;
            level.BoundaryCells = new List<Vector2Int>(footprint.Cells);
            level.InvalidateFootprint();
            EditorUtility.SetDirty(level);
            LevelLayoutChanges.Notify(level);
        }

        public static void Reset(LevelDefinition level)
        {
            Undo.RegisterCompleteObjectUndo(level, "恢复自动关卡边界");
            level.UseCustomBoundary = false;
            level.BoundaryCells.Clear();
            level.InvalidateFootprint();
            EditorUtility.SetDirty(level);
            LevelLayoutChanges.Notify(level);
        }
    }
}
