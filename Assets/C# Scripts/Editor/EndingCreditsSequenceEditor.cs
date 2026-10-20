using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    [CustomEditor(typeof(EndingCreditsSequence))]
    public sealed class EndingCreditsSequenceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("三页顺序为制作单位、作者、特别鸣谢；鸣谢中的三行依次淡入，最后整体淡出。预览按钮只切换当前预制体的显示，支持撤销。", MessageType.Info);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("预览制作单位")) Preview(0);
                if (GUILayout.Button("预览作者")) Preview(1);
                if (GUILayout.Button("预览鸣谢")) Preview(2);
            }
        }

        private void Preview(int page)
        {
            var credits = (EndingCreditsSequence)target;
            CanvasGroup[] panels = { credits.StudioPanel, credits.AuthorsPanel, credits.ThanksPanel };
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("预览结局署名");
            for (int index = 0; index < panels.Length; index++)
                SetVisible(panels[index], index == page);
            if (credits.ThanksLines != null)
                foreach (CanvasGroup line in credits.ThanksLines) SetVisible(line, true);
            Undo.CollapseUndoOperations(undoGroup);
            SceneView.RepaintAll();
        }

        private static void SetVisible(CanvasGroup group, bool visible)
        {
            if (group == null) return;
            Undo.RecordObjects(new Object[] { group, group.gameObject }, "预览结局署名");
            group.gameObject.SetActive(visible);
            group.alpha = visible ? 1f : 0f;
            EditorUtility.SetDirty(group);
            EditorUtility.SetDirty(group.gameObject);
        }
    }
}
