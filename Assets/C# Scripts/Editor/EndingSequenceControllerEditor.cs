using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    [CustomEditor(typeof(EndingSequenceController))]
    public sealed class EndingSequenceControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("曲线从角色当前位置开始，平滑经过列表中的每个锚点；控制柄自动生成，可用贝塞尔曲率调整弯曲程度。路径点只控制 X/Z，Y 向下检测场景碰撞。开启 Gizmos 即可查看贴地曲线，红色表示未找到可行走地面。选中本对象可拖动编号点的水平手柄。", MessageType.Info);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                if (GUILayout.Button("追加路径点")) AppendWaypoint();
        }

        private void AppendWaypoint()
        {
            var sequence = (EndingSequenceController)target;
            serializedObject.Update();
            SerializedProperty points = serializedObject.FindProperty(nameof(EndingSequenceController.IntroWaypoints));
            Transform previous = sequence.Player != null ? sequence.Player.transform : sequence.transform;
            for (int index = points.arraySize - 1; index >= 0; index--)
            {
                var point = points.GetArrayElementAtIndex(index).objectReferenceValue as Transform;
                if (point == null) continue;
                previous = point;
                break;
            }
            Vector3 right = sequence.ViewCamera != null
                ? Vector3.ProjectOnPlane(sequence.ViewCamera.transform.right, Vector3.up).normalized : Vector3.right;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("添加结局路径点");
            var node = new GameObject("Waypoint " + (points.arraySize + 1).ToString("00"));
            Undo.RegisterCreatedObjectUndo(node, "添加结局路径点");
            Undo.SetTransformParent(node.transform, sequence.transform, "添加结局路径点");
            node.transform.position = previous.position + right;
            int next = points.arraySize;
            points.arraySize++;
            points.GetArrayElementAtIndex(next).objectReferenceValue = node.transform;
            serializedObject.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(undoGroup);
            SceneView.RepaintAll();
        }

        private void OnSceneGUI()
        {
            var sequence = (EndingSequenceController)target;
            if (sequence.IntroWaypoints == null) return;
            sequence.RefreshPathPreview();
            for (int index = 0; index < sequence.IntroWaypoints.Length; index++)
            {
                Transform point = sequence.IntroWaypoints[index];
                if (point == null) continue;
                Vector3 display = sequence.WaypointPreviewPosition(point);
                Handles.Label(display + Vector3.up * 0.25f, (index + 1) + ". " + point.name);
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 position = Handles.Slider2D(display, Vector3.up, Vector3.right, Vector3.forward,
                        HandleUtility.GetHandleSize(display) * 0.08f, Handles.RectangleHandleCap, Vector2.zero);
                    if (!EditorGUI.EndChangeCheck()) continue;
                    Undo.RecordObject(point, "移动结局路径点");
                    point.position = new Vector3(position.x, point.position.y, position.z);
                    EditorUtility.SetDirty(point);
                }
            }
            if (sequence.Player == null || !sequence.UseLastWaypointAsWalkLine) return;
            Transform last = null;
            foreach (Transform point in sequence.IntroWaypoints)
                if (point != null) last = point;
            Vector3 origin = last != null ? sequence.WaypointPreviewPosition(last) : sequence.Player.transform.position;
            Handles.Label(origin + Vector3.up * 0.5f, "左右移动线");
        }
    }
}
