using UnityEngine;

namespace TapTap
{
    /// <summary>A self-contained 3D character for the ending; never drives the gameplay player.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(CharacterController))]
    public sealed class EndingPlayerController : MonoBehaviour
    {
        [Header("结局移动")]
        public Camera ViewCamera;
        [Min(0.01f)] public float MoveSpeed = 1f;
        [Min(0.01f)] public float IntroSpeed = 0.8f;
        [Min(0.1f)] public float Gravity = 18f;
        [Tooltip("交还操作时固定的左右行走线；留空则采用当时位置。")]
        public Transform WalkLine;
        [Tooltip("相对行走线原点的左右边界，单位为米。")]
        public Vector2 WalkLimits = new Vector2(-3f, 7f);
        public bool UseKeyboard = true;

        [Header("向下检测场景碰撞并贴地")]
        public LayerMask GroundLayers = 1;
        [Min(0.1f)] public float GroundProbeHeight = 1.5f;
        [Min(0.1f)] public float GroundProbeDepth = 4f;
        [Min(0.01f), Tooltip("单步允许贴合的下坡/台阶高度；前方没有可行走地面时停止前进。")]
        public float GroundSnapDistance = 0.5f;

        [Header("模型与慢速动画")]
        public Transform[] FacingPivots = new Transform[0];
        public Animator[] Animators = new Animator[0];
        [Range(0.1f, 1f)] public float WalkAnimationSpeed = 0.6f;
        [Min(0.01f), Tooltip("越小转向越柔和，按秒计算，不受帧率影响。")]
        public float TurnSpeed = 4.5f;
        [Min(0f), Tooltip("沿曲线提前观察的距离，使转弯更自然。")]
        public float TurnLookAhead = 0.2f;
        [Tooltip("模型正面相对移动方向的偏移；当前模型正面为 +Z，使用 0 度。")]
        public float ModelFrontYaw = 0f;

        private static readonly int Moving = Animator.StringToHash("Moving");
        private static readonly int Idle = Animator.StringToHash("Base Layer.Idle");
        private static readonly int Walk = Animator.StringToHash("Base Layer.Walk");
        private CharacterController motor;
        private Vector3 lineOrigin, lineRight = Vector3.right, desiredVelocity, facingDirection;
        private float verticalSpeed, externalHorizontal;
        private bool walking, groundContact;
        private readonly RaycastHit[] groundHits = new RaycastHit[64];
        private EndingBezierPath curve;
        private float curveDistance;
        public bool HasControl { get; private set; }
        public bool IsWalking => walking;
        public bool IsGrounded => groundContact || motor != null && motor.isGrounded;
        public bool IsFollowingPath => curve != null;
        public float PathDistance => curveDistance;
        public Vector3 HorizontalDirection => lineRight;
        public Vector3 FeetPosition => transform.position;

        private void Awake()
        {
            motor = GetComponent<CharacterController>();
            foreach (Animator animator in Animators)
                if (animator != null) animator.applyRootMotion = false;
            CaptureWalkLine();
            // Preserve the authored starting pose instead of turning toward the camera during the hold.
            facingDirection = transform.forward;
            foreach (Transform pivot in FacingPivots)
                if (pivot != null)
                {
                    facingDirection = pivot.rotation * Quaternion.Euler(0f, -ModelFrontYaw, 0f) * Vector3.forward;
                    break;
                }
            facingDirection = Vector3.ProjectOnPlane(facingDirection, Vector3.up).normalized;
            SetWalking(false, true);
        }

        public void CaptureWalkLine()
        {
            lineOrigin = WalkLine != null ? WalkLine.position : transform.position;
            Vector3 right = ViewCamera != null ? ViewCamera.transform.right : Vector3.right;
            lineRight = Vector3.ProjectOnPlane(right, Vector3.up).normalized;
            if (lineRight.sqrMagnitude < 0.5f) lineRight = Vector3.right;
        }

        public void SetControl(bool enabled)
        {
            HasControl = enabled;
            StopMotion();
        }

        // Allows a later touch/gamepad input adapter without depending on PlayerInput.
        public void SetHorizontalInput(float value) => externalHorizontal = Mathf.Clamp(value, -1f, 1f);

