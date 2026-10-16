using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap
{
    // Optional, removable debug adapter. Gameplay scripts never depend on this component.
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class DebugWorldCamera : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float moveSpeed = 12f;
        [SerializeField, Min(1f)] private float fastMultiplier = 3f;

        private static DebugWorldCamera current;
        private readonly Dictionary<RegionRevealView, bool> reveals = new Dictionary<RegionRevealView, bool>();
        private readonly Dictionary<Transform, Pose> poses = new Dictionary<Transform, Pose>();
        private readonly List<WorldMapModule> previewModules = new List<WorldMapModule>();
        private int previewIndex = -1;
        private bool hasPreviewLens;
        private LevelCameraShot previewLens;
        private PlayerInput controls;
        private DemoCamera rig;
        private Camera view;
        private RewindManager rewind;
        private WorldMapRoot world;
        private bool active, restoreKeyboard, restoreRig;
        private Vector3 position;
        private Quaternion rotation;
        private bool occlusion;
        private float fieldOfView, orthographicSize, nearClip, farClip;
        private bool orthographic;
        private bool physical;
        private float focalLength;
        private Vector2 sensorSize, lensShift;
        private Camera.GateFitMode gateFit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => current = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (current == null) new GameObject("Debug World Camera [U]").AddComponent<DebugWorldCamera>();
#endif
        }

        private void Awake()
        {
            if (current != null && current != this) { Destroy(gameObject); return; }
            current = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += SceneLoaded;
        }

        private static bool Blocked => WorldMapProgression.InputBlocked || LevelAnnotation.IsFeedbackOpen
            || GameplayActionLocks.IsLocked(GameplayAction.DebugCamera, SceneManager.GetActiveScene())
            || RewindManager.Rewinding
            || (SceneResetManager.Existing != null && SceneResetManager.Existing.IsResetting);

        private void Update()
        {
            if (active && (Blocked || controls == null || !controls.gameObject.activeInHierarchy
                || view == null || !view.isActiveAndEnabled || rig == null || world == null)) Exit();
            if (!Application.isFocused) return;
            if (Input.GetKeyDown(KeyCode.U))
            {
                if (active) Exit();
                else if (!Blocked) Enter();
            }
            if (!active) return;
            if (Input.GetKeyDown(KeyCode.UpArrow)) SwitchPreset(-1);
            if (Input.GetKeyDown(KeyCode.DownArrow)) SwitchPreset(1);
            float x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float z = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
            float y = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            Vector3 direction = Vector3.ClampMagnitude(rotation * new Vector3(x, 0f, z) + Vector3.up * y, 1f);
            float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                ? fastMultiplier : 1f);
            position += direction * (speed * Time.unscaledDeltaTime);
        }

        private void Enter()
        {
            var player = FindObjectOfType<PlayerController>();
            rig = FindObjectOfType<DemoCamera>();
            world = FindObjectOfType<WorldMapRoot>();
            controls = player != null ? player.GetComponent<PlayerInput>() : null;
            view = rig != null ? rig.ViewCamera : null;
            if (controls == null || !controls.isActiveAndEnabled || view == null
                || !view.isActiveAndEnabled || world == null || !rig.TryAcquireControl(this)) return;

            active = true;
            // Keep simulation and rewind registration intact; only stop input sampling.
            controls.RestoreHeldState(controls.SpaceHeld);
            controls.enabled = false;
            rewind = RewindManager.Current;
            if (rewind != null) { restoreKeyboard = rewind.UseKeyboard; rewind.UseKeyboard = false; }
            position = view.transform.position;
            rotation = view.transform.rotation;
            for (Transform node = view.transform; node != null; node = node.parent)
            {
                poses[node] = new Pose(node.localPosition, node.localRotation);
                if (node == rig.transform) break;
            }
            restoreRig = rig.enabled;
            rig.enabled = false;
            occlusion = view.useOcclusionCulling;
            fieldOfView = view.fieldOfView;
            orthographicSize = view.orthographicSize;
            orthographic = view.orthographic;
            nearClip = view.nearClipPlane;
            farClip = view.farClipPlane;
            physical = view.usePhysicalProperties;
            focalLength = view.focalLength;
            sensorSize = view.sensorSize;
            lensShift = view.lensShift;
            gateFit = view.gateFit;
            hasPreviewLens = false;
            previewModules.Clear();
            if (world.Source != null) previewModules.AddRange(world.Source.OrderedModules());
            previewIndex = previewModules.FindIndex(module => world.FindRegion(module) == rig.Region);
            view.useOcclusionCulling = false;
            RevealWorld();
        }

        private void SwitchPreset(int direction)
        {
            if (world == null || view == null || previewModules.Count == 0) return;
            int index = previewIndex < 0 ? (direction > 0 ? 0 : previewModules.Count - 1)
                : previewIndex + direction;
            for (; index >= 0 && index < previewModules.Count; index += direction)
            {
                LevelRegion region = world.FindRegion(previewModules[index]);
                if (region == null || !region.gameObject.activeInHierarchy) continue;
                // Evaluate only; do not call SetRegion/GoToSequence or change player progression.
                previewLens = LevelCameraShot.Evaluate(region, null, view.aspect);
                position = previewLens.Position;
                rotation = previewLens.Rotation;
                previewIndex = index;
                hasPreviewLens = true;
                break;
            }
        }

        private void LateUpdate()
        {
            if (!active || view == null) return;
            // SetRegion/SnapToPlayer can also be called directly by gameplay while the rig is disabled.
            view.transform.SetPositionAndRotation(position, rotation);
            if (hasPreviewLens) previewLens.ApplyLens(view);
            foreach (var entry in reveals)
                if (entry.Key != null && !entry.Key.Discovered) entry.Key.SetDiscovered(true, true);
        }

        private void RevealWorld()
        {
            if (world == null) return;
            // Inactive reset templates and intentionally inactive models must stay inactive.
            foreach (RegionRevealView reveal in world.GetComponentsInChildren<RegionRevealView>())
            {
                if (!reveals.ContainsKey(reveal)) reveals.Add(reveal, reveal.Discovered);
                if (!reveal.Discovered) reveal.SetDiscovered(true, true);
            }
        }

        private void Exit()
        {
            if (!active) return;
            active = false;
            foreach (var entry in reveals)
            {
                if (entry.Key == null) continue;
                bool discovered = entry.Value;
                // The player may fall into a new region while observation is active.
                var progression = world != null ? world.GetComponent<WorldMapProgression>() : null;
                var region = entry.Key.GetComponent<LevelRegion>();
                if (progression != null && progression.Modules != null && region != null)
                    foreach (WorldMapModule module in progression.Modules)
                        if (world.FindRegion(module) == region) { discovered |= progression.HasDiscovered(module); break; }
                entry.Key.SetDiscovered(discovered, true);
            }
            reveals.Clear();
            if (rig != null) rig.enabled = restoreRig;
            foreach (var entry in poses)
                if (entry.Key != null)
                {
                    entry.Key.localPosition = entry.Value.position;
                    entry.Key.localRotation = entry.Value.rotation;
                }
            poses.Clear();
            if (view != null)
            {
                view.useOcclusionCulling = occlusion;
                view.usePhysicalProperties = physical;
                view.sensorSize = sensorSize;
                view.lensShift = lensShift;
                view.gateFit = gateFit;
                view.focalLength = focalLength;
                view.fieldOfView = fieldOfView;
                if (physical) view.focalLength = focalLength;
                view.orthographicSize = orthographicSize;
                view.orthographic = orthographic;
                view.nearClipPlane = nearClip;
                view.farClipPlane = farClip;
            }
            if (rig != null) rig.ReleaseControl(this);
            if (controls != null)
            {
                controls.ResumeAfterRewind(); // Reconcile Space presses/releases made while input was suspended.
                controls.enabled = true;
            }
            if (rewind != null) rewind.UseKeyboard = restoreKeyboard;
            controls = null;
            rig = null;
            view = null;
            world = null;
            rewind = null;
            previewModules.Clear();
            previewIndex = -1;
            hasPreviewLens = false;
        }

        private void OnGUI()
        {
            if (active) GUI.Box(new Rect(12f, 12f, 620f, 28f), "DEBUG | U: Return | W/S: Up/Down | A/D: Left/Right | Q/E: Back/Forward | Shift: Fast");
            if (active)
            {
                string selected = previewIndex >= 0 && previewIndex < previewModules.Count
                    ? " | " + world.Source.SequenceNumber(previewModules[previewIndex]) + ": "
                        + previewModules[previewIndex].DisplayName : "";
                GUI.Box(new Rect(12f, 44f, 620f, 28f), "Up/Down: Previous/Next camera preset" + selected);
            }
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode) => Exit();
        private void OnDisable() => Exit();
        private void OnDestroy()
        {
            Exit();
            SceneManager.sceneLoaded -= SceneLoaded;
            if (current == this) current = null;
        }
    }
}
