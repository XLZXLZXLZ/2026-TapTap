using System;
using UnityEngine;

namespace TapTap
{
    [Serializable]
    public abstract class LevelPlacementSettings
    {
    }

    [Serializable]
    public sealed class AnnotationPlacementSettings : LevelPlacementSettings
    {
        [TextArea] public string Text = "";
    }

    [Serializable]
    public sealed class SpringPlacementSettings : LevelPlacementSettings
    {
        public float Height = 3f;
        public float AnimationDuration = 0.28f;
    }

    [Serializable]
    public sealed class EmptyPlacementSettings : LevelPlacementSettings
    {
    }

    [Serializable]
    public sealed class ConveyorPlacementSettings : LevelPlacementSettings
    {
        public float Speed = 1.25f;
        public bool FacingLeftWhenStopped;
        public bool FacingLeft => Speed < 0f || Speed == 0f && FacingLeftWhenStopped;
    }

    [Serializable]
    public sealed class SpikePlacementSettings : LevelPlacementSettings
    {
        public int QuarterTurns;
    }

    [Serializable]
    public sealed class CheckpointPlacementSettings : LevelPlacementSettings
    {
        public Vector2 SpawnOffset = new Vector2(0f, 0.45f);
    }

    [Serializable]
    public sealed class PhaseBlockPlacementSettings : LevelPlacementSettings
    {
        public bool SolidWhenActive = true;
    }

    [Serializable]
    public sealed class EndpointPlacementSettings : LevelPlacementSettings
    {
        public string EndpointId = "finish";
        public string Label = "终点";
    }
}
