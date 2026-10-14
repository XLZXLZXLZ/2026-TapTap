using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public sealed class LightLevelBrushHandler : LevelBrushHandler, ILevelBrushRotation
    {
        public override string Id => "indicator-light";
        public int RotationStepDegrees => 90;
        public override LevelPlacementSettings CreateSettings(LevelBrush brush) => new LightPlacementSettings();
        public bool CanRotate(LevelPlacement placement) => placement.Settings is LightPlacementSettings;
        public void Rotate(LevelPlacement placement)
        {
            if (placement.Settings is LightPlacementSettings settings)
                settings.RotationDegrees = Mathf.Repeat(settings.RotationDegrees - 90f, 360f);
        }

        public override void MirrorSettings(LevelPlacement placement, bool horizontal)
        {
            if (placement.Settings is LightPlacementSettings settings)
                settings.RotationDegrees = Mathf.Repeat(horizontal ? -settings.RotationDegrees : 180f - settings.RotationDegrees, 360f);
        }

        public override void DrawSettings(LevelPlacement placement)
        {
            if (!(placement.Settings is LightPlacementSettings settings)) return;
            settings.InitiallyOn = EditorGUILayout.Toggle("开始时打开", settings.InitiallyOn);
            settings.ShortWidth = EditorGUILayout.Popup("短边宽度（格）", settings.ShortWidth == 3 ? 1 : 0, new[] { "2", "3" }) + 2;
            settings.LongWidth = Mathf.Max(settings.ShortWidth, EditorGUILayout.FloatField("长边宽度（格）", settings.LongWidth));
            settings.Length = Mathf.Max(0.1f, EditorGUILayout.FloatField("条带长度（格）", settings.Length));
            settings.RotationDegrees = EditorGUILayout.FloatField("旋转角度（度）", settings.RotationDegrees);
            settings.Tint = EditorGUILayout.ColorField("颜色与透明度", settings.Tint);
            settings.GradientPower = Mathf.Max(0.1f, EditorGUILayout.FloatField("渐变强度", settings.GradientPower));
            EditorGUILayout.HelpBox("以短边中心为原点，默认向下展开。仅用于指示，不产生碰撞。T 每次顺时针转 90°。", MessageType.None);
        }

        public override string ValidatePlacement(LevelPlacement placement)
        {
            if (!(placement.Settings is LightPlacementSettings settings)) return "灯光缺少配置。";
            if (settings.ShortWidth != 2 && settings.ShortWidth != 3) return "灯光短边必须为 2 或 3 格。";
            if (!Finite(settings.LongWidth) || settings.LongWidth < settings.ShortWidth
                || !Finite(settings.Length) || settings.Length < 0.1f || !Finite(settings.RotationDegrees)
                || !Finite(settings.GradientPower) || settings.GradientPower <= 0f
                || !Finite(settings.Tint.r) || !Finite(settings.Tint.g) || !Finite(settings.Tint.b) || !Finite(settings.Tint.a))
                return "灯光尺寸、旋转及渐变必须为有效数值，长边不能比短边窄。";
            return placement.Brush.Prefab.GetComponent<IndicatorLight>() == null ? "灯光 Prefab 缺少 IndicatorLight。" : null;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public override void ConfigureInstance(GameObject instance, LevelPlacement placement, LevelBuildContext context)
        {
            var settings = (LightPlacementSettings)placement.Settings;
            IndicatorLight light = instance.GetComponent<IndicatorLight>();
            light.InitiallyOn = settings.InitiallyOn; light.ShortWidth = settings.ShortWidth;
            light.LongWidth = settings.LongWidth; light.Length = settings.Length;
            light.Tint = settings.Tint; light.GradientPower = settings.GradientPower; light.GridUnit = 1f;
            instance.transform.localRotation = Quaternion.Euler(0f, 0f, settings.RotationDegrees);
            light.RebuildVisual(); light.SetOn(settings.InitiallyOn);
        }
    }
}
