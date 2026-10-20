using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera)), DefaultExecutionOrder(1000)]
    public sealed class MainMenuCameraMotion : MonoBehaviour
    {
        [Header("Breathing (relative to the authored camera pose)")]
        [Min(0.1f)] public float BreathPeriod = 9f;
        public Vector2 BreathPosition = new Vector2(0.015f, 0.0225f);
        public Vector3 BreathRotation = new Vector3(0.15f, 0.2f, 0.05f);
        [Header("Mouse parallax (degrees at the screen edge)")]
        public Vector2 MouseRotation = new Vector2(0.55f, 0.9f);
        [Min(0.01f)] public float MouseSmoothTime = 0.35f;

        private Vector3 homePosition;
        private Quaternion homeRotation;
        private Vector2 pointer, pointerVelocity;
        private float startedAt;

        // Cinematics move this base pose; breathing is added in LateUpdate without accumulating drift.
        public Vector3 BaseWorldPosition => transform.parent != null
            ? transform.parent.TransformPoint(homePosition) : homePosition;
        public Quaternion BaseWorldRotation => transform.parent != null
            ? transform.parent.rotation * homeRotation : homeRotation;

        public void SetBaseWorldPosition(Vector3 position)
        {
            homePosition = transform.parent != null ? transform.parent.InverseTransformPoint(position) : position;
            transform.position = position;
        }

        private void OnEnable()
        {
            homePosition = transform.localPosition;
            homeRotation = transform.localRotation;
            pointer = pointerVelocity = Vector2.zero;
            startedAt = Time.unscaledTime;
        }

        private void LateUpdate()
        {
            Vector2 target = Vector2.zero;
            Rect viewport = GetComponent<Camera>().pixelRect;
            Vector2 mouse = Input.mousePosition;
            if (Application.isFocused && viewport.width > 0f && viewport.height > 0f && viewport.Contains(mouse))
                target = new Vector2((mouse.x - viewport.xMin) / viewport.width * 2f - 1f,
                    (mouse.y - viewport.yMin) / viewport.height * 2f - 1f);
            ApplyPose(Time.unscaledTime - startedAt, target, Time.unscaledDeltaTime);
        }

        private void ApplyPose(float elapsed, Vector2 target, float deltaTime)
        {
            pointer = Vector2.SmoothDamp(pointer, Vector2.ClampMagnitude(target, 1.414214f),
                ref pointerVelocity, MouseSmoothTime, Mathf.Infinity, deltaTime);
            float phase = elapsed * Mathf.PI * 2f / Mathf.Max(0.1f, BreathPeriod);
            float horizontal = Mathf.Sin(phase);
            float vertical = Mathf.Sin(phase * 1.19f);
            transform.localPosition = homePosition + homeRotation * new Vector3(
                horizontal * BreathPosition.x, vertical * BreathPosition.y, 0f);
            transform.localRotation = homeRotation * Quaternion.Euler(
                vertical * BreathRotation.x - pointer.y * MouseRotation.x,
                horizontal * BreathRotation.y + pointer.x * MouseRotation.y,
                Mathf.Sin(phase * 0.73f) * BreathRotation.z);
        }

        private void OnDisable()
        {
            transform.localPosition = homePosition;
            transform.localRotation = homeRotation;
        }
    }
}
