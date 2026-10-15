using UnityEngine;

namespace TapTap.Editor
{
    public static class ConveyorTileRules
    {
        public static ConveyorPart? Part(LevelDefinition level, LevelPlacement placement)
        {
            bool left = Connects(level, placement, Vector2Int.left), right = Connects(level, placement, Vector2Int.right);
            if (!left && !right) return null;
            return left ? right ? ConveyorPart.Middle : ConveyorPart.Right : ConveyorPart.Left;
        }

        private static bool Connects(LevelDefinition level, LevelPlacement placement, Vector2Int offset)
        {
            if (placement?.Brush == null || !(placement.Settings is ConveyorPlacementSettings settings)) return false;
            LevelPlacement other = level.Find(placement.Cell + offset, placement.Brush.LayerId);
            if (other?.Brush?.HandlerId != "conveyor" || !(other.Settings is ConveyorPlacementSettings neighbour)) return false;
            if (settings.Speed != neighbour.Speed || settings.FacingLeft != neighbour.FacingLeft) return false;
            var a = placement.Brush.Prefab != null ? placement.Brush.Prefab.GetComponent<ConveyorTileVisual>() : null;
            var b = other.Brush.Prefab != null ? other.Brush.Prefab.GetComponent<ConveyorTileVisual>() : null;
            return a != null && b != null && a.ConnectionGroup == b.ConnectionGroup;
        }
    }
}
