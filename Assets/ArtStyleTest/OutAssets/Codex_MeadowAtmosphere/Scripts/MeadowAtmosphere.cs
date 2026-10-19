using UnityEngine;
using UnityEngine.Rendering.Universal;

[ExecuteAlways,DisallowMultipleComponent]
public sealed class MeadowAtmosphere:MonoBehaviour {
 public static MeadowAtmosphere active;
 [Header("作用范围")]
 public Camera viewCamera;
 public Vector3 fogSize=new Vector3(150,26,140);
 [Min(0.01f)]public float edgeSoftness=12;
 [Header("薄雾")]
 public bool fogEnabled=true;
 [Range(0,0.06f)]public float density=0.009f;
 public Color fogColour=new Color(.67f,.79f,.86f,1);
 public Color sunScatterColour=new Color(1,.94f,.77f,1);
 [Min(.1f)]public float heightFalloff=9;
 public float baseHeight=0;
 [Min(0)]public float clearNearCamera=10;
 [Range(0,1)]public float noiseAmount=.55f;
 [Range(.005f,.2f)]public float noiseScale=.035f;
 [Range(16,64)]public int steps=40;
 [Range(0,2)]public float scattering=.38f;
 [Range(0,.8f)]public float anisotropy=.5f;
 [Range(0,1)]public float ambientScattering=.6f;
 public bool preserveReferenceLighting;
 [Range(0,2)]public float extraSunShafts=0;
 [Min(1)]public float sunShaftHeight=72;
 public float sunShaftElevation=12;
 public Texture2D sunShaftCookie;
 public Vector3 sunShaftDirection=new Vector3(39,243,0);
 public bool showInSceneView=true;
 [Header("阳光与云影")]
 public Light sunlight;
 public Texture2D cloudCookie;
 public bool cloudShadowsEnabled=true;
 public Vector2 cloudSize=new Vector2(82,82);
 public Vector2 cloudOffset=new Vector2(0,0);
 public Vector2 cloudVelocity=new Vector2(.15f,.07f);
 public bool animateClouds=true;
 [HideInInspector]public bool freezePreview;
 [HideInInspector]public float previewTime;
 public float CurrentTime {get {if(freezePreview)return previewTime;return Application.isPlaying?Time.time:(float)Time.realtimeSinceStartup;}}
 Vector2 initialOffset;float initialClock;
 void OnEnable(){active=this;initialClock=CurrentTime;initialOffset=cloudOffset;UpdateClouds();}
 void OnDisable(){if(active==this)active=null;if(sunlight&&sunlight.cookie==cloudCookie)sunlight.cookie=null;}
 void OnValidate(){fogSize=Vector3.Max(fogSize,Vector3.one);cloudSize=Vector2.Max(cloudSize,Vector2.one);steps=Mathf.Clamp(steps,16,64);UpdateClouds();}
 void Update(){active=this;UpdateClouds();}
 public void UpdateClouds(){if(!sunlight)return;sunlight.cookie=cloudShadowsEnabled?cloudCookie:null;var data=sunlight.GetComponent<UniversalAdditionalLightData>();if(data){data.lightCookieSize=cloudSize;data.lightCookieOffset=cloudOffset+(animateClouds?cloudVelocity*(freezePreview?previewTime:(Application.isPlaying?Time.time:0)):Vector2.zero);}}
 void OnDrawGizmosSelected(){Gizmos.matrix=transform.localToWorldMatrix;Gizmos.color=new Color(.4f,.78f,1,.65f);Gizmos.DrawWireCube(Vector3.zero,fogSize);Gizmos.color=new Color(.4f,.78f,1,.045f);Gizmos.DrawCube(Vector3.zero,fogSize);}
}
