using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public sealed class GhostBlockLevelBrushHandler : LevelBrushHandler
    {
        public const string HandlerId = "ghost-block";
        public override string Id => HandlerId;
        public override bool GeneratesSceneObject => false;

        public override void DrawSettings(LevelPlacement placement)
        {
            EditorGUILayout.HelpBox("幽灵方块只标记关卡范围，游戏中没有模型或碰撞。可用笔刷、矩形填充或矩形边框扩展绿色轮廓和遮罩范围；超出蓝色画布时先扩展画布。", MessageType.None);
        }
    }

    public sealed class SpringLevelBrushHandler : LevelBrushHandler
    {
        public override string Id => "spring";
        public override LevelPlacementSettings CreateSettings(LevelBrush brush) => new SpringPlacementSettings();
        public override void DrawSettings(LevelPlacement placement)
        {
            if (!(placement.Settings is SpringPlacementSettings settings))
            { EditorGUILayout.HelpBox("弹簧配置不兼容，请重新绘制。", MessageType.Error); return; }
            settings.Height = EditorGUILayout.FloatField("弹起高度（格）", settings.Height);
            settings.AnimationDuration = EditorGUILayout.FloatField("动画时长（秒）", settings.AnimationDuration);
            EditorGUILayout.HelpBox("从顶部落下时触发；默认向上弹起 4.5 格。", MessageType.None);
        }
        public override string ValidatePlacement(LevelPlacement placement)
        {
            if (!(placement.Settings is SpringPlacementSettings settings)) return "弹簧缺少配置。";
            if (float.IsNaN(settings.Height) || float.IsInfinity(settings.Height) || settings.Height <= 0f)
                return "弹起高度必须是大于零的有限数值。";
            if (float.IsNaN(settings.AnimationDuration) || float.IsInfinity(settings.AnimationDuration) || settings.AnimationDuration < 0.02f)
                return "动画时长必须至少为 0.02 秒且是有限数值。";
            return placement.Brush.Prefab.GetComponent<SpringPad>() == null ? "弹簧 Prefab 缺少 SpringPad。" : null;
        }
        public override void ConfigureInstance(GameObject instance, LevelPlacement placement, LevelBuildContext context)
        {
            var settings = (SpringPlacementSettings)placement.Settings;
            SpringPad spring = instance.GetComponent<SpringPad>();
            spring.BounceHeight = settings.Height * context.UnitSize;
            spring.AnimationDuration = settings.AnimationDuration;
        }
    }

    public sealed class EndpointLevelBrushHandler : LevelBrushHandler
    {
        public override string Id => "endpoint";
        public override LevelPlacementSettings CreateSettings(LevelBrush brush) => new EndpointPlacementSettings();

        public override void DrawSettings(LevelPlacement placement)
        {
            if (!(placement.Settings is EndpointPlacementSettings settings))
            { EditorGUILayout.HelpBox("终点配置不兼容，请重新绘制此格。", MessageType.Error); return; }
            settings.EndpointId = EditorGUILayout.TextField("终点标识", settings.EndpointId);
            settings.Label = EditorGUILayout.TextField("备注名称", settings.Label);
            EditorGUILayout.HelpBox("身体触及终点后，按大世界序号转场到下一关入口。所有关卡保持在同一场景。", MessageType.None);
        }

        public override string ValidatePlacement(LevelPlacement placement)
        {
            if (!(placement.Settings is EndpointPlacementSettings settings)) return "终点缺少配置。";
            if (string.IsNullOrWhiteSpace(settings.EndpointId)) return "终点标识不能为空。";
            return placement.Brush.Prefab.GetComponentInChildren<LevelEndpoint>() == null
                ? "终点 Prefab 缺少 LevelEndpoint。" : null;
        }

        public override void ConfigureInstance(GameObject instance, LevelPlacement placement, LevelBuildContext context)
        {
            var settings = (EndpointPlacementSettings)placement.Settings;
            LevelEndpoint endpoint = instance.GetComponentInChildren<LevelEndpoint>();
            endpoint.EndpointId = settings.EndpointId;
            endpoint.Label = settings.Label;
            endpoint.EnsureTrigger();
            // Endpoint anchors use all three axes: walking one grid unit into the door
            // must also scale in Z when the palette uses a non-unit cell size.
            Vector3 scale = instance.transform.localScale;
            scale.z = context.UnitSize;
            instance.transform.localScale = scale;
        }
    }

    public sealed class PhaseBlockLevelBrushHandler : LevelBrushHandler
    {
        public override string Id => "phase-block";
        public override LevelPlacementSettings CreateSettings(LevelBrush brush) => new PhaseBlockPlacementSettings
        {
            SolidWhenActive = brush.Prefab != null && brush.Prefab.GetComponent<PhaseBlock>() != null
                ? brush.Prefab.GetComponent<PhaseBlock>().SolidWhenActive : true
        };
        public override void DrawSettings(LevelPlacement placement)
        {
            if (!(placement.Settings is PhaseBlockPlacementSettings settings))
            { EditorGUILayout.HelpBox("虚实方块配置不兼容，请重新绘制。", MessageType.Error); return; }
            settings.SolidWhenActive = EditorGUILayout.Toggle("开关亮起时为实", settings.SolidWhenActive);
            EditorGUILayout.HelpBox("所有按钮翻转同一个场景状态。关闭状态下，勾选的方块为虚；未勾选的方块为实。", MessageType.None);
        }
        public override string ValidatePlacement(LevelPlacement placement) =>
            !(placement.Settings is PhaseBlockPlacementSettings) || placement.Brush.Prefab.GetComponent<PhaseBlock>() == null
                ? "虚实方块缺少 PhaseBlock 或配置。" : null;
        public override void ConfigureInstance(GameObject instance, LevelPlacement placement, LevelBuildContext context) =>
            instance.GetComponent<PhaseBlock>().SolidWhenActive = ((PhaseBlockPlacementSettings)placement.Settings).SolidWhenActive;
    }

    public sealed class DefaultLevelBrushHandler : LevelBrushHandler
    {
        public override string Id => "default";
    }

    public sealed class MechanismSwitchLevelBrushHandler : LevelBrushHandler, ILevelBrushRotation
    {
        public override string Id => "mechanism-switch";
        public int RotationStepDegrees => 180;
        public override LevelPlacementSettings CreateSettings(LevelBrush brush) => new MechanismSwitchPlacementSettings();
        // Existing buttons have empty settings and keep their original upward orientation.
        public bool CanRotate(LevelPlacement placement) => placement.Settings is MechanismSwitchPlacementSettings ||
            placement.Settings is EmptyPlacementSettings;

        public void Rotate(LevelPlacement placement)
        {
            if (!CanRotate(placement)) return;
            placement.Settings = new MechanismSwitchPlacementSettings
            {
                FacingDown = (placement.Settings as MechanismSwitchPlacementSettings)?.FacingDown != true
            };
        }

        public override void MirrorSettings(LevelPlacement placement, bool horizontal)
        {
            if (!horizontal) Rotate(placement);
        }

        public override void DrawSettings(LevelPlacement placement)
        {
            if (!CanRotate(placement))
            { EditorGUILayout.HelpBox("按钮配置不兼容，请重新绘制。", MessageType.Error); return; }
            bool down = (placement.Settings as MechanismSwitchPlacementSettings)?.FacingDown == true;
            EditorGUI.BeginChangeCheck();
            int direction = EditorGUILayout.Popup("朝向", down ? 1 : 0, new[] { "上 ↑", "下 ↓" });
            if (EditorGUI.EndChangeCheck())
                placement.Settings = new MechanismSwitchPlacementSettings { FacingDown = direction == 1 };
        }

        public override Rect GetPreviewRect(LevelPlacement placement)
        {
            Rect rect = base.GetPreviewRect(placement);
            if ((placement.Settings as MechanismSwitchPlacementSettings)?.FacingDown == true)
                rect.position = new Vector2(1f - rect.xMax, 1f - rect.yMax);
            return rect;
        }

        public override string ValidatePlacement(LevelPlacement placement) => !CanRotate(placement)
            ? "按钮缺少朝向配置。"
            : placement.Brush.Prefab.GetComponent<MechanismSwitch>() == null ? "按钮 Prefab 缺少 MechanismSwitch。" : null;

        public override void ConfigureInstance(GameObject instance, LevelPlacement placement, LevelBuildContext context)
        {
            if ((placement.Settings as MechanismSwitchPlacementSettings)?.FacingDown != true) return;
            // Turn around the cell centre, so the ceiling button stays inside the same cell.
            Vector2 offset = (Vector2.one - placement.Brush.Anchor * 2f) * context.UnitSize;
            instance.transform.localPosition += (Vector3)offset;
            instance.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
        }
    }

    public sealed class SpikesLevelBrushHandler : LevelBrushHandler, ILevelBrushRotation
    {
        public override string Id => "spikes";
        public int RotationStepDegrees => 90;
        public override LevelPlacementSettings CreateSettings(LevelBrush brush) => new SpikePlacementSettings();
        public override void MirrorSettings(LevelPlacement placement, bool horizontal)
        {
            if (!CanRotate(placement)) return;
            var settings = placement.Settings as SpikePlacementSettings ?? new SpikePlacementSettings();
            settings.QuarterTurns = (horizontal ? -settings.QuarterTurns : 2 - settings.QuarterTurns) & 3;
            placement.Settings = settings;
        }
        // Existing layouts used EmptyPlacementSettings for upward spikes.
        public bool CanRotate(LevelPlacement placement) => placement.Settings is SpikePlacementSettings ||
            placement.Settings is EmptyPlacementSettings;
        public void Rotate(LevelPlacement placement)
        {
            if (!CanRotate(placement)) return;
            var settings = placement.Settings as SpikePlacementSettings ?? new SpikePlacementSettings();
            settings.QuarterTurns = (settings.QuarterTurns + 1) & 3;
            placement.Settings = settings;
        }
        public override void DrawSettings(LevelPlacement placement)
        {
            if (!CanRotate(placement))
            { EditorGUILayout.HelpBox("尖刺配置不兼容，请重新绘制。", MessageType.Error); return; }
            var settings = placement.Settings as SpikePlacementSettings ?? new SpikePlacementSettings();
            settings.QuarterTurns = EditorGUILayout.Popup("朝向", settings.QuarterTurns & 3,
                new[] { "上 ↑", "右 →", "下 ↓", "左 ←" });
            placement.Settings = settings;
        }
        public override string ValidatePlacement(LevelPlacement placement) => !CanRotate(placement)
            ? "尖刺缺少方向配置。"
            : placement.Brush.Prefab.GetComponentInChildren<LethalZone>(true) == null ? "尖刺 Prefab 缺少 LethalZone。" : null;
        public override void ConfigureInstance(GameObject instance, LevelPlacement placement, LevelBuildContext context)
        {
            int turns = (placement.Settings as SpikePlacementSettings)?.QuarterTurns ?? 0;
            // Rotate both the visual and lethal collider around the cell's anchor.
            instance.transform.localRotation = Quaternion.Euler(0f, 0f, -(turns & 3) * 90f);
        }
    }

    public sealed class ConveyorLevelBrushHandler : LevelBrushHandler, ILevelBrushRotation
    {
        public override string Id => "conveyor";
        public int RotationStepDegrees => 180;
        public bool CanRotate(LevelPlacement placement) => placement.Settings is ConveyorPlacementSettings;
        public override void MirrorSettings(LevelPlacement placement, bool horizontal)
        {
            if (!horizontal || !(placement.Settings is ConveyorPlacementSettings settings)) return;
            settings.Speed = -settings.Speed;
            settings.FacingLeftWhenStopped = !settings.FacingLeftWhenStopped;
        }
        public void Rotate(LevelPlacement placement)
        {
            if (!(placement.Settings is ConveyorPlacementSettings settings)) return;
            bool wasLeft = settings.FacingLeft;
            settings.Speed = -settings.Speed;
            settings.FacingLeftWhenStopped = !wasLeft;
        }
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
            EditorGUILayout.HelpBox("同排且速度、方向一致的传送带自动拼接，至少连续两格。中段随方向翻转，箭头按世界 X 坐标错相闪亮。闪光材质与周期在 GridConveyor 的 ConveyorTileVisual 配置。", MessageType.None);
        }

        public override string ValidatePlacement(LevelPlacement placement)
        {
            if (!(placement.Settings is ConveyorPlacementSettings settings))
                return "传送带缺少速度配置。";
            if (float.IsNaN(settings.Speed) || float.IsInfinity(settings.Speed))
                return "传送带速度必须是有限数值。";
            if (placement.Brush.Prefab.GetComponent<WorldSurface>() == null) return "传送带 Prefab 缺少 WorldSurface。";
            var view = placement.Brush.Prefab.GetComponent<ConveyorTileVisual>();
            if (view == null || view.LeftModel == null || view.MiddleModel == null || view.RightModel == null)
                return "传送带需要 ConveyorTileVisual 及左端、中段、右端模型。";
            if (view.ArrowGlowMaterial == null) return "传送带缺少箭头发光材质。";
            if (float.IsNaN(view.FlashPeriod) || float.IsInfinity(view.FlashPeriod) || view.FlashPeriod <= 0f ||
                float.IsNaN(view.FlashDuration) || float.IsInfinity(view.FlashDuration) || view.FlashDuration < 0f || view.FlashDuration > view.FlashPeriod)
                return "闪光周期必须大于零，亮起时长须在零到一个周期之间。";
            return null;
        }

        public override string ValidateLayoutPlacement(LevelPlacement placement, LevelDefinition layout) =>
            ConveyorTileRules.Part(layout, placement).HasValue ? null : "传送带至少连续两格，且相邻格的速度和方向需一致。";

        public override void ConfigureInstance(GameObject instance, LevelPlacement placement, LevelBuildContext context)
        {
            var settings = (ConveyorPlacementSettings)placement.Settings;
            instance.GetComponentInChildren<WorldSurface>().ConveyorSpeed = settings.Speed * context.UnitSize;
            var view = instance.GetComponent<ConveyorTileVisual>();
            view.FacingLeftWhenStopped = settings.FacingLeftWhenStopped;
            view.ApplyPart(ConveyorTileRules.Part(context.Definition, placement).Value);
        }
    }

    public sealed class CheckpointLevelBrushHandler : LevelBrushHandler
    {
        public override string Id => "checkpoint";
        public override bool IsCheckpoint => true;
        public override void MirrorSettings(LevelPlacement placement, bool horizontal)
        {
            if (!(placement.Settings is CheckpointPlacementSettings settings)) return;
            settings.SpawnOffset = horizontal ? new Vector2(-settings.SpawnOffset.x, settings.SpawnOffset.y)
                : new Vector2(settings.SpawnOffset.x, -settings.SpawnOffset.y);
        }
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
