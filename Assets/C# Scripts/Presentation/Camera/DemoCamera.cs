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
        [SerializeField] private LevelRegion fixedRegion;
        [Header("Fixed region shake")]
        [Tooltip("Shake strength multiplier while the camera is locked to a region.")]
        [SerializeField, Min(0f)] private float fixedShakeMultiplier = 1f;
        [Tooltip("Maximum shake displacement as a fraction of the viewport height.")]
        [SerializeField, Range(0f, 0.02f)] private float fixedShakeViewportLimit = 0.004f;
        private Vector3 followVelocity;
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
            if (fixedRegion != null) { Initialize(); FrameFixedRegion(); ApplyCameraEffects(); return; }
            if (player == null || player.Body == null) return;
            transform.position = FollowTarget();
            followVelocity = Vector3.zero;
        }

        public void CenterOnRegion(LevelRegion region)
        {
            fixedRegion = region;
            Initialize();
            if (fixedRegion != null)
            {
                FrameFixedRegion();
                ApplyCameraEffects();
            }
        }

        private void FrameFixedRegion()
        {
            if (viewCamera == null) return;
            Bounds bounds = fixedRegion.WorldBounds;
            Vector3 center = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z - 10f);
            if (shakeRoot != null) shakeRoot.localPosition = shakeOrigin;
            // Move the rig, preserving the authored local offsets of its children.
            transform.position += center - viewCamera.transform.position;
            viewCamera.transform.rotation = Quaternion.identity;
            viewCamera.orthographic = true;
            followVelocity = Vector3.zero;
        }

        private Vector3 FollowTarget()
        {
            Vector2 bodyPosition = player.PresentationPosition;
            return new Vector3(bodyPosition.x + followOffset.x, Mathf.Max(minimumY, bodyPosition.y + followOffset.y), -10f);
        }

        private void Initialize()
        {
            if (initialized || viewCamera == null) return;
            if (Application.isPlaying && viewCamera.GetComponent<RewindScreenEffect>() == null)
                viewCamera.gameObject.AddComponent<RewindScreenEffect>();
            shakeOrigin = shakeRoot != null ? shakeRoot.localPosition : Vector3.zero;
            initialized = true;
        }

        private void LateUpdate()
        {
            Initialize();
            if (viewCamera == null) return;
            if (fixedRegion != null) FrameFixedRegion();
            else
            {
                if (player == null || player.Body == null) return;
                if (RewindManager.Rewinding)
                {
                    transform.position = FollowTarget();
                    followVelocity = Vector3.zero;
                }
                else
                {
                    float smooth = player.Body.IsReturning ? Mathf.Min(0.06f, followSmoothTime) : followSmoothTime;
                    transform.position = Vector3.SmoothDamp(transform.position, FollowTarget(), ref followVelocity, smooth);
                }
            }
            ApplyCameraEffects();
        }

        private void ApplyCameraEffects()
        {
            if (viewCamera == null) return;
            if (!Application.isPlaying) return;
            EffectManager effects = EffectManager.Instance;
            Vector2 shake = shakeRoot != null ? effects.ShakeOffset : Vector2.zero;
            if (fixedRegion != null)
            {
                Vector3 scale = shakeRoot != null && shakeRoot.parent != null
                    ? shakeRoot.parent.lossyScale : Vector3.one;
                float worldScale = Mathf.Max(0.0001f, Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                float shakeLimit = shakeRoot != null
                    ? viewCamera.orthographicSize * 2f * fixedShakeViewportLimit / worldScale : 0f;
                shake = Vector2.ClampMagnitude(shake * fixedShakeMultiplier, shakeLimit);
            }
            if (shakeRoot != null) shakeRoot.localPosition = shakeOrigin + (Vector3)shake;
        }
    }
}
