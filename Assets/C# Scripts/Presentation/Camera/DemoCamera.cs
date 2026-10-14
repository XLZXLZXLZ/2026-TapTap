using UnityEngine;

namespace TapTap
{
    public sealed class DemoCamera : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Transform shakeRoot;
        [Tooltip("未绑定关卡时使用。绑定关卡后读取关卡的 Camera Settings，默认按地图尺寸自动计算 Size。")]
        [SerializeField] private LevelCameraSettings defaultSettings = new LevelCameraSettings();
        [Tooltip("未绑定关卡时，镜头中心的最低世界坐标。")]
        [SerializeField] private float minimumY = 3.5f;
        [SerializeField, Min(0.01f)] private float followSmoothTime = 0.2f;
        [SerializeField, Min(0.01f)] private float verticalSmoothTime = 0.3f;
        // Preserve the serialized name used by existing scene prefab overrides.
        [SerializeField, InspectorName("Follow Region")] private LevelRegion fixedRegion;
        private LevelRegion region => fixedRegion;
        [Header("Region shake")]
        [Tooltip("Shake strength multiplier while following a level region.")]
        [SerializeField, Min(0f)] private float fixedShakeMultiplier = 1f;
        [Tooltip("Maximum shake displacement as a fraction of the viewport height.")]
        [SerializeField, Range(0f, 0.02f)] private float fixedShakeViewportLimit = 0.004f;
        private Vector2 followVelocity;
        private Vector3 shakeOrigin;
        private bool initialized;
        private bool changingRegion;
        private float sizeVelocity;
        private float baseViewSize;
        private float progressZoom = 1f;

        public void SetProgressZoom(float scale) => progressZoom = Mathf.Clamp(scale, 0.1f, 1f);

