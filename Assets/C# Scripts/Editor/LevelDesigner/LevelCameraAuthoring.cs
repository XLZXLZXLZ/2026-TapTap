using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public static class LevelCameraAuthoring
    {
        private static readonly Dictionary<LevelDefinition, LevelRegion> contexts = new Dictionary<LevelDefinition, LevelRegion>();
        private static bool expanded = true, advanced;
        private static string error;

        public static LevelRegion FindContext(LevelDefinition level, LevelRegion preferred = null)
        {
            if (preferred != null && preferred.Source == level) return preferred;
            if (contexts.TryGetValue(level, out LevelRegion saved) && saved != null && saved.Source == level) return saved;
            LevelRegion selected = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<LevelRegion>() : null;
            if (selected != null && selected.Source == level) return selected;
            return UnityEngine.Object.FindObjectsOfType<LevelRegion>(true)
                .FirstOrDefault(region => region.Source == level && region.gameObject.scene.IsValid()
                    && region.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(region));
        }

        public static void GenerateDefault(LevelDefinition level, float aspect = 1.6f)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            Undo.RecordObject(level, "生成固定透视镜头");
            if (level.CameraSettings == null) level.CameraSettings = new LevelCameraSettings();
            level.CameraSettings.GenerateDefault(level.Footprint.Bounds, aspect,
                level.Palette != null ? level.Palette.UnitSize : 1f);
            EditorUtility.SetDirty(level);
        }

        public static void Capture(LevelDefinition level, LevelRegion region, Camera camera)
        {
            if (level == null || region == null || region.Source != level || camera == null)
                throw new ArgumentException("请选择本关在场景中的区域实例和参考相机。");
            if (camera.orthographic) throw new ArgumentException("请先将参考相机或 Scene 视图切换为透视模式。");
            Undo.RecordObject(level, "采集关卡镜头");
            if (level.CameraSettings == null) level.CameraSettings = new LevelCameraSettings();
            level.CameraSettings.Capture(camera, region);
            contexts[level] = region;
            EditorUtility.SetDirty(level);
        }

        public static void DrawPanel(LevelDefinition level, LevelRegion preferred = null)
        {
            if (level == null) return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            expanded = EditorGUILayout.Foldout(expanded, "固定透视镜头（关卡共享）", true);
            if (!expanded) { EditorGUILayout.EndVertical(); return; }
            float previousWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 94f;
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                var serialized = new SerializedObject(level);
                serialized.Update();
                SerializedProperty settings = serialized.FindProperty("CameraSettings");
                Field(settings, "HasPreset", "使用已保存预设");
                bool authored = settings.FindPropertyRelative("HasPreset").boolValue;
                using (new EditorGUI.DisabledScope(!authored))
                {
                    Field(settings, "LocalPosition", "位置（格）");
                    Field(settings, "LocalEulerAngles", "局部旋转");
                }
                if (!authored) EditorGUILayout.HelpBox("尚未保存预设：运行时按区域范围使用固定镜头。可先生成默认镜头再调整。", MessageType.Info);
                Field(settings, "LensMode", "镜头模式");
                if (settings.FindPropertyRelative("LensMode").enumValueIndex == (int)CameraLensMode.Physical)
                {
                    Field(settings, "FocalLength", "焦距（mm）");
                    Field(settings, "SensorSize", "传感器（mm）");
                    Field(settings, "LensShift", "镜头偏移");
                    Field(settings, "GateFit", "画幅适配");
                }
                else Field(settings, "FieldOfView", "垂直 FOV（°）");
                Field(settings, "BlendDuration", "进入过渡（秒）");
                Field(settings, "BlendCurve", "过渡曲线");
                advanced = EditorGUILayout.Foldout(advanced, "裁剪设置", true);
                if (advanced)
                {
                    Field(settings, "NearClip", "近裁剪");
                    Field(settings, "FarClip", "远裁剪");
                }
                serialized.ApplyModifiedProperties();
                LevelRegion context = FindContext(level, preferred);
                context = (LevelRegion)EditorGUILayout.ObjectField("场景参照", context, typeof(LevelRegion), true);
                if (context != null && context.Source != level)
                    EditorGUILayout.HelpBox("所选区域的 Source 不是当前关卡。", MessageType.Warning);
                else contexts[level] = context;
                bool validContext = context != null && context.Source == level;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("生成默认镜头")) Run(() => GenerateDefault(level));
                if (GUILayout.Button("保存镜头")) Run(() => AssetDatabase.SaveAssetIfDirty(level));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(!validContext || SceneView.lastActiveSceneView == null))
                    if (GUILayout.Button("采集 Scene 视图"))
                        Run(() => Capture(level, context, SceneView.lastActiveSceneView.camera));
                Camera selectedCamera = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Camera>() : null;
                using (new EditorGUI.DisabledScope(!validContext || selectedCamera == null))
                    if (GUILayout.Button("采集选中相机")) Run(() => Capture(level, context, selectedCamera));
                EditorGUILayout.EndHorizontal();
                using (new EditorGUI.DisabledScope(!validContext))
                    if (GUILayout.Button("预览本关镜头")) Run(() => LevelCameraPreviewWindow.Open(level, context));
                if (!validContext) EditorGUILayout.HelpBox("采集和预览前，请打开本关测试场景或包含本关的大地图，并指定区域实例。", MessageType.Info);
                if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            EditorGUIUtility.labelWidth = previousWidth;
            EditorGUILayout.EndVertical();
        }

        private static void Field(SerializedProperty settings, string name, string label) =>
            EditorGUILayout.PropertyField(settings.FindPropertyRelative(name), new GUIContent(label), true);

        private static void Run(Action action)
        {
            try { action(); error = null; }
            catch (Exception exception) { error = exception.Message; }
        }
    }

    [CustomEditor(typeof(LevelDefinition))]
    public sealed class LevelDefinitionCameraInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            LevelCameraAuthoring.DrawPanel((LevelDefinition)target);
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "CameraSettings");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(LevelRegion))]
    public sealed class LevelRegionCameraInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var region = (LevelRegion)target;
            serializedObject.Update();
            if (region.Source != null) DrawPropertiesExcluding(serializedObject, "cameraSettings");
            else DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();
            if (region.Source != null) LevelCameraAuthoring.DrawPanel(region.Source, region);
        }
    }
}
