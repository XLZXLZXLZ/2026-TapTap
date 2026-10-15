using UnityEditor;

namespace TapTap.Editor
{
    public sealed partial class LevelEditorWindow
    {
        private void DrawCameraSidebar()
        {
            if (layout != null) LevelCameraAuthoring.DrawPanel(layout);
            EditorGUILayout.Space(6f);
        }
    }
}
