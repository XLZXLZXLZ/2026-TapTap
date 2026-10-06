using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap.Editor
{
    internal static class LevelGridEditing
    {
        public static IEnumerable<Vector2Int> Rectangle(Vector2Int start, Vector2Int end, bool outline)
        {
            int left = Mathf.Min(start.x, end.x), right = Mathf.Max(start.x, end.x);
            int bottom = Mathf.Min(start.y, end.y), top = Mathf.Max(start.y, end.y);
            for (int y = bottom; y <= top; y++)
                for (int x = left; x <= right; x++)
                    if (!outline || x == left || x == right || y == bottom || y == top)
                        yield return new Vector2Int(x, y);
        }

        public static List<Vector2Int> ConnectedArea(LevelDefinition layout, Vector2Int origin, string layerId)
        {
            var result = new List<Vector2Int>();
            if (!layout.Contains(origin)) return result;
            Dictionary<Vector2Int, LevelPlacement> layer = IndexLayer(layout, layerId);
            layer.TryGetValue(origin, out LevelPlacement source);
            LevelBrush target = source != null ? source.Brush : null;
            var pending = new Queue<Vector2Int>();
            var seen = new HashSet<Vector2Int>();
            pending.Enqueue(origin);
            seen.Add(origin);
            while (pending.Count > 0)
            {
                Vector2Int cell = pending.Dequeue();
                layer.TryGetValue(cell, out LevelPlacement placement);
                if ((placement != null ? placement.Brush : null) != target) continue;
                result.Add(cell);
                Enqueue(cell + Vector2Int.left);
                Enqueue(cell + Vector2Int.right);
                Enqueue(cell + Vector2Int.up);
                Enqueue(cell + Vector2Int.down);
            }
            return result;

            void Enqueue(Vector2Int cell)
            {
                if (layout.Contains(cell) && seen.Add(cell)) pending.Enqueue(cell);
            }
        }

        public static bool Apply(LevelDefinition layout, IEnumerable<Vector2Int> cells, LevelBrush brush,
            LevelPlacementSettings settings, string layerId, bool erase, bool allLayers)
        {
            var targets = new HashSet<Vector2Int>();
            foreach (Vector2Int cell in cells)
                if (layout.Contains(cell)) targets.Add(cell);
            if (targets.Count == 0) return false;
            if (layout.Placements == null) layout.Placements = new List<LevelPlacement>();
            if (erase)
            {
                int removed = layout.Placements.RemoveAll(item => item != null && targets.Contains(item.Cell) &&
                    (allLayers || item.Brush != null && string.Equals(item.Brush.LayerId, layerId, StringComparison.Ordinal)));
                if (removed > 0 && layout.EntryPlacement == null) layout.EntryPlacementId = null;
                return removed > 0;
            }
            if (brush == null) return false;
            Dictionary<Vector2Int, LevelPlacement> layer = IndexLayer(layout, brush.LayerId);
            bool checkpoint = LevelBrushHandlers.Get(brush.HandlerId)?.IsCheckpoint == true;
            string settingsJson = settings != null ? JsonUtility.ToJson(settings) : "{}";
            Type settingsType = settings != null ? settings.GetType() : typeof(EmptyPlacementSettings);
            bool changed = false;
            foreach (Vector2Int cell in targets)
            {
                if (layer.TryGetValue(cell, out LevelPlacement placement))
                {
                    if (placement.Brush == brush && placement.Settings != null &&
                        placement.Settings.GetType() == settingsType && JsonUtility.ToJson(placement.Settings) == settingsJson)
                        continue;
                    if (placement.Id == layout.EntryPlacementId && !checkpoint) layout.EntryPlacementId = null;
                    placement.Brush = brush;
                    placement.Settings = CloneSettings(settings);
                }
                else
                {
                    layout.Placements.Add(new LevelPlacement { Cell = cell, Brush = brush, Settings = CloneSettings(settings) });
                }
                changed = true;
            }
            return changed;
        }

        public static LevelPlacementSettings CloneSettings(LevelPlacementSettings source)
        {
            return source == null ? new EmptyPlacementSettings() :
                JsonUtility.FromJson(JsonUtility.ToJson(source), source.GetType()) as LevelPlacementSettings;
        }

        private static Dictionary<Vector2Int, LevelPlacement> IndexLayer(LevelDefinition layout, string layerId)
        {
            var result = new Dictionary<Vector2Int, LevelPlacement>();
            if (layout.Placements != null)
                foreach (LevelPlacement item in layout.Placements)
                    if (item != null && item.Brush != null &&
                        string.Equals(item.Brush.LayerId, layerId, StringComparison.Ordinal)) result[item.Cell] = item;
            return result;
        }
    }
}
