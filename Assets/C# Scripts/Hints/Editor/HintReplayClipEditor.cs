using UnityEditor;
using UnityEngine;

namespace TapTap.Hints.Editor
{
    [CustomEditor(typeof(HintReplayClip))]
    public sealed class HintReplayClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var clip = (HintReplayClip)target;
            EditorGUILayout.LabelField("时长", clip.Duration.ToString("F2") + " 秒");
            EditorGUILayout.LabelField("坐标空间", clip.UsesWorldSpace ? "世界坐标（Trigger 只切换录像）" : "相对参考点（正向 / 旧版录像）");
            EditorGUILayout.LabelField("姿态帧", (clip.Frames?.Length ?? 0).ToString());
            EditorGUILayout.LabelField("显示节点（包含骨骼）", (clip.Nodes?.Length ?? 0).ToString());
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("notes"));
            serializedObject.ApplyModifiedProperties();
            if (!clip.Validate(out string error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
            if (GUILayout.Button("打开录制 / 预览窗口")) HintReplayWindow.Open();
        }
    }
}
