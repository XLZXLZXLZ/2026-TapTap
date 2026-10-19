using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace TapTap
{
    public enum EndingState { Inactive, Intro, Exploration, Broadcast, ReturningControl, Transition }

    [DisallowMultipleComponent, DefaultExecutionOrder(-100)]
    public sealed class EndingSequenceController : MonoBehaviour
    {
        [Header("场景引用")]
        public EndingPlayerController Player;
        public EndingRadioTrigger Radio;
        public Camera ViewCamera;
        [Tooltip("顺序为堡垒内、门口、可左右行走的位置；以 XZ 路径移动，Y 由地面碰撞决定。")]
        public Transform[] IntroWaypoints = new Transform[0];
        public LayerMask GroundLayers = 1;
        public bool PlayIntroOnStart = true;
        [Min(0f)] public float IntroHold = 0.6f;
        [Min(0.01f)] public float WaypointTolerance = 0.08f;
        [Min(1f), Tooltip("路线受阻时恢复操作，避免角色一直原地踏步。")]
        public float WaypointTimeout = 15f;

        [Header("镜头推进（保持原旋转与 FOV）")]
        [Min(0f)] public float CameraApproachDistance = 1.2f;
        [Min(0f)] public float CameraApproachDuration = 1.5f;
        [Min(0f)] public float CameraRestoreDuration = 0.8f;

        [Header("广播结束")]
        [Tooltip("关闭后，广播结束会归还操作权；也可通过事件调用 ReturnControl。")]
        public bool ReturnToMainMenuAfterBroadcast = true;
        public string MainMenuScenePath = "Assets/Scenes/MainMenu/MainMenu.unity";
        [Min(0f)] public float AfterVoiceHold = 1.5f;
        [Min(0f)] public float FadeToWhiteDuration = 2f;
        [Min(0f)] public float WhiteHoldDuration = 0.5f;
        [Min(0f)] public float RevealMainMenuDuration = 2f;
        public UnityEvent OnIntroCompleted = new UnityEvent();
        public UnityEvent OnBroadcastStarted = new UnityEvent();
        public UnityEvent OnBroadcastCompleted = new UnityEvent();
        public UnityEvent OnControlReturned = new UnityEvent();
        public EndingState State { get; private set; }

        private Vector3 initialCameraPosition;
        private bool capturedCamera;
        private VoicePlayer subscribedVoice;
        private Component previousVoiceLockOwner;
        private Coroutine flow, cameraMotion;

        private void Start()
        {
            if (Player == null || Radio == null || ViewCamera == null)
            {
                Debug.LogError("结局缺少 Player、Radio 或 ViewCamera 引用。", this);
                return;
            }
            Player.ViewCamera = ViewCamera;
            Radio.Sequence = this;
            initialCameraPosition = ViewCamera.transform.position;
            capturedCamera = true;
            if (PlayIntroOnStart) PlayIntro();
            else ReturnControl();
        }

        private bool Paused => Time.timeScale <= 0f || PauseMenu.BlocksInput;

        private void AcquireControl()
        {
            Player.SetControl(false);
            GameplayActionLocks.Acquire(this, GameplayAction.Input | GameplayAction.PlayerControls
                | GameplayAction.Rewind | GameplayAction.DebugCamera);
            RewindManager.Current?.EndRewind();
        }

        [ContextMenu("播放出场")]
        public void PlayIntro()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || Player == null || State != EndingState.Inactive) return;
            AcquireControl();
            State = EndingState.Intro;
            flow = StartCoroutine(Intro());
        }

        private IEnumerator Intro()
        {
            Player.SnapToGround(GroundLayers);
            yield return Wait(IntroHold);
            foreach (Transform waypoint in IntroWaypoints)
            {
                if (waypoint == null) continue;
                float elapsed = 0f;
                while (elapsed < WaypointTimeout)
                {
                    Vector3 delta = Vector3.ProjectOnPlane(waypoint.position - Player.transform.position, Vector3.up);
                    if (delta.magnitude <= WaypointTolerance) break;
                    if (!Paused)
                    {
                        elapsed += Time.deltaTime;
                        float speed = Mathf.Min(Player.IntroSpeed, delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f));
                        Player.SetScriptedMotion(delta.normalized, speed);
                    }
                    yield return null;
                }
                Player.StopMotion();
                if (elapsed >= WaypointTimeout)
                {
                    Debug.LogWarning("结局出场路线受阻，已归还操作权，请检查路点和地面碰撞。", waypoint);
                    // Rebase a blocked intro rather than pulling the character sideways to an unreachable rail.
                    Player.WalkLine = null;
                    break;
                }
            }
            flow = null;
            ReturnControl();
            OnIntroCompleted.Invoke();
        }

        public bool TryBeginBroadcast(EndingRadioTrigger radio, EndingPlayerController player)
        {
            if (!isActiveAndEnabled || State != EndingState.Exploration || player != Player || radio != Radio
                || Paused || radio.EndingVoice == null || !radio.EndingVoice.IsConfigured
                || GameplayActionLocks.IsLocked(GameplayAction.Input | GameplayAction.PlayerControls, gameObject.scene)) return false;
            AcquireControl();
            State = EndingState.Broadcast;
            subscribedVoice = radio.Playback;
            previousVoiceLockOwner = subscribedVoice.InputLockOwner;
            // VoicePlayer must ignore the same input lease that freezes the robot during its speech.
            subscribedVoice.InputLockOwner = this;
            subscribedVoice.SetDistanceGain(1f);
            subscribedVoice.Ended += VoiceEnded;
            if (!subscribedVoice.Play(radio.EndingVoice))
            {
                ReturnControl();
                return false;
            }
            StartCameraMotion(initialCameraPosition + (Player.transform.position + Vector3.up * 0.8f
                - initialCameraPosition).normalized * CameraApproachDistance, CameraApproachDuration);
            OnBroadcastStarted.Invoke();
            return true;
        }

        private void VoiceEnded(VoicePlayer voice, VoiceSequence sequence, VoiceStopReason reason)
        {
            if (State != EndingState.Broadcast) return;
            UnsubscribeVoice();
            if (reason != VoiceStopReason.Completed)
            {
                Radio.ResetTrigger();
                ReturnControl();
                return;
            }
            OnBroadcastCompleted.Invoke();
            // An event listener can call ReturnControl, or change the default ending policy.
            if (State != EndingState.Broadcast) return;
            if (ReturnToMainMenuAfterBroadcast) ReturnToMainMenu();
            else ReturnControl();
        }

        /// <summary>Public UnityEvent/Timeline hook: stop the sequence, restore the camera and allow walking.</summary>
        [ContextMenu("归还操作权")]
        public void ReturnControl()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || Player == null || State == EndingState.Transition) return;
            StopFlow();
            VoicePlayer voice = subscribedVoice;
            UnsubscribeVoice();
            if (voice != null && voice.IsPlaying) { voice.Stop(); Radio.ResetTrigger(); }
            Player.StopMotion();
            State = EndingState.ReturningControl;
            flow = StartCoroutine(RestoreControl());
        }

        private IEnumerator RestoreControl()
        {
            if (cameraMotion != null) { StopCoroutine(cameraMotion); cameraMotion = null; }
            if (capturedCamera && ViewCamera != null)
                yield return MoveCamera(initialCameraPosition, CameraRestoreDuration);
            Player.CaptureWalkLine();
            GameplayActionLocks.Release(this);
            State = EndingState.Exploration;
            Player.SetControl(true);
            flow = null;
            OnControlReturned.Invoke();
        }

        /// <summary>Optional hook for a later interaction after the broadcast.</summary>
        public void ReturnToMainMenu()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || State == EndingState.Transition || Player == null) return;
            StopFlow();
            VoicePlayer voice = subscribedVoice;
            UnsubscribeVoice();
            if (voice != null && voice.IsPlaying) voice.Stop();
            AcquireControl();
            State = EndingState.Transition;
            flow = StartCoroutine(Leave());
        }

        private IEnumerator Leave()
        {
            yield return Wait(AfterVoiceHold);
            if (!EndingWhiteFade.TryLoad(MainMenuScenePath, FadeToWhiteDuration, WhiteHoldDuration, RevealMainMenuDuration))
            {
                State = EndingState.Broadcast;
                ReturnControl();
            }
            flow = null;
        }

        // Called by the persistent cover if loading fails after the fade has started.
        public void RecoverFromFailedTransition()
        {
            if (State != EndingState.Transition) return;
            State = EndingState.Broadcast;
            ReturnControl();
        }

        private IEnumerator Wait(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (!Paused) elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private void StartCameraMotion(Vector3 target, float duration)
        {
            if (cameraMotion != null) StopCoroutine(cameraMotion);
            cameraMotion = StartCoroutine(MoveCamera(target, duration));
        }

        private IEnumerator MoveCamera(Vector3 target, float duration)
        {
            if (ViewCamera == null) yield break;
            Vector3 from = ViewCamera.transform.position;
            float elapsed = 0f;
            while (elapsed < duration && ViewCamera != null)
            {
                if (!Paused) elapsed += Time.deltaTime;
                ViewCamera.transform.position = Vector3.Lerp(from, target,
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            if (ViewCamera != null) ViewCamera.transform.position = target;
            cameraMotion = null;
        }

        private void StopFlow()
        {
            if (flow != null) StopCoroutine(flow);
            flow = null;
        }

        private void UnsubscribeVoice()
        {
            if (subscribedVoice == null) return;
            subscribedVoice.Ended -= VoiceEnded;
            subscribedVoice.InputLockOwner = previousVoiceLockOwner;
            subscribedVoice = null;
            previousVoiceLockOwner = null;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            flow = cameraMotion = null;
            VoicePlayer voice = subscribedVoice;
            UnsubscribeVoice();
            if (voice != null && voice.IsPlaying) voice.Stop(VoiceStopReason.Disabled);
            GameplayActionLocks.Release(this);
            if (Player != null) Player.SetControl(false);
            if (capturedCamera && ViewCamera != null) ViewCamera.transform.position = initialCameraPosition;
            State = EndingState.Inactive;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.2f);
            Transform previous = null;
            foreach (Transform point in IntroWaypoints)
            {
                if (point == null) continue;
                Gizmos.DrawWireSphere(point.position, 0.15f);
                if (previous != null) Gizmos.DrawLine(previous.position, point.position);
                previous = point;
            }
        }
    }
}
