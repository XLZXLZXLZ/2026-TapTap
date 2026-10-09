using UnityEngine;
using UnityEngine.Serialization;

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
        [Header("Fixed region framing")]
        [Tooltip("Crop this fraction of the region width from each side, within the outer one-grid wall.")]
        [FormerlySerializedAs("fixedRegionPadding")]
        [SerializeField, Range(0f, 0.25f)] private float fixedRegionInset = 0.015f;
        [Tooltip("Shake strength multiplier while the camera is locked to a region.")]
        [SerializeField, Min(0f)] private float fixedShakeMultiplier = 1f;
        [Tooltip("Maximum shake displacement as a fraction of the viewport height.")]
        [SerializeField, Range(0f, 0.02f)] private float fixedShakeViewportLimit = 0.004f;
        [Tooltip("Maximum zoom-in as a fraction of the resting camera size.")]
        [SerializeField, Range(0f, 0.1f)] private float fixedZoomLimit = 0.015f;
        private Vector3 followVelocity;
        private float baseSize;
        private float followBaseSize;
        private float fixedFittedSize;
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

        public void FrameRegionHorizontally(LevelRegion region, float referenceAspect = 0f)
        {
            fixedRegion = region;
            Initialize();
            if (fixedRegion != null)
            {
                FrameFixedRegion(referenceAspect);
                ApplyCameraEffects();
            }
        }

        private void FrameFixedRegion(float referenceAspect = 0f)
        {
            if (viewCamera == null) return;
            Bounds bounds = fixedRegion.WorldBounds;
            Vector3 center = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z - 10f);
            if (shakeRoot != null) shakeRoot.localPosition = shakeOrigin;
            // Move the rig, preserving the authored local offsets of its children.
            transform.position += center - viewCamera.transform.position;
            viewCamera.transform.rotation = Quaternion.identity;
            viewCamera.orthographic = true;
            // Runtime framing must use the actual viewport on the first frame as well.
            float aspect = !Application.isPlaying && referenceAspect > 0f ? referenceAspect : viewCamera.aspect;
            aspect = Mathf.Max(0.01f, aspect);
            // The outer one-grid wall may be cropped; protect the playable interior instead.
            float wallWidth = Mathf.Abs(fixedRegion.transform.lossyScale.x) * fixedRegion.UnitSize;
            fixedFittedSize = Mathf.Max(0.01f, (bounds.extents.x - wallWidth) / aspect);
            baseSize = Mathf.Max(fixedFittedSize, bounds.extents.x / aspect * (1f - 2f * fixedRegionInset));
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
            baseSize = viewCamera.orthographicSize;
            followBaseSize = baseSize;
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
                baseSize = followBaseSize;
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
            if (!Application.isPlaying)
            {
                viewCamera.orthographicSize = baseSize;
                return;
            }
            EffectManager effects = EffectManager.Instance;
            Vector2 shake = shakeRoot != null ? effects.ShakeOffset : Vector2.zero;
            float zoom = effects.ZoomPulse;
            if (fixedRegion != null)
            {
                float aspect = Mathf.Max(0.01f, viewCamera.aspect);
                float margin = Mathf.Max(0f, baseSize - fixedFittedSize);
                Vector3 scale = shakeRoot != null && shakeRoot.parent != null
                    ? shakeRoot.parent.lossyScale : Vector3.one;
                float worldScale = Mathf.Max(0.0001f, Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                float shakeLimit = shakeRoot != null
                    ? Mathf.Min(baseSize * 2f * fixedShakeViewportLimit, margin * aspect / worldScale) : 0f;
                shake = Vector2.ClampMagnitude(shake * fixedShakeMultiplier, shakeLimit);
                // Reserve a constant shake budget instead of resizing with each oscillation.
                // Scale the whole zoom curve; clipping its instantaneous value creates a plateau.
                float zoomBudget = Mathf.Max(0f, margin - shakeLimit * worldScale / aspect);
                float amplitude = Mathf.Min(effects.ZoomAmount, baseSize * fixedZoomLimit, zoomBudget);
                zoom = effects.ZoomProgress * amplitude;
            }
            float size = Mathf.Max(0.01f, baseSize - zoom);
            if (shakeRoot != null) shakeRoot.localPosition = shakeOrigin + (Vector3)shake;
            viewCamera.orthographicSize = size;
        }
    }
}
