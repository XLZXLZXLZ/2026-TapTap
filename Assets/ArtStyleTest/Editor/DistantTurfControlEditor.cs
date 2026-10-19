using UnityEngine;using UnityEditor;
[CustomEditor(typeof(DistantTurfControl))]
public class DistantTurfControlEditor:Editor {
 MaterialEditor editor;
 void OnDisable(){if(editor)DestroyImmediate(editor);}
 public override void OnInspectorGUI(){var control=(DistantTurfControl)target;EditorGUILayout.LabelField("远景建筑草皮",EditorStyles.boldLabel);EditorGUILayout.HelpBox("颜色和覆盖率实时生效。块大小越大，草皮越连片；覆盖率 0 为全不覆盖，1 为全部覆盖。",MessageType.Info);serializedObject.Update();EditorGUILayout.PropertyField(serializedObject.FindProperty("grassMaterial"),new GUIContent("草皮材质"));serializedObject.ApplyModifiedProperties();if(!control.grassMaterial)return;if(!editor||editor.target!=control.grassMaterial){if(editor)DestroyImmediate(editor);editor=(MaterialEditor)CreateEditor(control.grassMaterial);}var properties=MaterialEditor.GetMaterialProperties(new Object[]{control.grassMaterial});EditorGUI.BeginChangeCheck();foreach(var name in new[]{"_TurfColour","_Coverage","_PatchSize","_TopBias","_EdgeBreakup"}){var p=System.Array.Find(properties,x=>x.name==name);if(p!=null)editor.ShaderProperty(p,p.displayName);}if(EditorGUI.EndChangeCheck()){EditorUtility.SetDirty(control.grassMaterial);SceneView.RepaintAll();EditorApplication.QueuePlayerLoopUpdate();}}
}
