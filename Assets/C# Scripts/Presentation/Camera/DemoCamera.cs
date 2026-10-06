using UnityEngine;

namespace TapTap
{
    public sealed class DemoCamera : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Transform shakeRoot;
        [SerializeField] private Vector2 followOffset = new Vector2(3f, 2f);
        [SerializeField] private float minimumY = 3.5f;
        [SerializeField, Min(0.01f)] private float followSmoothTime = 0.2f;
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

        public void BindPlayer(PlayerController target)
        {
            player = target;
            Initialize();
        }

        public void SnapToPlayer()
        {
            if (player == null || player.Body == null) return;
            transform.position = FollowTarget();
            followVelocity = Vector3.zero;
        }

        private Vector3 FollowTarget()
        {
            Vector3 bodyPosition = player.Body.transform.position;
            return new Vector3(bodyPosition.x + followOffset.x, Mathf.Max(minimumY, bodyPosition.y + followOffset.y), -10f);
        }

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
            transform.position = Vector3.SmoothDamp(transform.position, FollowTarget(), ref followVelocity, followSmoothTime);
            EffectManager effects = EffectManager.Instance;
            if (shakeRoot != null) shakeRoot.localPosition = shakeOrigin + (Vector3)effects.ShakeOffset;
            if (viewCamera != null) viewCamera.orthographicSize = baseSize - effects.ZoomPulse;
        }
    }
}