        public LevelRegion Region => region;
        private LevelCameraSettings Settings => region != null ? region.CameraSettings
            : defaultSettings ?? (defaultSettings = new LevelCameraSettings());
        private float UnitScale => region != null
            ? Mathf.Max(0.0001f, region.UnitSize * Mathf.Abs(region.transform.lossyScale.y)) : 1f;
        private const float MaximumAutoSize = 12f;
        private bool AutoSize => region != null && Settings.AutoSize;
        private float MapHalfSize => Mathf.Max(region.WorldBounds.size.x, region.WorldBounds.size.y) * 0.5f;
        private bool FollowEnabled => !AutoSize || MapHalfSize > MaximumAutoSize * UnitScale;
        private bool FollowHorizontal => FollowEnabled && Settings.FollowHorizontal;
        private bool FollowVertical => FollowEnabled && Settings.FollowVertical;

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
            followVelocity = Vector2.zero;
            Initialize();
        }

        public void SetRegion(LevelRegion level, bool snap = true)
        {
            bool changed = fixedRegion != level;
            fixedRegion = level;
            if (snap) SnapToPlayer();
            else if (changed) { changingRegion = true; followVelocity = Vector2.zero; sizeVelocity = 0f; }
        }

        // Existing authoring tools can keep their region-binding entry point.
        public void CenterOnRegion(LevelRegion level) => SetRegion(level);

        public void SnapToPlayer()
        {
            Initialize();
            changingRegion = false;
            sizeVelocity = 0f;
            if (viewCamera == null) return;
            ResetShake();
            UpdateView();
            Vector3 center = viewCamera.transform.position;
            if (player != null && player.Body != null) center = FollowTarget(center);
            else if (region != null) center = region.WorldBounds.center + Vector3.back * 10f;
            MoveCamera(ConstrainToRegion(center));
            followVelocity = Vector2.zero;
            ApplyCameraEffects();
        }

        private Vector3 FollowTarget(Vector3 current)
        {
            LevelCameraSettings settings = Settings;
            Vector2 position = player.PresentationPosition + settings.Offset * UnitScale;
            if (region == null) position.y = Mathf.Max(minimumY, position.y);
            Vector3 center = region != null ? region.WorldBounds.center : current;
            return new Vector3(FollowHorizontal ? position.x : center.x,
                FollowVertical ? position.y : center.y, region != null ? center.z - 10f : -10f);
        }

        private Vector3 ConstrainToRegion(Vector3 center)
        {
            if (region == null) return center;
            Bounds bounds = region.WorldBounds;
            float halfHeight = viewCamera.orthographicSize;
            center.x = ConstrainAxis(center.x, bounds.min.x, bounds.max.x, halfHeight * viewCamera.aspect);
            center.y = ConstrainAxis(center.y, bounds.min.y, bounds.max.y, halfHeight);
            center.z = bounds.center.z - 10f;
            return center;
        }

        private static float ConstrainAxis(float position, float min, float max, float halfView)
        {
            // A smaller room is centered on this axis; its surroundings remain visible.
            return max - min <= halfView * 2f ? (min + max) * 0.5f
                : Mathf.Clamp(position, min + halfView, max - halfView);
        }

        private void MoveCamera(Vector3 center)
        {
            // Move the rig, preserving authored local offsets in the camera/shake hierarchy.
            transform.position += center - viewCamera.transform.position;
        }

        private void ResetShake()
        {
            if (shakeRoot != null) shakeRoot.localPosition = shakeOrigin;
        }

        private void UpdateView(float deltaTime = 0f)
        {
            viewCamera.orthographic = true;
            viewCamera.transform.rotation = Quaternion.identity;
            float size = AutoSize ? Mathf.Min(MapHalfSize, MaximumAutoSize * UnitScale)
                : Settings.ViewSize * UnitScale;
            if (changingRegion && deltaTime <= 0f) return;
            size = Mathf.Max(0.1f, size);
            if (baseViewSize <= 0f) baseViewSize = viewCamera.orthographicSize;
            baseViewSize = changingRegion && deltaTime > 0f
                ? Mathf.SmoothDamp(baseViewSize, size, ref sizeVelocity, followSmoothTime,
                    Mathf.Infinity, deltaTime) : size;
            viewCamera.orthographicSize = baseViewSize * progressZoom;
        }

        private void Initialize()
        {
            if (initialized || viewCamera == null) return;
            if (Application.isPlaying && viewCamera.GetComponent<RewindScreenEffect>() == null)
                viewCamera.gameObject.AddComponent<RewindScreenEffect>();
            shakeOrigin = shakeRoot != null ? shakeRoot.localPosition : Vector3.zero;
            initialized = true;
        }

        private void LateUpdate() => UpdateFollow(Time.deltaTime, RewindManager.Rewinding);

        private void UpdateFollow(float deltaTime, bool rewinding)
        {
            Initialize();
            if (viewCamera == null || player == null || player.Body == null) return;
            ResetShake();
            if (rewinding) changingRegion = false;
            UpdateView(rewinding ? 0f : deltaTime);
            Vector3 current = viewCamera.transform.position;
            Vector3 target = FollowTarget(current);
            if ((!FollowEnabled && !changingRegion) || rewinding)
            {
                MoveCamera(ConstrainToRegion(target));
                followVelocity = Vector2.zero;
                changingRegion = false;
                sizeVelocity = 0f;
            }
            else if (deltaTime <= 0f)
            {
                // Paused annotations use timeScale = 0; SmoothDamp's velocity divides by deltaTime.
                if (!changingRegion) MoveCamera(ConstrainToRegion(current));
                followVelocity = Vector2.zero;
            }
            else
            {
                LevelCameraSettings settings = Settings;
                Vector2 viewport = new Vector2(viewCamera.orthographicSize * 2f * viewCamera.aspect,
                    viewCamera.orthographicSize * 2f);
                if (FollowHorizontal && !changingRegion)
                    target.x = Mathf.Clamp(current.x, target.x - viewport.x * Mathf.Clamp(settings.DeadZone.x, 0f, 0.45f),
                        target.x + viewport.x * Mathf.Clamp(settings.DeadZone.x, 0f, 0.45f));
                if (FollowVertical && !changingRegion)
                    target.y = Mathf.Clamp(current.y, target.y - viewport.y * Mathf.Clamp(settings.DeadZone.y, 0f, 0.45f),
                        target.y + viewport.y * Mathf.Clamp(settings.DeadZone.y, 0f, 0.45f));
                target = ConstrainToRegion(target);
                float horizontalSmooth = Mathf.Max(0.01f, followSmoothTime);
                float verticalSmooth = Mathf.Max(0.01f, verticalSmoothTime);
                if (player.Body.IsReturning)
                {
                    horizontalSmooth = Mathf.Min(0.06f, horizontalSmooth);
                    verticalSmooth = Mathf.Min(0.06f, verticalSmooth);
                }
                Vector3 next = new Vector3(
                    Mathf.SmoothDamp(current.x, target.x, ref followVelocity.x, horizontalSmooth, Mathf.Infinity, deltaTime),
                    Mathf.SmoothDamp(current.y, target.y, ref followVelocity.y, verticalSmooth, Mathf.Infinity, deltaTime),
                    target.z);
                // A new room's clamp must not teleport the camera across the shared boundary.
                Vector3 constrained = changingRegion ? next : ConstrainToRegion(next);
                if (!Mathf.Approximately(next.x, constrained.x)) followVelocity.x = 0f;
                if (!Mathf.Approximately(next.y, constrained.y)) followVelocity.y = 0f;
                MoveCamera(constrained);
                if (changingRegion && Vector2.Distance(next, target) < UnitScale * 0.01f
                    && Mathf.Abs(sizeVelocity) < UnitScale * 0.01f) changingRegion = false;
            }
            ApplyCameraEffects();
        }

        private void ApplyCameraEffects()
        {
            if (viewCamera == null || !Application.isPlaying) return;
            EffectManager effects = EffectManager.Instance;
            Vector2 shake = shakeRoot != null ? effects.ShakeOffset : Vector2.zero;
            if (region != null)
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