        public void SetScriptedMotion(Vector3 direction, float speed)
        {
            if (HasControl) return;
            curve = null;
            direction.y = 0f;
            desiredVelocity = Vector3.ClampMagnitude(direction, 1f) * Mathf.Max(0f, speed);
        }

        public void StopMotion()
        {
            curve = null;
            desiredVelocity = Vector3.zero;
            externalHorizontal = 0f;
            SetWalking(false);
        }

        public void FollowBezier(EndingBezierPath path)
        {
            SetControl(false);
            curveDistance = 0f;
            curve = path != null && path.Length > 0.0001f ? path : null;
        }

        public void PlaceAt(Vector3 position)
        {
            if (motor == null) motor = GetComponent<CharacterController>();
            bool wasEnabled = motor.enabled;
            motor.enabled = false;
            transform.position = position;
            motor.enabled = wasEnabled;
            verticalSpeed = 0f;
            groundContact = false;
            StopMotion();
        }

        public void SnapToGround(LayerMask groundLayers)
        {
            GroundLayers = groundLayers;
            if (motor == null) motor = GetComponent<CharacterController>();
            if (!TryGetGroundPosition(transform.position, transform.position.y, groundLayers, out Vector3 ground, true)) return;
            PlaceAt(ground);
            groundContact = true;
        }

        // Both movement and the unselected Gizmos use the same downward collision query.
        // The supplied point's Y is deliberately ignored; referenceY is the previous grounded foot height.
        public bool TryGetGroundPosition(Vector3 point, float referenceY, LayerMask layers,
            out Vector3 grounded, bool allowLargeDrop = false)
        {
            if (motor == null) motor = GetComponent<CharacterController>();
            grounded = new Vector3(point.x, referenceY, point.z);
            if (motor == null) return false;
            Vector3 origin = new Vector3(point.x, referenceY + GroundProbeHeight, point.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits,
                GroundProbeHeight + GroundProbeDepth, layers, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = groundHits;
            if (count == groundHits.Length)
            {
                hits = Physics.RaycastAll(origin, Vector3.down, GroundProbeHeight + GroundProbeDepth,
                    layers, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }
            float distance = float.PositiveInfinity;
            float rise = motor.stepOffset + motor.skinWidth;
            float drop = allowLargeDrop ? GroundProbeDepth : GroundSnapDistance;
            float minimumNormalY = Mathf.Cos(motor.slopeLimit * Mathf.Deg2Rad);
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = hits[index];
                // Exclude our capsule, roofs overhead, triggers, walls and slopes the motor cannot climb.
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform) || hit.distance >= distance
                    || hit.point.y > referenceY + rise || hit.point.y < referenceY - drop
                    || hit.normal.y < minimumNormalY) continue;
                distance = hit.distance;
                grounded = new Vector3(point.x, hit.point.y + motor.skinWidth, point.z);
            }
            return !float.IsPositiveInfinity(distance);
        }

        private bool Paused => Time.timeScale <= 0f || PauseMenu.BlocksInput;

