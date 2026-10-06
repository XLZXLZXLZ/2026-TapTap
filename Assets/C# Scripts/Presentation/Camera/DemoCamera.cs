using UnityEngine;

namespace TapTap
{
    public sealed class DemoCamera : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Transform shakeRoot;
        private Vector3 followVelocity;
        private float baseSize;
        private Vector3 shakeOrigin;
        private bool initialized;

        public void Configure(PlayerController target, Camera camera, Transform motionRoot)
        {
            player = target; viewCamera = camera; shakeRoot = motionRoot;
            initialized = false;
            Initialize();
        }

        private void Awake() => Initialize();

        private void Initialize()
        {
            if (initialized || viewCamera == null) return;
            baseSize = viewCamera.orthographicSize;
            shakeOrigin = shakeRoot != null ? shakeRoot.localPosition : Vector3.zero;
            initialized = true;
        }

        private void LateUpdate()
        {
            if (player == null || player.Body == null) return;
            Initialize();
            Vector3 bodyPosition = player.Body.transform.position;
            Vector3 target = new Vector3(bodyPosition.x + 3f, Mathf.Max(3.5f, bodyPosition.y + 2f), -10f);
            transform.position = Vector3.SmoothDamp(transform.position, target, ref followVelocity, 0.2f);
            EffectManager effects = EffectManager.Instance;
            if (shakeRoot != null) shakeRoot.localPosition = shakeOrigin + (Vector3)effects.ShakeOffset;
            if (viewCamera != null) viewCamera.orthographicSize = baseSize - effects.ZoomPulse;
        }
    }
}
