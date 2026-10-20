using System.Collections;
using System.Collections.Generic;
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
        [InspectorName("贝塞尔路径点（按顺序）")]
        [Tooltip("曲线平滑经过这些锚点，控制柄自动生成。只使用路径点的 X/Z，Y 持续向下检测场景碰撞决定；不会修改路点。")]
        public Transform[] IntroWaypoints = new Transform[0];
        [Range(0f, 1f), InspectorName("贝塞尔曲率")]
        public float PathSmoothing = 0.8f;
        [Range(8, 128), Tooltip("每段用于弧长计算的采样数量，使移动速度不随曲线参数变化。")]
        public int PathSamplesPerSegment = 32;
        [InspectorName("最后一个路径点作为水平行走线")]
        [Tooltip("开启后，最后到达的路径点作为左右移动的原点；关闭后使用 Player 上手动指定的 WalkLine。")]
        public bool UseLastWaypointAsWalkLine = true;
        public LayerMask GroundLayers = 1;
        public bool PlayIntroOnStart = true;
        [Min(0f)] public float IntroHold = 0.6f;
        [Min(0.001f), InspectorName("受阻检测的前进距离")]
        public float WaypointTolerance = 0.08f;
        [Min(1f), InspectorName("路径受阻超时（秒）"), Tooltip("持续无法前进时恢复操作，长路径正常行走不受总时长限制。")]
        public float WaypointTimeout = 15f;

        [Header("镜头推进（保持原旋转与 FOV）")]
        [Min(0f)] public float CameraApproachDistance = 1.2f;
        [InspectorName("推进时向右偏移（米）")]
        [Tooltip("沿相机自身的右方向平移，让角色更靠近屏幕中心；正值向右，负值向左。")]
        public float CameraApproachRightOffset = 0.45f;
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
        private MainMenuCameraMotion cameraBreathing;
        private bool capturedCamera;
        private VoicePlayer subscribedVoice;
        private Component previousVoiceLockOwner;
        private Coroutine flow, cameraMotion;
        private EndingBezierPath introPath;
        private float introStartHeight;
        private readonly List<Vector3> previewSamples = new List<Vector3>();
        private readonly List<bool> previewGrounded = new List<bool>();
        private readonly List<Vector3> previewAnchors = new List<Vector3>();
        private readonly Dictionary<Transform, Vector3> previewWaypoints = new Dictionary<Transform, Vector3>();

        private void Start()
        {
            if (Player == null || Radio == null || ViewCamera == null)
            {
                Debug.LogError("结局缺少 Player、Radio 或 ViewCamera 引用。", this);
                return;
            }
            Player.ViewCamera = ViewCamera;
            Player.GroundLayers = GroundLayers;
            Radio.Sequence = this;
            cameraBreathing = ViewCamera.GetComponent<MainMenuCameraMotion>();
            initialCameraPosition = ReadCameraBasePosition();
            capturedCamera = true;
            if (PlayIntroOnStart) PlayIntro();
            else ReturnControl();
        }

        private bool Paused => Time.timeScale <= 0f || PauseMenu.BlocksInput;

        private void AcquireControl()
        {
            Player.SetControl(false);
            GameplayActionLocks.Acquire(this, GameplayAction.Input | GameplayAction.PlayerControls
                | GameplayAction.Rewind);
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
            introStartHeight = Player.transform.position.y;
            introPath = BuildPath(Player.transform.position);
            yield return Wait(IntroHold);
            Player.FollowBezier(introPath);
            float lastProgress = 0f, stalled = 0f;
            bool interrupted = false;
            while (Player.IsFollowingPath)
            {
                if (!Paused)
                {
                    if (Player.PathDistance - lastProgress >= Mathf.Max(0.001f, WaypointTolerance))
                    {
                        lastProgress = Player.PathDistance;
                        stalled = 0f;
                    }
                    else stalled += Time.deltaTime;
                    if (stalled >= WaypointTimeout)
                    {
                        Debug.LogWarning("结局贝塞尔路径受阻或前方没有可行走地面，已归还操作权，请检查曲线与场景碰撞。", this);
                        Player.StopMotion();
                        Player.WalkLine = null;
                        interrupted = true;
                        break;
                    }
                }
                yield return null;
            }
            if (UseLastWaypointAsWalkLine && !interrupted) Player.WalkLine = LastWaypoint();
            flow = null;
            ReturnControl();
            OnIntroCompleted.Invoke();
        }

        private Transform LastWaypoint()
        {
            if (IntroWaypoints == null) return null;
            for (int index = IntroWaypoints.Length - 1; index >= 0; index--)
                if (IntroWaypoints[index] != null) return IntroWaypoints[index];
            return null;
        }

        private EndingBezierPath BuildPath(Vector3 start)
        {
            var points = new List<Vector3> { start };
            if (IntroWaypoints != null)
                foreach (Transform point in IntroWaypoints)
                    if (point != null) points.Add(point.position);
            return new EndingBezierPath(points, PathSmoothing, PathSamplesPerSegment);
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
                - initialCameraPosition).normalized * CameraApproachDistance
                + (cameraBreathing != null && cameraBreathing.isActiveAndEnabled
                    ? cameraBreathing.BaseWorldRotation * Vector3.right : ViewCamera.transform.right)
                    * CameraApproachRightOffset, CameraApproachDuration);
            AudioManager.Instance?.BeginEndingMusic(this);
            OnBroadcastStarted.Invoke();
            return true;
        }

        private void VoiceEnded(VoicePlayer voice, VoiceSequence sequence, VoiceStopReason reason)
        {
            if (State != EndingState.Broadcast) return;
            UnsubscribeVoice();
            if (reason != VoiceStopReason.Completed)
            {
                AudioManager.Existing?.CancelPendingEndingMusic(this);
                Radio.ResetTrigger();
                ReturnControl();
                return;
            }
            GameCompletionStore.MarkCompleted();
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
            if (!Application.isPlaying || !isActiveAndEnabled || Player == null
                || State == EndingState.Transition || State == EndingState.ReturningControl) return;
            StopFlow();
            VoicePlayer voice = subscribedVoice;
            UnsubscribeVoice();
            if (voice != null && voice.IsPlaying)
            {
                AudioManager.Existing?.CancelPendingEndingMusic(this);
                voice.Stop(); Radio.ResetTrigger();
            }
            AcquireControl();
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
            AudioManager.Existing?.CancelPendingEndingMusic(this);
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
            flow = null;
            if (!EndingWhiteFade.TryLoad(MainMenuScenePath, FadeToWhiteDuration, WhiteHoldDuration, RevealMainMenuDuration))
            {
                State = EndingState.Broadcast;
                ReturnControl();
            }
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
            Vector3 from = ReadCameraBasePosition();
            float elapsed = 0f;
            while (elapsed < duration && ViewCamera != null)
            {
                if (!Paused) elapsed += Time.deltaTime;
                ApplyCameraBasePosition(Vector3.Lerp(from, target,
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration))));
                yield return null;
            }
            if (ViewCamera != null) ApplyCameraBasePosition(target);
            cameraMotion = null;
        }

        private Vector3 ReadCameraBasePosition() => cameraBreathing != null && cameraBreathing.isActiveAndEnabled
            ? cameraBreathing.BaseWorldPosition : ViewCamera.transform.position;

        private void ApplyCameraBasePosition(Vector3 position)
        {
            if (cameraBreathing != null) cameraBreathing.SetBaseWorldPosition(position);
            else ViewCamera.transform.position = position;
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
            AudioManager.Existing?.CancelPendingEndingMusic(this);
            StopAllCoroutines();
            flow = cameraMotion = null;
            VoicePlayer voice = subscribedVoice;
            UnsubscribeVoice();
            if (voice != null && voice.IsPlaying) voice.Stop(VoiceStopReason.Disabled);
            GameplayActionLocks.Release(this);
            if (Player != null) Player.SetControl(false);
            if (capturedCamera && ViewCamera != null) ApplyCameraBasePosition(initialCameraPosition);
            State = EndingState.Inactive;
            introPath = null;
        }

        public void RefreshPathPreview()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) Physics.SyncTransforms();
