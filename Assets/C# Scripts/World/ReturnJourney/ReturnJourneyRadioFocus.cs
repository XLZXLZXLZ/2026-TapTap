using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent, RequireComponent(typeof(RadioReceiver))]
    [DefaultExecutionOrder(900)]
    public sealed class ReturnJourneyRadioFocus : MonoBehaviour
    {
        public DemoCamera CameraRig;
        [Tooltip("特写中心。留空时使用收音机根节点上方半格。")]
        public Transform FocusTarget;
        [Min(0.5f), Tooltip("特写画面的垂直高度，单位为格。")]
        public float CloseupHeight = 2.5f;
        [Min(1f), Tooltip("镜头构图余量；1.2 在原有高度上拉远 20%，已有场景配置也会生效。")]
        public float ViewPadding = 1.2f;
        [Range(10f, 80f)] public float FieldOfView = 35f;
        [Min(0.01f)] public float ApproachDuration = 0.6f;
        [Min(0.01f)] public float RestoreDuration = 0.5f;

        private RadioReceiver receiver;
        private VoicePlayer playback;
        private PlayerController player;
        private LevelCameraShot initialShot, closeShot, restoreFrom;
        private float elapsed;
        private bool restoring;
        public bool IsActive { get; private set; }
        public float EffectiveCloseupHeight => Mathf.Max(0.5f, CloseupHeight) * Mathf.Max(1f, ViewPadding);

        private void OnEnable()
        {
            receiver = GetComponent<RadioReceiver>();
            playback = GetComponent<VoicePlayer>();
            playback.Began += PlaybackBegan;
            playback.Ended += PlaybackEnded;
            if (playback.IsPlaying) PlaybackBegan(playback, playback.Sequence);
        }

        // Can also be invoked by another trigger or a UnityEvent; body entry uses RadioReceiver.
        public bool TryActivate() => isActiveAndEnabled && !IsActive
            && GameStageProgression.HasProgressedIn(gameObject.scene) && !GameStageProgression.InputBlocked
            && receiver.TryPlay();
        public void Activate() => TryActivate();

        private void PlaybackBegan(VoicePlayer voice, VoiceSequence sequence)
        {
            if (IsActive || !GameStageProgression.HasProgressedIn(gameObject.scene)
                || GameStageProgression.InputBlocked || sequence != receiver.ReturnJourney) return;
            player = receiver.Player != null ? receiver.Player : ReturnJourneyContact.FindPlayer(gameObject.scene);
            if (CameraRig == null)
                foreach (DemoCamera rig in FindObjectsOfType<DemoCamera>())
                    if (rig.gameObject.scene == gameObject.scene) { CameraRig = rig; break; }
            if (player == null || CameraRig == null || CameraRig.gameObject.scene != gameObject.scene
                || CameraRig.ViewCamera == null || !CameraRig.TryAcquireControl(this))
            {
                Debug.LogWarning("返程收音机特写未取得同场景镜头；广播继续正常播放。", this);
                return;
            }

            initialShot = LevelCameraShot.Read(CameraRig.ViewCamera);
            closeShot = initialShot;
            float unit = player.Config.UnitSize;
            closeShot.Focus = FocusTarget != null ? FocusTarget.position : transform.position + Vector3.up * (0.5f * unit);
            closeShot.FieldOfView = FieldOfView;
            closeShot.Physical = false;
            closeShot.LensShift = Vector2.zero;
            closeShot.NearClip = Mathf.Min(initialShot.NearClip, 0.05f);
            float distance = EffectiveCloseupHeight * unit / (2f * Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad));
            closeShot.Position = closeShot.Focus - (initialShot.Rotation * Vector3.forward) * distance;
            elapsed = 0f;
            restoring = false;
            IsActive = true;
            // Block controls without pausing simulation, animation or the broadcast.
            GameplayActionLocks.Acquire(this, GameplayAction.PlayerControls | GameplayAction.Rewind | GameplayAction.DebugCamera);
            RewindManager.Current?.EndRewind();
            player.GetComponent<PlayerInput>()?.ResetForTransition();
        }

        private void PlaybackEnded(VoicePlayer voice, VoiceSequence sequence, VoiceStopReason reason)
        {
            if (!IsActive || restoring) return;
            if (CameraRig == null || CameraRig.ViewCamera == null) { Release(); return; }
            restoreFrom = LevelCameraShot.Read(CameraRig.ViewCamera);
            elapsed = 0f;
            restoring = true;
        }

        private void LateUpdate()
        {
            if (!IsActive) return;
            if (CameraRig == null || CameraRig.ViewCamera == null || player == null) { Release(); return; }
            if (Time.timeScale <= 0f || WorldMapProgression.IsInputBlocked(this)) return;
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, restoring ? RestoreDuration : ApproachDuration));
            LevelCameraShot shot = LevelCameraShot.Blend(restoring ? restoreFrom : initialShot,
                restoring ? GameplayShot() : closeShot, Mathf.SmoothStep(0f, 1f, t));
            Apply(shot);
            if (restoring && t >= 1f) Release();
        }

        private void Apply(LevelCameraShot shot)
        {
            Camera camera = CameraRig.ViewCamera;
            camera.transform.SetPositionAndRotation(shot.Position, shot.Rotation);
            shot.ApplyLens(camera);
        }

        private LevelCameraShot GameplayShot() => CameraRig.Region != null
            ? LevelCameraShot.Evaluate(CameraRig.Region, null, CameraRig.ViewCamera.aspect) : initialShot;

        private void Release()
        {
            if (!IsActive) return;
            IsActive = false;
            GameplayActionLocks.Release(this);
            if (player != null) player.GetComponent<PlayerInput>()?.ResetForTransition();
            if (CameraRig != null)
            {
                if (CameraRig.ViewCamera != null) Apply(GameplayShot());
                CameraRig.ReleaseControl(this);
            }
        }

        private void OnDisable()
        {
            if (playback != null)
            {
                playback.Began -= PlaybackBegan;
                playback.Ended -= PlaybackEnded;
                if (IsActive) playback.Stop(VoiceStopReason.Interrupted);
            }
            Release();
        }
    }
}
