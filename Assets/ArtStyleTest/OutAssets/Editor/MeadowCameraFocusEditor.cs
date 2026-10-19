using UnityEditor;using UnityEngine;
[CustomEditor(typeof(MeadowCameraFocus))]
public class MeadowCameraFocusEditor:Editor {
 public override void OnInspectorGUI(){serializedObject.Update();EditorGUILayout.LabelField("相机焦点与远景空气色",EditorStyles.boldLabel);EditorGUILayout.HelpBox("中景清楚，太近和太远逐渐虚焦。远景单独加入灰青色空气透视。",MessageType.Info);Field("depthOfField","启用近远景虚焦");Field("focusTarget","对焦物体（可不填）");Field("focusDistance","对焦距离（米）");Field("focalLength","虚焦强度 / 焦距");Field("aperture","光圈（越小越虚）");EditorGUILayout.Space();Field("aerialPerspective","启用远景空气色");Field("hazeStart","空气色起始距离");Field("hazeEnd","空气色充分显现距离");Field("hazeStrength","远景空气色强度");Field("hazeColour","远景灰青颜色");serializedObject.ApplyModifiedProperties();}
 void Field(string name,string label){EditorGUILayout.PropertyField(serializedObject.FindProperty(name),new GUIContent(label));}
}
