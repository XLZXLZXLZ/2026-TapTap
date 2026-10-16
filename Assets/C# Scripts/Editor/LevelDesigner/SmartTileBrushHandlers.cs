using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public sealed class PistonLevelBrushHandler : LevelBrushHandler, ILevelBrushRotation
    {
        public override string Id => "phase-piston";
        public int RotationStepDegrees => 90;
        public override LevelPlacementSettings CreateSettings(LevelBrush brush) => new PistonPlacementSettings();
        public bool CanRotate(LevelPlacement placement) => placement.Settings is PistonPlacementSettings;
        public void Rotate(LevelPlacement placement)
        {
            if (placement.Settings is PistonPlacementSettings settings) settings.QuarterTurns = (settings.QuarterTurns + 1) & 3;
        }
        public override void MirrorSettings(LevelPlacement placement, bool horizontal)
        {
            if (placement.Settings is PistonPlacementSettings settings)
                settings.QuarterTurns = (horizontal ? 2 - settings.QuarterTurns : -settings.QuarterTurns) & 3;
        }
        public override void DrawSettings(LevelPlacement placement)
        {
            if (!(placement.Settings is PistonPlacementSettings settings)) return;
            settings.QuarterTurns = EditorGUILayout.Popup("伸出方向", settings.QuarterTurns & 3, new[] { "右 →", "下 ↓", "左 ←", "上 ↑" });
            settings.MaxLength = Mathf.Clamp(EditorGUILayout.IntField("最大伸出长度（格）", settings.MaxLength), 1, PistonPlacementSettings.MaximumLength);
            settings.ExtendedWhenActive = EditorGUILayout.Toggle("开关亮起时伸出", settings.ExtendedWhenActive);
            EditorGUILayout.HelpBox("起点为伸出的第一格，基座位于该格背面边界。长度包含末端。左键拖出整数长度；T 转向。碰撞瞬间切换，模型随后伸缩。", MessageType.None);
        }
        public override Rect GetPreviewRect(LevelPlacement placement)
        {
            RectInt bounds = LevelPlacementGeometry.Bounds(placement);
            return new Rect(bounds.x - placement.Cell.x, bounds.y - placement.Cell.y, bounds.width, bounds.height);
        }
        public override string ValidatePlacement(LevelPlacement placement)
        {
            if (!(placement.Settings is PistonPlacementSettings settings) || settings.MaxLength < 1 || settings.MaxLength > PistonPlacementSettings.MaximumLength)
                return "活塞最大长度必须是 1～2048 的整数。";
            return placement.Brush.Prefab.GetComponent<PhasePiston>() == null || placement.Brush.Prefab.GetComponent<PhaseBlock>() == null
                ? "活塞 Prefab 缺少 PhasePiston 或 PhaseBlock。" : null;
        }
        public override void ConfigureInstance(GameObject instance, LevelPlacement placement, LevelBuildContext context)
        {
            var settings = (PistonPlacementSettings)placement.Settings;
            instance.GetComponent<PhaseBlock>().SolidWhenActive = settings.ExtendedWhenActive;
            instance.GetComponent<PhasePiston>().Configure(settings.QuarterTurns, settings.MaxLength);
        }
    }

    public sealed class PlatformLevelBrushHandler : LevelBrushHandler
    {
        public override string Id => "smart-platform";
        public static PlatformPart Part(LevelDefinition level, LevelPlacement placement)
        {
            bool left = Connects(level, placement, Vector2Int.left), right = Connects(level, placement, Vector2Int.right);
            return left ? right ? PlatformPart.Middle : PlatformPart.Right : right ? PlatformPart.Left : PlatformPart.Single;
        }
        private static bool Connects(LevelDefinition level, LevelPlacement placement, Vector2Int offset)
        {
            LevelPlacement neighbour = level.Find(placement.Cell + offset, placement.Brush.LayerId);
            if (neighbour?.Brush == null || neighbour.Brush.HandlerId != "smart-platform") return false;
            PlatformTileVisual a = placement.Brush.Prefab != null ? placement.Brush.Prefab.GetComponent<PlatformTileVisual>() : null;
            PlatformTileVisual b = neighbour.Brush.Prefab != null ? neighbour.Brush.Prefab.GetComponent<PlatformTileVisual>() : null;
            return a != null && b != null && a.ConnectionGroup == b.ConnectionGroup;
        }
        public override string ValidatePlacement(LevelPlacement placement) => placement.Brush.Prefab.GetComponent<PlatformTileVisual>() == null
            ? "智能平台 Prefab 缺少 PlatformTileVisual。" : null;
        public override void ConfigureInstance(GameObject instance, LevelPlacement placement, LevelBuildContext context) =>
            instance.GetComponent<PlatformTileVisual>().ApplyPart(Part(context.Definition, placement));
    }
}
