using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap.Hints
{
    [DisallowMultipleComponent, DefaultExecutionOrder(10000)]
    public sealed class HintReplayRecorder : MonoBehaviour
    {
        public PlayerController Player;
        [Tooltip("仅正向录制使用，默认使用当前 LevelRegion。返程自动记录世界坐标，忽略此字段。")]
        public Transform ReferenceFrame;
        [Range(10, 60)] public int SamplesPerSecond = 30;
        [Min(1f)] public float MaximumDuration = 180f;
        public bool IsRecording { get; private set; }
        public string Status { get; private set; } = "尚未录制";
        public int Revision { get; private set; }
        public int FrameCount => frames.Count;
        public float Duration => elapsed;
        public event Action<HintReplayRecorder> Stopped;
        public Transform RecordingReferenceFrame => recordingReference;
        public bool RecordsWorldSpace => recordWorldSpace;

        private readonly List<HintReplayFrame> frames = new List<HintReplayFrame>();
        private HintReplayVisuals visuals;
        private PlayerInput input;
        private LevelRegion startingRegion;
        private GameStage startingStage;
        private float elapsed, nextSample;
        private bool hadReference;
        private Transform recordingReference;
        private bool recordWorldSpace;

        public bool BeginRecording()
        {
            if (!Application.isPlaying || IsRecording || !isActiveAndEnabled) return false;
            if (Player == null) Player = HintReplayContext.Find<PlayerController>(gameObject.scene);
            if (Player == null || Player.gameObject.scene != gameObject.scene || Player.Body == null || Player.Head == null
                || Player.Body.Visual == null || Player.Head.Visual == null)
            { Status = "同场景中没有完整的玩家显示层级。"; return false; }
            if (RewindManager.Rewinding || HintReplayContext.Transitioning(gameObject.scene))
            { Status = "请等待回溯或转场结束。"; return false; }
            startingRegion = HintReplayContext.Region(gameObject.scene, Player);
            startingStage = GameStageProgression.StageIn(gameObject.scene);
            recordWorldSpace = startingStage >= GameStage.Returning;
            if (startingStage == GameStage.Outbound && startingRegion != null
                && ReferenceFrame != null && ReferenceFrame != startingRegion.transform)
            { Status = "正向录像请清空参考点，或使用当前 LevelRegion 的 Transform。"; return false; }
            recordingReference = recordWorldSpace ? null : ReferenceFrame != null ? ReferenceFrame : startingRegion != null ? startingRegion.transform : null;
            if (recordingReference != null)
            {
                Vector3 scale = recordingReference.lossyScale;
                if (scale.x <= 0f || Mathf.Abs(scale.x - scale.y) > 0.001f || Mathf.Abs(scale.x - scale.z) > 0.001f
                    || recordingReference.IsChildOf(Player.transform)
                    || recordingReference.IsChildOf(Player.Body.transform) || recordingReference.IsChildOf(Player.Head.transform))
                { Status = "参考点需要正值等比缩放，并且不能跟随玩家。"; return false; }
            }
            visuals = new HintReplayVisuals(Player.Body.Visual, Player.Head.Visual);
            if (visuals.Renderers.Count == 0) { Status = "玩家没有可录制的网格或 Sprite。"; return false; }
            HintReplayContext.Find<HintReplayPlayer>(gameObject.scene)?.Stop();
            frames.Clear(); elapsed = nextSample = 0f;
            hadReference = recordingReference != null;
            input = Player.GetComponent<PlayerInput>();
            IsRecording = true;
            Capture();
            nextSample = 1f / Mathf.Max(1, SamplesPerSecond);
            Status = "正在录制";
            return true;
        }

        private void LateUpdate()
        {
            if (!IsRecording) return;
            if (Player == null || Player.Body == null || Player.Head == null || hadReference && recordingReference == null)
            { StopRecording("玩家或参考点已被移除。", false); return; }
            if (RewindManager.Rewinding || HintReplayContext.Transitioning(gameObject.scene)
                || GameStageProgression.StageIn(gameObject.scene) != startingStage
                || startingStage == GameStage.Outbound && HintReplayContext.Region(gameObject.scene, Player) != startingRegion)
            { StopRecording("回溯、转场、切关或流程变化：已结束并保留录制。", false); return; }
            // Respawn is part of a solution. Keep its collapse/rebuild poses and the following actions on the same timeline.
            if (HintReplayContext.Paused) return;
            if (!visuals.StillValid()) { StopRecording("角色显示层级发生变化，请重新录制。", false); return; }
            elapsed = Mathf.Min(MaximumDuration, elapsed + Time.deltaTime);
            if (elapsed >= nextSample)
            {
                Capture();
                // Never synthesize multiple identical poses after a slow rendered frame.
                nextSample = elapsed + 1f / Mathf.Max(1, SamplesPerSecond);
            }
            if (elapsed >= MaximumDuration) StopRecording("已达到最大录制时长。", true);
        }

        public void StopRecording() => StopRecording("录制已结束", true);

        private void StopRecording(string reason, bool finalFrame)
        {
            if (!IsRecording) return;
            if (finalFrame && elapsed > frames[frames.Count - 1].Time && visuals.StillValid()) Capture();
            IsRecording = false;
            Status = reason;
            Revision++;
            Stopped?.Invoke(this);
        }

        public HintReplayClip CreateClip()
        {
            if (IsRecording || visuals == null || frames.Count < 2) return null;
            var result = ScriptableObject.CreateInstance<HintReplayClip>();
            result.SetRecording(visuals.Layout.ToArray(), frames.ToArray(), recordWorldSpace);
            return result;
        }

        private void Capture()
        {
            var frame = new HintReplayFrame
            {
                Time = elapsed,
                Poses = new HintReplayPose[visuals.Transforms.Count],
                Visible = new bool[visuals.Renderers.Count],
                Connected = Player.HasMagneticConnection && Player.Phase != PlayerPhase.Joined
                    && !Player.Body.IsReturning && !Player.Head.IsReturning,
                ConnectionFrom = LocalPoint(Player.Body.Visual.position + Player.Body.Visual.up * Player.Body.Motor.Size.y * 0.42f),
                ConnectionTo = LocalPoint(Player.Head.Visual.position - Player.Head.Visual.up * Player.Head.Motor.Size.y * 0.42f),
                Horizontal = input != null ? input.Horizontal : 0f,
                SpaceHeld = input != null && input.SpaceHeld,
                SlowHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
            };
            for (int i = 0; i < frame.Poses.Length; i++)
            {
                Transform node = visuals.Transforms[i];
                bool root = visuals.Layout[i].Parent < 0;
                frame.Poses[i] = new HintReplayPose
                {
                    Position = root ? LocalPoint(node.position) : node.localPosition,
                    Rotation = root ? Quaternion.Inverse(recordingReference != null ? recordingReference.rotation : Quaternion.identity) * node.rotation
                        : node.localRotation,
                    Scale = root ? node.lossyScale / (recordingReference != null ? recordingReference.lossyScale.x : 1f) : node.localScale,
                    Active = root ? node.gameObject.activeInHierarchy : node.gameObject.activeSelf
                };
            }
            for (int i = 0; i < frame.Visible.Length; i++) frame.Visible[i] = visuals.Renderers[i].enabled;
            frames.Add(frame);
        }

        private Vector3 LocalPoint(Vector3 world) => recordingReference != null ? recordingReference.InverseTransformPoint(world) : world;
        private void OnDisable() => StopRecording("录制组件已停用，已保留录制。", false);
    }
}
