using UnityEngine;

namespace ArtStyleTest
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class FreeFlyCamera : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] float moveSpeed = 5f;
        [SerializeField, Min(1f)] float fastMultiplier = 3f;
        [SerializeField, Min(0.1f)] float mouseSensitivity = 2f;
        [SerializeField] bool showControls = true;

        Vector3 initialPosition;
        Quaternion initialRotation;
        float yaw, pitch;
        bool looking;
        CursorLockMode previousCursorLock;
        bool previousCursorVisible;

        void Awake()
        {
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            SynchronizeAngles();
        }

        void Update()
        {
            if (!Application.isFocused) return;
            if (Input.GetMouseButtonDown(1)) BeginLook();
            if (Input.GetMouseButtonUp(1) || Input.GetKeyDown(KeyCode.Escape)) EndLook();
            if (Input.GetKeyDown(KeyCode.R)) { ResetView(); return; }

            Vector3 move = new Vector3(
                (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f),
                (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
            Vector2 mouse = looking
                ? new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"))
                : Vector2.zero;
            Step(move, mouse, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
                Time.unscaledDeltaTime);
        }

        void Step(Vector3 move, Vector2 mouse, bool fast, float deltaTime)
        {
            if (mouse != Vector2.zero)
            {
                yaw = Mathf.Repeat(yaw + mouse.x * mouseSensitivity, 360f);
                pitch = Mathf.Clamp(pitch - mouse.y * mouseSensitivity, -85f, 85f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }
            Vector3 direction = transform.right * move.x + Vector3.up * move.y + transform.forward * move.z;
            transform.position += Vector3.ClampMagnitude(direction, 1f) *
                moveSpeed * (fast ? fastMultiplier : 1f) * Mathf.Max(0f, deltaTime);
        }

        public void ResetView()
        {
            transform.SetPositionAndRotation(initialPosition, initialRotation);
            SynchronizeAngles();
        }

        void SynchronizeAngles()
        {
            yaw = transform.eulerAngles.y;
            pitch = Mathf.DeltaAngle(0f, transform.eulerAngles.x);
        }

        void BeginLook()
        {
            if (looking) return;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            looking = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void EndLook()
        {
            if (!looking) return;
            looking = false;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        void OnApplicationFocus(bool focused) { if (!focused) EndLook(); }
        void OnDisable() { EndLook(); }

        void OnGUI()
        {
            if (!showControls) return;
            GUI.Box(new Rect(12, 12, 600, 76),
                "FREE CAMERA | Click Game view to focus\n" +
                "WASD: move | Q / E: down / up | Shift: faster\n" +
                "Hold right mouse: look | Esc: release mouse | R: reset view");
        }
    }
}
