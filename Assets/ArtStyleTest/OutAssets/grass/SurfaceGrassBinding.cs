using UnityEngine;
[ExecuteAlways,DisallowMultipleComponent]
public sealed class SurfaceGrassBinding:MonoBehaviour {
 [Tooltip("草和苔藓附着的房屋或石头，移动原物体时会自动跟随。")]
 public Transform surface;
 public void Apply(){if(!surface)return;transform.SetPositionAndRotation(surface.position,surface.rotation);var p=transform.parent?transform.parent.lossyScale:Vector3.one;var s=surface.lossyScale;transform.localScale=new Vector3(s.x/Mathf.Max(.0001f,p.x),s.y/Mathf.Max(.0001f,p.y),s.z/Mathf.Max(.0001f,p.z));}
 void OnEnable(){Apply();}void LateUpdate(){Apply();}void OnValidate(){Apply();}
}
