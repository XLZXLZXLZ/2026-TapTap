using UnityEngine;
[ExecuteAlways,DisallowMultipleComponent]
public sealed class MeadowGrassDistanceBlend:MonoBehaviour {
 [Header("草地远近衔接")]
 public Camera referenceCamera;
 [Tooltip("此距离以内保留原来的草高，单位米。"),Min(1)]public float transitionStart=18;
 [Tooltip("在此距离逐渐过渡到远景草高。"),Min(2)]public float transitionEnd=65;
 [Tooltip("远景草高相对于原草高的比例。"),Range(.08f,1)]public float distantHeight=.24f;
 public void Apply(){if(!referenceCamera)return;Shader.SetGlobalVector("_MeadowGrassDistanceSettings",new Vector4(transitionStart,Mathf.Max(transitionStart+1,transitionEnd),distantHeight,1));Shader.SetGlobalVector("_MeadowGrassViewPosition",referenceCamera.transform.position);}
 void OnEnable(){Apply();}void Update(){Apply();}void OnValidate(){Apply();}
 void OnDisable(){Shader.SetGlobalVector("_MeadowGrassDistanceSettings",Vector4.zero);}
}
