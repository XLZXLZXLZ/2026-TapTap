using System;
using UnityEngine;

namespace TapTap.Hints
{
    [PreferBinarySerialization, CreateAssetMenu(menuName = "TapTap/Hints/Replay Clip")]
    public sealed class HintReplayClip : ScriptableObject
    {
        public const int CurrentVersion = 1;
        [SerializeField, HideInInspector] private int version = CurrentVersion;
        [SerializeField, HideInInspector] private HintReplayNode[] nodes = Array.Empty<HintReplayNode>();
        [SerializeField, HideInInspector] private HintReplayFrame[] frames = Array.Empty<HintReplayFrame>();
        [SerializeField, HideInInspector] private bool worldSpace;
        [SerializeField, TextArea] private string notes;

        public int Version => version;
        public HintReplayNode[] Nodes => nodes;
        public HintReplayFrame[] Frames => frames;
        // Missing in older assets: false preserves their reference-frame coordinates.
        public bool UsesWorldSpace => worldSpace;
        public string Notes { get => notes; set => notes = value; }
        public float Duration => frames != null && frames.Length > 0 ? frames[frames.Length - 1].Time : 0f;

        // The editor owns persistence; runtime recording has no dependency on AssetDatabase.
        public void SetRecording(HintReplayNode[] layout, HintReplayFrame[] recording, bool useWorldSpace = false)
        {
            version = CurrentVersion;
            nodes = layout;
            frames = recording;
            worldSpace = useWorldSpace;
        }

        public bool Validate(out string error)
        {
            error = null;
            if (version != CurrentVersion) error = "录像版本不兼容，请重新录制。";
            else if (nodes == null || nodes.Length < 2 || frames == null || frames.Length < 2 || Duration <= 0f)
                error = "录像至少需要两个姿态帧和大于零的时长。";
            if (error != null) return false;
            int roots = 0, renderers = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] == null || nodes[i].Parent < -1 || nodes[i].Parent >= i || nodes[i].RendererCount < 0)
                { error = "录像层级损坏。"; return false; }
                if (nodes[i].Parent == -1) roots++;
                renderers += nodes[i].RendererCount;
            }
            if (roots != 2 || renderers == 0) { error = "录像需要身体、头部两个显示层级和至少一个渲染器。"; return false; }
            float previous = -1f;
            foreach (HintReplayFrame frame in frames)
            {
                if (frame == null || float.IsNaN(frame.Time) || float.IsInfinity(frame.Time)
                    || frame.Time < 0f || frame.Time <= previous || frame.Poses == null || frame.Poses.Length != nodes.Length
                    || frame.Visible == null || frame.Visible.Length != renderers)
                { error = "录像帧不完整或时间顺序错误。"; return false; }
                previous = frame.Time;
            }
            if (frames[0].Time != 0f) { error = "录像必须从零秒开始。"; return false; }
            return true;
        }

        // Binary search also serves editor scrubbing without maintaining a mutable playback cursor.
        public void Sample(float time, out HintReplayFrame from, out HintReplayFrame to, out float blend)
        {
            int low = 0, high = frames.Length - 1;
            time = Mathf.Clamp(time, 0f, Duration);
            while (low + 1 < high)
            {
                int middle = (low + high) / 2;
                if (frames[middle].Time <= time) low = middle;
                else high = middle;
            }
            from = frames[low]; to = frames[high];
            blend = Mathf.InverseLerp(from.Time, to.Time, time);
        }
    }

    [Serializable]
    public sealed class HintReplayNode
    {
        public string Name;
        public int Parent = -1;
        public int RendererCount;
    }

    [Serializable]
    public struct HintReplayPose
    {
        public Vector3 Position, Scale;
        public Quaternion Rotation;
        public bool Active;
    }

    [Serializable]
    public sealed class HintReplayFrame
    {
        public float Time;
        public HintReplayPose[] Poses;
        public bool[] Visible;
        public bool Connected;
        public Vector3 ConnectionFrom, ConnectionTo;
        public float Horizontal;
        public bool SpaceHeld, SlowHeld;
    }
}
