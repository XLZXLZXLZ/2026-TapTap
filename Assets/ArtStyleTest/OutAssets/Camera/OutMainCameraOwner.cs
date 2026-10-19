using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

namespace ArtStyleTest {
 [DisallowMultipleComponent,RequireComponent(typeof(Camera)),DefaultExecutionOrder(10000)]
 public sealed class OutMainCameraOwner:MonoBehaviour {
  void OnEnable(){if(!Application.isPlaying)return;SceneManager.sceneLoaded+=SceneLoaded;TakeOwnership();}
  void Start(){TakeOwnership();}
  void OnDisable(){SceneManager.sceneLoaded-=SceneLoaded;}
  void SceneLoaded(Scene scene,LoadSceneMode mode){TakeOwnership();}
  public void TakeOwnership(){if(!Application.isPlaying||!isActiveAndEnabled)return;var own=GetComponent<Camera>();own.enabled=true;own.tag="MainCamera";own.targetDisplay=0;own.GetUniversalAdditionalCameraData().renderType=CameraRenderType.Base;
   foreach(var c in FindObjectsOfType<Camera>()){if(c==own||c.cameraType!=CameraType.Game||c.targetTexture||c.targetDisplay!=own.targetDisplay)continue;var data=c.GetComponent<UniversalAdditionalCameraData>();if(data&&data.renderType==CameraRenderType.Overlay)continue;c.enabled=false;var listener=c.GetComponent<AudioListener>();if(listener)listener.enabled=false;}
   var audio=GetComponent<AudioListener>();if(audio)audio.enabled=true;
  }
 }
}