#endif
            previewSamples.Clear();
            previewGrounded.Clear();
            previewAnchors.Clear();
            previewWaypoints.Clear();
            if (IntroWaypoints == null) return;
            Vector3 start = Player != null ? Player.transform.position : transform.position;
            EndingBezierPath path = Application.isPlaying && introPath != null ? introPath : BuildPath(start);
            if (path.AnchorCount == 0) return;
            float height = Application.isPlaying && introPath != null ? introStartHeight : start.y;
            previewAnchors.Add(AddPreviewPoint(path.GetAnchor(0), ref height, true));
            for (int index = 0; index < path.SegmentCount; index++)
            {
                EndingBezierPath.Segment segment = path.GetSegment(index);
                float length = path.GetAnchorDistance(index + 1) - path.GetAnchorDistance(index);
                int steps = Mathf.Clamp(Mathf.CeilToInt(length / 0.12f), 8, 128);
                for (int step = 1; step <= steps; step++)
                    AddPreviewPoint(segment.Point((float)step / steps), ref height);
                previewAnchors.Add(previewSamples[previewSamples.Count - 1]);
            }
            int anchor = 0;
            foreach (Transform point in IntroWaypoints)
            {
                if (point == null) continue;
                Vector3 flat = new Vector3(point.position.x, 0f, point.position.z);
                while (anchor + 1 < path.AnchorCount && (path.GetAnchor(anchor) - flat).sqrMagnitude > 0.000001f) anchor++;
                previewWaypoints[point] = previewAnchors[anchor];
            }
        }

        private Vector3 AddPreviewPoint(Vector3 flat, ref float height, bool initial = false)
        {
            Vector3 grounded = new Vector3(flat.x, height, flat.z);
            bool found = Player != null && Player.TryGetGroundPosition(flat, height, GroundLayers, out grounded, initial);
            if (found) height = grounded.y;
            previewSamples.Add(grounded);
            previewGrounded.Add(found);
            return grounded;
        }

        public Vector3 WaypointPreviewPosition(Transform point) => previewWaypoints.TryGetValue(point, out Vector3 value)
            ? value : new Vector3(point.position.x, Player != null ? Player.transform.position.y : transform.position.y, point.position.z);

        private void OnDrawGizmos()
        {
            RefreshPathPreview();
            Color previous = Gizmos.color;
            for (int index = 1; index < previewSamples.Count; index++)
            {
                Gizmos.color = previewGrounded[index - 1] && previewGrounded[index]
                    ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 0.3f, 0.25f);
                Gizmos.DrawLine(previewSamples[index - 1], previewSamples[index]);
            }
            Gizmos.color = new Color(1f, 0.75f, 0.2f);
            foreach (Vector3 point in previewWaypoints.Values) Gizmos.DrawWireSphere(point, 0.12f);
            if (Player != null)
            {
                Transform last = UseLastWaypointAsWalkLine ? LastWaypoint() : Player.WalkLine;
                Vector3 origin = last != null ? WaypointPreviewPosition(last) : Player.transform.position;
                Vector3 right = ViewCamera != null
                    ? Vector3.ProjectOnPlane(ViewCamera.transform.right, Vector3.up).normalized : Vector3.right;
                DrawGroundedRail(origin, right * Player.WalkLimits.x);
                DrawGroundedRail(origin, right * Player.WalkLimits.y);
            }
            Gizmos.color = previous;
        }

        private void DrawGroundedRail(Vector3 origin, Vector3 delta)
        {
            Vector3 previous = origin;
            float height = origin.y;
            int steps = Mathf.Clamp(Mathf.CeilToInt(delta.magnitude / 0.15f), 1, 128);
            for (int index = 1; index <= steps; index++)
            {
                Vector3 point = origin + delta * ((float)index / steps);
                bool found = Player.TryGetGroundPosition(point, height, GroundLayers, out Vector3 grounded);
                if (found) height = grounded.y;
                Gizmos.color = found ? Color.cyan : new Color(1f, 0.3f, 0.25f);
                Gizmos.DrawLine(previous, grounded);
                previous = grounded;
            }
        }
    }
}
