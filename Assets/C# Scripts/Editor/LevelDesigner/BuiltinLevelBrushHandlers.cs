using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public sealed class DefaultLevelBrushHandler : LevelBrushHandler
    {
        public override string Id => "default";
    }

    public sealed class ConveyorLevelBrushHandler : LevelBrushHandler
    {
        public override string Id => "conveyor";
        public override LevelPlacementSettings CreateSettings(LevelBrush brush) => new ConveyorPlacementSettings();

        public override void DrawSettings(LevelPlacement placement)
        {
            if (!(placement.Settings is ConveyorPlacementSettings settings))
            {
                EditorGUILayout.HelpBox("传送带配置不兼容，请重新绘制此格。", MessageType.Error);
                return;
            }
            settings.Speed = EditorGUILayout.FloatField("速度（格/秒）", settings.Speed);
            EditorGUILayout.LabelField("正值向右，负值向左，零为停止。", EditorStyles.wordWrappedMiniLabel);
        }

        public override string ValidatePlacement(LevelPlacement placement)
        {
            if (!(placement.Settings is ConveyorPlacementSettings settings))
                return "传送带缺少速度配置。";
            if (float.IsNaN(settings.Speed) || float.IsInfinity(settings.Speed))
                return "传送带速度必须是有限数值。";
            return placement.Brush.Prefab.GetComponentInChildren<WorldSurface>() == null
                ? "传送带 Prefab 缺少 WorldSurface。" : null;
        }

        public override void ConfigureInstance(GameObject instance, LevelPlacement placement, LevelBuildContext context)
        {
            var settings = (ConveyorPlacementSettings)placement.Settings;
            instance.GetComponentInChildren<WorldSurface>().ConveyorSpeed = settings.Speed * context.UnitSize;
            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                if (child.name.StartsWith("Belt arrow ", System.StringComparison.Ordinal))
                    child.localRotation = Quaternion.Euler(0f, 0f, settings.Speed < 0f ? 90f : -90f);
        }
    }

    public sealed class CheckpointLevelBrushHandler : LevelBrushHandler
    {
        public override string Id => "checkpoint";
        public override bool IsCheckpoint => true;
        public override LevelPlacementSettings CreateSettings(LevelBrush brush) => new CheckpointPlacementSettings();

        public override void DrawSettings(LevelPlacement placement)
        {
            if (placement.Settings is CheckpointPlacementSettings settings)
                settings.SpawnOffset = EditorGUILayout.Vector2Field("出生偏移（格）", settings.SpawnOffset);
            else
                EditorGUILayout.HelpBox("复活点配置不兼容，请重新绘制此格。", MessageType.Error);
        }

        public override string ValidatePlacement(LevelPlacement placement)
        {
            if (!(placement.Settings is CheckpointPlacementSettings settings))
                return "复活点缺少出生偏移配置。";
            if (float.IsNaN(settings.SpawnOffset.x) || float.IsInfinity(settings.SpawnOffset.x)
                || float.IsNaN(settings.SpawnOffset.y) || float.IsInfinity(settings.SpawnOffset.y))
                return "复活点出生偏移必须是有限数值。";
            return placement.Brush.Prefab.GetComponentInChildren<CheckpointFlag>() == null
                ? "复活点 Prefab 缺少 CheckpointFlag。" : null;
        }

        public override void ConfigureInstance(GameObject instance, LevelPlacement placement, LevelBuildContext context)
        {
            var checkpoint = instance.GetComponentInChildren<CheckpointFlag>();
            checkpoint.SpawnOffset = ((CheckpointPlacementSettings)placement.Settings).SpawnOffset * context.UnitSize;
            if (placement.Id == context.Definition.EntryPlacementId)
                context.Region.EntryCheckpoint = checkpoint;
        }
    }
}
