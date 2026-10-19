using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;
[CustomEditor(typeof(MeadowPainterlyStyle))]
public sealed class MeadowPainterlyStyleEditor:Editor {
 void P(string name,string label){EditorGUILayout.PropertyField(serializedObject.FindProperty(name),new GUIContent(label));}
 public override void OnInspectorGUI(){serializedObject.Update();EditorGUILayout.LabelField("柔和卡通色彩",EditorStyles.boldLabel);P("cartoonStrength","卡通化程度");P("shadowColour","暗部颜色");P("shadowBrightness","暗部亮度");P("sunlitTint","受光颜色");P("transitionSoftness","明暗过渡柔和度");EditorGUILayout.Space();EditorGUILayout.LabelField("树冠柔和暗部",EditorStyles.boldLabel);P("softenTreeShadows","启用树冠柔和暗部");P("treeShadowSoftness","树冠暗部柔化程度");P("treeCoolShade","灰青绿暗部");P("treeOliveShade","橄榄色暗部");EditorGUILayout.Space();EditorGUILayout.LabelField("草地整体色块",EditorStyles.boldLabel);P("unifyGrassColour","启用整体草色");P("grassWarmCoolPalette","启用草地冷暖层次");P("grassColour","草地中间色");P("grassSunlitColour","草地受光颜色");P("grassShadeColour","草地背光颜色");P("grassColourUnity","草色整体度");P("grassShadowStrength","草地大块阴影强度");P("grassPatchVariation","草地缓慢色彩变化");if(serializedObject.ApplyModifiedProperties()){var v=(MeadowPainterlyStyle)target;v.Apply();EditorSceneManager.MarkSceneDirty(v.gameObject.scene);SceneView.RepaintAll();}}
}

