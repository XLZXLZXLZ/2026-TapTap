using UnityEngine;
using UnityEngine.Rendering;

namespace ScreenFilters
{
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    [AddComponentMenu("Rendering/Screen Filter Camera")]
    public sealed class ScreenFilterCamera : MonoBehaviour
    {
        [SerializeField, Tooltip("多个相机可以引用同一份公共滤镜配置；为空时不处理画面。")]
        private ScreenFilterProfile profile;
        private Camera attachedCamera;
        private bool ownsMSAA;
        private bool previousMSAA;
        public ScreenFilterProfile Profile => profile;
        public bool HasPixelation => isActiveAndEnabled && profile != null
            && profile.IsActive(ScreenFilterKind.Pixelation);

        public void SetProfile(ScreenFilterProfile value) { profile = value; RefreshCameraState(); }

        private void OnEnable()
        {
            attachedCamera = GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            RefreshCameraState();
        }

        private void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == attachedCamera) RefreshCameraState();
        }

        private void OnValidate() { attachedCamera = GetComponent<Camera>(); RefreshCameraState(); }

        private void RefreshCameraState()
        {
            if (attachedCamera == null) attachedCamera = GetComponent<Camera>();
            if (attachedCamera == null) return;
            if (HasPixelation)
            {
                if (!ownsMSAA) { previousMSAA = attachedCamera.allowMSAA; ownsMSAA = true; }
                attachedCamera.allowMSAA = false;
            }
            else RestoreMSAA();
        }

        private void RestoreMSAA()
        {
            if (ownsMSAA && attachedCamera != null) attachedCamera.allowMSAA = previousMSAA;
            ownsMSAA = false;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RestoreMSAA();
        }
    }
}
