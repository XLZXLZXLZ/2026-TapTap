using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    internal sealed class AnnotationEditorPopup : PopupWindowContent
    {
        private readonly LevelDefinition layout;
        private readonly string placementId;
        private readonly System.Action changed;
        private readonly System.Action closed;
        private string text;
        private Vector2 scroll;
        private bool focus = true;

        public AnnotationEditorPopup(LevelDefinition layout, LevelPlacement placement, System.Action changed, System.Action closed = null)
        {
            this.layout = layout;
            placementId = placement.Id;
            this.changed = changed;
            this.closed = closed;
            text = (placement.Settings as AnnotationPlacementSettings)?.Text ?? "";
        }
        public override Vector2 GetWindowSize() => new Vector2(380f, 260f);
        public override void OnClose()
        {
            // FocusTextInControl sets a shared editor flag which outlives the popup.
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
            closed?.Invoke();
        }
        public override void OnGUI(Rect rect)
        {
            if (layout == null) { editorWindow.Close(); return; }
            EditorGUILayout.LabelField("编辑测试批注", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            GUI.SetNextControlName("AnnotationText");
            text = EditorGUILayout.TextArea(text, GUILayout.ExpandHeight(true), GUILayout.MinHeight(170f));
            EditorGUILayout.EndScrollView();
            if (focus) { EditorGUI.FocusTextInControl("AnnotationText"); focus = false; }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存"))
            {
                LevelPlacement placement = layout.Placements.Find(item => item != null && item.Id == placementId);
                if (placement?.Settings is AnnotationPlacementSettings settings && settings.Text != text)
                {
                    Undo.RegisterCompleteObjectUndo(layout, "编辑批注");
                    settings.Text = text;
                    EditorUtility.SetDirty(layout);
                    changed?.Invoke();
                    AssetDatabase.SaveAssetIfDirty(layout);
                }
                editorWindow.Close();
            }
            if (GUILayout.Button("取消")) editorWindow.Close();
            EditorGUILayout.EndHorizontal();
        }
    }
}
