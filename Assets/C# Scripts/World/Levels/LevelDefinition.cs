using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    [CreateAssetMenu(menuName = "TapTap/Levels/Layout")]
    public sealed class LevelDefinition : ScriptableObject
    {
        public Vector2Int Size = new Vector2Int(32, 18);
        public LevelPalette Palette;
        public List<LevelPlacement> Placements = new List<LevelPlacement>();
        public string EntryPlacementId;
        public GameObject OutputPrefab;

        public LevelPlacement EntryPlacement
        {
            get
            {
                if (string.IsNullOrEmpty(EntryPlacementId) || Placements == null)
                    return null;

                return Placements.Find(placement => placement != null &&
                    string.Equals(placement.Id, EntryPlacementId, StringComparison.Ordinal));
            }
        }

        public bool Contains(Vector2Int cell)
        {
            return cell.x >= 0 && cell.y >= 0 && cell.x < Size.x && cell.y < Size.y;
        }

        public LevelPlacement Find(Vector2Int cell, string layerId)
        {
            if (Placements == null)
                return null;

            return Placements.Find(placement => Matches(placement, cell, layerId));
        }

        public LevelPlacement Put(Vector2Int cell, LevelBrush brush, LevelPlacementSettings settings)
        {
            if (!Contains(cell) || brush == null)
                return null;

            if (Placements == null)
                Placements = new List<LevelPlacement>();

            Erase(cell, brush.LayerId);
            var placement = new LevelPlacement
            {
                Cell = cell,
                Brush = brush,
                Settings = settings ?? new EmptyPlacementSettings()
            };
            Placements.Add(placement);
            return placement;
        }

        public bool Erase(Vector2Int cell, string layerId, bool allLayers = false)
        {
            if (Placements == null)
                return false;

            bool removed = false;
            for (int i = Placements.Count - 1; i >= 0; i--)
            {
                LevelPlacement placement = Placements[i];
                if (placement == null || placement.Cell != cell ||
                    (!allLayers && !Matches(placement, cell, layerId)))
                    continue;

                if (string.Equals(placement.Id, EntryPlacementId, StringComparison.Ordinal))
                    EntryPlacementId = string.Empty;
                Placements.RemoveAt(i);
                removed = true;
            }
            return removed;
        }

        private static bool Matches(LevelPlacement placement, Vector2Int cell, string layerId)
        {
            return placement != null && placement.Cell == cell && placement.Brush != null &&
                string.Equals(placement.Brush.LayerId, layerId, StringComparison.Ordinal);
        }

        private void OnValidate()
        {
            Size = new Vector2Int(Mathf.Max(1, Size.x), Mathf.Max(1, Size.y));
        }
    }
}