        private void Update()
        {
            if (motor == null || !motor.enabled || Paused) { SetWalking(false); return; }
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            EndingBezierPath activeCurve = curve;
            float nextDistance = curveDistance;
            Vector3 horizontalDelta = desiredVelocity * dt;
            Vector3 curveDirection = Vector3.zero;
            if (activeCurve != null)
            {
                nextDistance = Mathf.Min(activeCurve.Length, curveDistance + IntroSpeed * dt);
                activeCurve.Evaluate(nextDistance, out Vector3 next, out _);
                horizontalDelta = new Vector3(next.x - transform.position.x, 0f, next.z - transform.position.z);
                horizontalDelta = Vector3.ClampMagnitude(horizontalDelta, IntroSpeed * dt);
                activeCurve.Evaluate(Mathf.Min(activeCurve.Length, nextDistance + TurnLookAhead), out _, out curveDirection);
            }
            else if (HasControl)
            {
                bool blocked = GameplayActionLocks.IsLocked(GameplayAction.Input | GameplayAction.PlayerControls, gameObject.scene);
                float horizontal = blocked ? 0f : ReadHorizontal();
                float coordinate = Vector3.Dot(transform.position - lineOrigin, lineRight);
                float next = Mathf.Clamp(coordinate + horizontal * MoveSpeed * dt,
                    Mathf.Min(WalkLimits.x, WalkLimits.y), Mathf.Max(WalkLimits.x, WalkLimits.y));
                Vector3 target = lineOrigin + lineRight * next;
                horizontalDelta = blocked ? Vector3.zero
                    : Vector3.ClampMagnitude(Vector3.ProjectOnPlane(target - transform.position, Vector3.up), MoveSpeed * dt);
            }
            Vector3 before = transform.position;
            MoveOnGround(horizontalDelta, dt);
            Vector3 moved = Vector3.ProjectOnPlane(transform.position - before, Vector3.up);
            if (activeCurve != null)
            {
                float fraction = horizontalDelta.sqrMagnitude > 0.00000001f
                    ? Mathf.Clamp01(Vector3.Dot(moved, horizontalDelta) / horizontalDelta.sqrMagnitude) : 1f;
                curveDistance = Mathf.Lerp(curveDistance, nextDistance, fraction);
                if (curveDistance >= activeCurve.Length - 0.0001f) curve = null;
            }
            bool isMoving = moved.sqrMagnitude > 0.000001f;
            if (isMoving) facingDirection = curveDirection.sqrMagnitude > 0.01f ? curveDirection : moved.normalized;
            SetWalking(isMoving);
            RenderFacing(dt);
        }

        private void MoveOnGround(Vector3 horizontalDelta, float dt)
        {
            Vector3 before = transform.position;
            if (TryGetGroundPosition(before + horizontalDelta, before.y, GroundLayers, out Vector3 nextGround))
            {
                verticalSpeed = 0f;
                motor.Move(new Vector3(horizontalDelta.x, nextGround.y - before.y, horizontalDelta.z));
                // A wall may shorten XZ movement. Resolve Y again at the actual resulting XZ position.
                groundContact = TryGetGroundPosition(transform.position, before.y, GroundLayers, out Vector3 actualGround);
                if (groundContact) motor.Move(Vector3.up * (actualGround.y - transform.position.y));
                return;
            }
            // Do not walk into a gap or through a steep face just because the curve continues there.
            groundContact = TryGetGroundPosition(before, before.y, GroundLayers, out Vector3 currentGround);
            if (groundContact)
            {
                verticalSpeed = 0f;
                motor.Move(Vector3.up * (currentGround.y - before.y));
            }
            else
            {
                verticalSpeed = Mathf.Max(verticalSpeed - Gravity * dt, -30f);
                motor.Move(Vector3.up * (verticalSpeed * dt));
            }
        }

        private float ReadHorizontal()
        {
            if (!UseKeyboard) return externalHorizontal;
            if (!Application.isFocused) return 0f;
            return (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
        }

        private void RenderFacing(float dt)
        {
            if (facingDirection.sqrMagnitude < 0.01f) return;
            Quaternion rotation = Quaternion.LookRotation(facingDirection, Vector3.up)
                * Quaternion.Euler(0f, ModelFrontYaw, 0f);
            foreach (Transform pivot in FacingPivots)
                if (pivot != null) pivot.rotation = Quaternion.Slerp(pivot.rotation, rotation, 1f - Mathf.Exp(-TurnSpeed * dt));
        }

        private void SetWalking(bool moving, bool force = false)
        {
            bool changed = force || walking != moving;
            walking = moving;
            foreach (Animator animator in Animators)
            {
                if (animator == null || animator.runtimeAnimatorController == null) continue;
                animator.speed = Paused ? 0f : moving ? WalkAnimationSpeed : 1f;
                if (!changed) continue;
                animator.SetBool(Moving, moving);
                int state = moving ? Walk : Idle;
                if (animator.HasState(0, state)) animator.CrossFadeInFixedTime(state, 0.16f, 0, 0f);
            }
        }

        private void OnDisable() { HasControl = false; StopMotion(); }

    }
}
