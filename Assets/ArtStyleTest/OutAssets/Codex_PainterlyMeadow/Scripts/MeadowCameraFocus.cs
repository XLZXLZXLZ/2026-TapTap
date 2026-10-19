using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[ExecuteAlways,DisallowMultipleComponent,RequireComponent(typeof(Camera))]
public sealed class MeadowCameraFocus:MonoBehaviour {
 public static MeadowCameraFocus active;
 public VolumeProfile focusProfile;
 public bool depthOfField=true;
 public Transform focusTarget;
 [Min(.5f)]public float focusDistance=22;
 [Range(1,300)]public float focalLength=170;
 [Range(1,32)]public float aperture=2.6f;
 public bool aerialPerspective=true;
 [Min(0)]public float hazeStart=25;
 [Min(1)]public float hazeEnd=170;
 [Range(0,1)]public float hazeStrength=.68f;
 public Color hazeColour=new Color(.57f,.74f,.73f,1);
 public float EffectiveFocusDistance {get{return focusTarget?Mathf.Max(.5f,Vector3.Dot(focusTarget.position-transform.position,transform.forward)):Mathf.Max(.5f,focusDistance);}}
 void OnEnable(){active=this;Apply();}
 void OnValidate(){hazeEnd=Mathf.Max(hazeStart+1,hazeEnd);Apply();}
 void Update(){active=this;Apply();}
 void OnDisable(){if(active==this)active=null;}
 public void Apply(){if(!focusProfile)return;DepthOfField d;if(!focusProfile.TryGet(out d))return;d.active=true;d.mode.Override(depthOfField?DepthOfFieldMode.Bokeh:DepthOfFieldMode.Off);d.focusDistance.Override(EffectiveFocusDistance);d.focalLength.Override(focalLength);d.aperture.Override(aperture);d.bladeCount.Override(7);d.bladeCurvature.Override(1);}
 void OnDrawGizmosSelected(){var camera=GetComponent<Camera>();float d=EffectiveFocusDistance;float h=Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f)*d;Gizmos.matrix=transform.localToWorldMatrix;Gizmos.color=new Color(.8f,1,.5f,.7f);Gizmos.DrawWireCube(new Vector3(0,0,d),new Vector3(h*2*camera.aspect,h*2,.01f));}
}
