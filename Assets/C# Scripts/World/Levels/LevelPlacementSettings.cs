using System;
using UnityEngine;

namespace TapTap
{
    [Serializable]
    public abstract class LevelPlacementSettings
    {
    }

    [Serializable]
    public sealed class EmptyPlacementSettings : LevelPlacementSettings
    {
    }

    [Serializable]
    public sealed class ConveyorPlacementSettings : LevelPlacementSettings
    {
        public float Speed = 1.25f;
    }

    [Serializable]
    public sealed class CheckpointPlacementSettings : LevelPlacementSettings
    {
        public Vector2 SpawnOffset = new Vector2(0f, 0.4f);
    }
}
