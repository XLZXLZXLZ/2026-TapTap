using UnityEditor;using UnityEngine;
public sealed class MeadowCameraFocusWindow:EditorWindow {
 Editor focusEditor;MeadowCameraFocus current;
 [MenuItem("Tools/草地相机焦点")]
 public static void Open(){var w=GetWindow<MeadowCameraFocusWindow>(true,"草地相机焦点",true);w.minSize=new Vector2(380,425);w.Show();}
 void OnGUI(){var f=MeadowCameraFocus.active;if(!f){EditorGUILayout.HelpBox("打开草地场景后即可调整。",MessageType.Info);return;}if(current!=f||!focusEditor){if(focusEditor)DestroyImmediate(focusEditor);current=f;focusEditor=Editor.CreateEditor(f);}focusEditor.OnInspectorGUI();EditorGUILayout.Space();if(GUILayout.Button("选中摄影机"))Selection.activeGameObject=f.gameObject;}
 void OnDisable(){if(focusEditor)DestroyImmediate(focusEditor);}
}
