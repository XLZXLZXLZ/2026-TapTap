using UnityEngine;using UnityEditor;
namespace CodexOutTransfer {
public sealed class MeadowSkyShaderGUI:ShaderGUI {
 public override void OnGUI(MaterialEditor editor,MaterialProperty[] properties){EditorGUILayout.HelpBox("这些云由天空材质绘制，不是层级里的独立模型。修改后会实时更新场景和游戏画面。",MessageType.Info);foreach(var key in new[]{"_Zenith","_Horizon","_Cloud","_SkyRotation","_CloudOpacity","_CloudSoftness"}){var p=FindProperty(key,properties);string label=key=="_Zenith"?"天空顶部颜色":key=="_Horizon"?"地平线颜色":key=="_Cloud"?"云朵颜色":key=="_SkyRotation"?"整体旋转（度）":key=="_CloudOpacity"?"云朵不透明度":"云边柔和度";editor.ShaderProperty(p,label);}
 for(int i=1;i<=4;i++){EditorGUILayout.Space();EditorGUILayout.LabelField("第 "+i+" 组云",EditorStyles.boldLabel);var p=FindProperty("_Cloud"+i,properties);var v=p.vectorValue;EditorGUI.BeginChangeCheck();v.x=EditorGUILayout.Slider("左右位置（度）",v.x,-180,180);v.y=EditorGUILayout.Slider("高低位置（度）",v.y,-30,80);v.z=EditorGUILayout.Slider("横向长度（度）",v.z,.1f,130);v.w=EditorGUILayout.Slider("厚度（度）",v.w,.05f,18);if(EditorGUI.EndChangeCheck()){editor.RegisterPropertyChangeUndo("调整云朵");p.vectorValue=v;}}
 }
}
public sealed class MeadowSkyEditor:EditorWindow {
 Editor editor;Material previous;Vector2 scroll;
 [MenuItem("Tools/天空与云朵")]
 public static void Open(){var w=GetWindow<MeadowSkyEditor>("天空与云朵");w.minSize=new Vector2(350,450);w.Show();}
 void OnGUI(){var material=RenderSettings.skybox;if(!material){EditorGUILayout.HelpBox("当前场景没有天空材质。",MessageType.Info);return;}EditorGUILayout.ObjectField("当前天空材质",material,typeof(Material),false);if(GUILayout.Button("在项目面板中定位材质")){Selection.activeObject=material;EditorGUIUtility.PingObject(material);}if(previous!=material){if(editor)DestroyImmediate(editor);editor=Editor.CreateEditor(material);previous=material;}scroll=EditorGUILayout.BeginScrollView(scroll);if(editor)editor.OnInspectorGUI();EditorGUILayout.EndScrollView();if(GUI.changed){SceneView.RepaintAll();Repaint();}}
 void OnDisable(){if(editor)DestroyImmediate(editor);}
}
}
