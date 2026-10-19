using System;using System.IO;using System.Linq;using System.Text;using UnityEngine;using UnityEngine.SceneManagement;using UnityEditor;
namespace CodexOutTransfer {
public static class LocateOutClouds {
[MenuItem("Tools/Out Scene Transfer/Locate Active Sky And Clouds")]
public static void Run(){var b=new StringBuilder();try{var scene=SceneManager.GetSceneByPath("Assets/ArtStyleTest/Scenes/out.unity");if(!scene.isLoaded)throw new Exception("out is not open");SceneManager.SetActiveScene(scene);var sky=RenderSettings.skybox;b.AppendLine("sky="+AssetDatabase.GetAssetPath(sky)+" name="+sky?.name+" shader="+sky?.shader?.name);if(sky){for(int i=0;i<ShaderUtil.GetPropertyCount(sky.shader);i++){var n=ShaderUtil.GetPropertyName(sky.shader,i);var kind=ShaderUtil.GetPropertyType(sky.shader,i);b.AppendLine("property="+n+" label="+ShaderUtil.GetPropertyDescription(sky.shader,i)+" type="+kind+(kind==ShaderUtil.ShaderPropertyType.Color?" value="+sky.GetColor(n):""));}b.AppendLine("shaderFile="+AssetDatabase.GetAssetPath(sky.shader));Selection.activeObject=sky;EditorGUIUtility.PingObject(sky);}
foreach(var g in scene.GetRootGameObjects().Where(g=>g.name=="Background_Sky")){foreach(var r in g.GetComponentsInChildren<Renderer>(true))b.AppendLine("oldSkyObject="+r.name+" enabled="+r.enabled+" active="+r.gameObject.activeInHierarchy);}
b.AppendLine("materialSelected=True sceneUnchanged=True");}catch(Exception e){b.AppendLine(e.ToString());}File.WriteAllText("C:/Users/lenovo/Documents/Codex/2026-10-16/new-chat/work/out_cloud_location.txt",b.ToString());}
}
}
