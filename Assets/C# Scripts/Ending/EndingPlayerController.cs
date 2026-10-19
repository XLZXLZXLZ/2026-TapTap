using UnityEngine;

namespace TapTap
{
    /// <summary>A self-contained 3D character for the ending; never drives the gameplay player.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(CharacterController))]
    public sealed class EndingPlayerController : MonoBehaviour
    {
        [Header("结局移动")]
        public Camera ViewCamera;
        [Min(0.01f)] public float MoveSpeed = 0.85f;
        [Min(0.01f)] public float IntroSpeed = 0.7f;
        [Min(0.1f)] public float Gravity = 18f;
        [Tooltip("交还操作时固定的左右行走线；留空则采用当时位置。")]
        public Transform WalkLine;
        [Tooltip("相对行走线原点的左右边界，单位为米。")]
        public Vector2 WalkLimits = new Vector2(-3f, 7f);
        public bool UseKeyboard = true;

        [Header("模型与慢速动画")]
        public Transform[] FacingPivots = new Transform[0];
        public Animator[] Animators = new Animator[0];
        [Range(0.1f, 1f)] public float WalkAnimationSpeed = 0.5f;
        [Min(0.01f)] public float TurnSpeed = 6f;
        [Tooltip("沿用机器人模型的正面角度。")]
        public float ModelFrontYaw = 180f;

        private static readonly int Moving = Animator.StringToHash("Moving");
        private static readonly int Idle = Animator.StringToHash("Base Layer.Idle");
        private static readonly int Walk = Animator.StringToHash("Base Layer.Walk");
        private CharacterController motor;
        private Vector3 lineOrigin, lineRight = Vector3.right, desiredVelocity, facingDirection;
        private float verticalSpeed, externalHorizontal;
        private bool walking;
        public bool HasControl { get; private set; }
        public bool IsWalking => walking;
        public bool IsGrounded => motor != null && motor.isGrounded;
        public Vector3 HorizontalDirection => lineRight;
        public Vector3 FeetPosition => transform.position;

        private void Awake()
        {
            motor = GetComponent<CharacterController>();
            foreach (Animator animator in Animators)
                if (animator != null) animator.applyRootMotion = false;
            CaptureWalkLine();
            facingDirection = ViewCamera != null
                ? Vector3.ProjectOnPlane(-ViewCamera.transform.forward, Vector3.up).normalized
                : -transform.forward;
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
            desiredVelocity = Vector3.zero;
            externalHorizontal = 0f;
            SetWalking(false);
        }

        // Allows a later touch/gamepad input adapter without depending on PlayerInput.
        public void SetHorizontalInput(float value) => externalHorizontal = Mathf.Clamp(value, -1f, 1f);

        public void SetScriptedMotion(Vector3 direction, float speed)
        {
            if (HasControl) return;
            direction.y = 0f;
            desiredVelocity = Vector3.ClampMagnitude(direction, 1f) * Mathf.Max(0f, speed);
        }

        public void StopMotion()
        {
            desiredVelocity = Vector3.zero;
            externalHorizontal = 0f;
            SetWalking(false);
        }

        public void SnapToGround(LayerMask groundLayers, float searchHeight = 3f)
        {
            if (motor == null) motor = GetComponent<CharacterController>();
            Vector3 origin = transform.position + Vector3.up * searchHeight;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, searchHeight * 2f,
                groundLayers, QueryTriggerInteraction.Ignore);
            float distance = float.PositiveInfinity;
            Vector3 ground = transform.position;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= distance) continue;
                distance = hit.distance;
                ground = hit.point;
            }
            if (float.IsPositiveInfinity(distance)) return;
            bool wasEnabled = motor.enabled;
            motor.enabled = false;
            transform.position = ground + Vector3.up * motor.skinWidth;
            motor.enabled = wasEnabled;
            verticalSpeed = 0f;
        }

        private bool Paused => Time.timeScale <= 0f || PauseMenu.BlocksInput;

        private void Update()
        {
            if (motor == null || !motor.enabled || Paused) { SetWalking(false); return; }
            float dt = Time.deltaTime;
            Vector3 velocity = desiredVelocity;
            if (HasControl)
            {
                bool blocked = GameplayActionLocks.IsLocked(GameplayAction.Input | GameplayAction.PlayerControls, gameObject.scene);
                float horizontal = blocked ? 0f : ReadHorizontal();
                float coordinate = Vector3.Dot(transform.position - lineOrigin, lineRight);
                float next = Mathf.Clamp(coordinate + horizontal * MoveSpeed * dt,
                    Mathf.Min(WalkLimits.x, WalkLimits.y), Mathf.Max(WalkLimits.x, WalkLimits.y));
                Vector3 target = lineOrigin + lineRight * next;
                velocity = Vector3.ProjectOnPlane(target - transform.position, Vector3.up) / Mathf.Max(dt, 0.0001f);
            }
            if (motor.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
            verticalSpeed = Mathf.Max(verticalSpeed - Gravity * dt, -30f);
            Vector3 before = transform.position;
            motor.Move((velocity + Vector3.up * verticalSpeed) * dt);
            Vector3 moved = Vector3.ProjectOnPlane(transform.position - before, Vector3.up);
            bool isMoving = moved.sqrMagnitude > 0.000001f;
            if (isMoving) facingDirection = moved.normalized;
            SetWalking(isMoving);
            RenderFacing(dt);
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
