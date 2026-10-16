using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace ScreenFilters.Editor
{
    [CustomEditor(typeof(ScreenFilterProfile))]
    public sealed class ScreenFilterProfileEditor : UnityEditor.Editor
    {
        private FilterProfileGUI controls;
        private void OnEnable() => controls = new FilterProfileGUI(serializedObject);
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("打开滤镜工作台（独立预览）")) ScreenFilterPreviewWindow.Open((ScreenFilterProfile)target);
            EditorGUILayout.HelpBox("此处直接修改共享资源，所有引用它的相机会同步生效。工作台可先试调，再应用保存。", MessageType.Info);
            controls.Draw();
        }
    }

    internal sealed class FilterProfileGUI
    {
        internal static readonly string[] Names = { "像素", "笔刷", "撕裂故障", "色散故障", "显像管" };
        private readonly SerializedObject serialized;
        private readonly ReorderableList order;
        private readonly bool[] expanded = { true, true, false, false, false };

        internal FilterProfileGUI(SerializedObject source)
        {
            serialized = source;
            order = new ReorderableList(serialized, serialized.FindProperty("Order"), true, true, false, false);
            order.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "执行顺序（从上到下，拖动排序）");
            order.drawElementCallback = (rect, index, active, focused) =>
            {
                int kind = order.serializedProperty.GetArrayElementAtIndex(index).enumValueIndex;
                if (kind < 0 || kind >= Names.Length) return;
                SerializedProperty group = serialized.FindProperty(((ScreenFilterKind)kind).ToString());
                SerializedProperty enabled = group.FindPropertyRelative("Enabled");
                rect.y += 1; rect.height = EditorGUIUtility.singleLineHeight;
                enabled.boolValue = EditorGUI.ToggleLeft(rect, Names[kind], enabled.boolValue);
            };
        }

        internal bool Draw()
        {
            serialized.Update();
            EditorGUI.BeginChangeCheck();
            Field("Enabled", "滤镜总开关");
            Field("Seed", "随机种子", "相同种子和预览时间产生相同的动态效果。");
            order.DoLayoutList();
            for (int i = 0; i < order.serializedProperty.arraySize; i++)
            {
                int kind = order.serializedProperty.GetArrayElementAtIndex(i).enumValueIndex;
                if (kind < 0 || kind >= Names.Length) continue;
                expanded[kind] = EditorGUILayout.Foldout(expanded[kind], Names[kind] + "参数", true);
                if (!expanded[kind]) continue;
                using (new EditorGUI.IndentLevelScope())
                {
                    string root = ((ScreenFilterKind)kind) + ".";
                    Field(root + "Strength", "混合强度");
                    switch ((ScreenFilterKind)kind)
                    {
                        case ScreenFilterKind.Pixelation:
                            Field(root + "Size", "像素块尺寸", "以预览／相机实际输出像素计。尺寸 1 为原画；强度 1 保持完整色块。");
                            break;
                        case ScreenFilterKind.Brush:
                            Field(root + "BlockSize", "色块尺寸（像素）");
                            Field(root + "ColorLevels", "每通道颜色层次");
                            Field(root + "EdgePreservation", "边缘保留");
                            Field(root + "EdgeThreshold", "边缘阈值", "越低越容易识别为边缘，并保留原图细节。");
                            Field(root + "Disturbance", "微扰幅度（像素）");
                            Field(root + "MotionSpeed", "微扰速度", "设为 0 时保持静态笔触。");
                            break;
                        case ScreenFilterKind.Tear:
                            Field(root + "Offset", "横向位移（1080p 像素）");
                            Field(root + "BandHeight", "条带高度比例");
                            Field(root + "BandCount", "条带数量");
                            Field(root + "Frequency", "触发频率（次／秒）", "设为 0 时固定为一组静态撕裂。");
                            Field(root + "Duty", "每周期持续比例");
                            break;
                        case ScreenFilterKind.Chromatic:
                            Field(root + "Offset", "通道偏移（1080p 像素）");
                            Field(root + "Angle", "偏移方向（度）");
                            Field(root + "EdgeOnly", "向边缘集中", "0 为全屏均匀偏移，1 为主要影响屏幕边缘。");
                            Field(root + "Jitter", "动态变化幅度");
                            Field(root + "Frequency", "变化频率（次／秒）");
                            break;
                        case ScreenFilterKind.CRT:
                            Field(root + "Scanlines", "扫描线强度");
                            Field(root + "ScanlineSpacing", "扫描线间距（像素）");
                            Field(root + "Mask", "RGB 荫罩强度");
                            Field(root + "MaskSize", "荫罩条纹宽度（像素）");
                            Field(root + "Curvature", "屏幕曲率");
                            Field(root + "Vignette", "暗角");
                            Field(root + "Flicker", "闪烁幅度");
                            Field(root + "Brightness", "亮度补偿");
                            break;
                    }
                }
            }
            bool changed = EditorGUI.EndChangeCheck();
            return serialized.ApplyModifiedProperties() || changed;
        }

        private void Field(string path, string label, string tooltip = null) =>
            EditorGUILayout.PropertyField(serialized.FindProperty(path), new GUIContent(label, tooltip), true);

        internal static void ApplyPreset(ScreenFilterProfile target, int preset)
        {
            Undo.RecordObject(target, "切换滤镜预设");
            string name = target.name;
            HideFlags flags = target.hideFlags;
            var defaults = ScriptableObject.CreateInstance<ScreenFilterProfile>();
            try { EditorUtility.CopySerialized(defaults, target); }
            finally { UnityEngine.Object.DestroyImmediate(defaults); }
            target.name = name;
            target.hideFlags = flags;
            if (preset > 0 && preset <= 5) target.GetSettings((ScreenFilterKind)(preset - 1)).Enabled = true;
            target.Normalize();
            EditorUtility.SetDirty(target);
        }
    }
}
