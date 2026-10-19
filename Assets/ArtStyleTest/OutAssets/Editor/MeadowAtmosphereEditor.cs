using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;
[CustomEditor(typeof(MeadowAtmosphere))]
public sealed class MeadowAtmosphereEditor:Editor{
 void P(string id,string label){EditorGUILayout.PropertyField(serializedObject.FindProperty(id),new GUIContent(label));}
 public override void OnInspectorGUI(){serializedObject.Update();var v=(MeadowAtmosphere)target;EditorGUILayout.LabelField("草坡 · 局部雾与云影",EditorStyles.boldLabel);EditorGUILayout.Space();
 EditorGUILayout.LabelField("局部体积雾",EditorStyles.boldLabel);P("fogEnabled","启用体积雾");P("fogSize","雾范围（宽 / 高 / 深）");P("density","雾浓度");P("fogColour","雾的颜色");P("sunScatterColour","受光雾色");P("edgeSoftness","范围边缘柔化");P("baseHeight","雾的基准高度");P("heightFalloff","向高处变淡");P("clearNearCamera","镜头前清晰距离");if(v.preserveReferenceLighting){P("extraSunShafts","丁达尔光强度");P("sunShaftHeight","光束范围高度");}else P("scattering","丁达尔光强度");P("anisotropy","光线聚拢程度");P("ambientScattering","空气中的天光");P("noiseAmount","雾的疏密变化");P("noiseScale","疏密纹理大小");P("steps","体积雾精度");
 EditorGUILayout.Space();EditorGUILayout.LabelField("云朵投在地面的阴影",EditorStyles.boldLabel);P("cloudShadowsEnabled","启用云影");P("cloudSize","云影覆盖尺寸");P("cloudOffset","云影位置");P("animateClouds","播放时缓慢移动");P("cloudVelocity","云影移动速度");
 EditorGUILayout.Space();EditorGUILayout.LabelField("阳光",EditorStyles.boldLabel);P("sunlight","太阳灯光");if(v.sunlight){EditorGUI.BeginChangeCheck();var c=EditorGUILayout.ColorField("阳光颜色",v.sunlight.color);float f=EditorGUILayout.Slider("阳光强度",v.sunlight.intensity,.1f,8);if(EditorGUI.EndChangeCheck()){Undo.RecordObject(v.sunlight,"Adjust meadow sunlight");v.sunlight.color=c;v.sunlight.intensity=f;EditorUtility.SetDirty(v.sunlight);EditorSceneManager.MarkSceneDirty(v.gameObject.scene);}}
 EditorGUILayout.Space();P("showInSceneView","在场景窗口预览");if(GUILayout.Button("在场景中显示雾范围")){Selection.activeGameObject=v.gameObject;if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.Frame(new Bounds(v.transform.position,v.fogSize),false);}
 if(serializedObject.ApplyModifiedProperties()){v.UpdateClouds();EditorSceneManager.MarkSceneDirty(v.gameObject.scene);SceneView.RepaintAll();}
 }
}
