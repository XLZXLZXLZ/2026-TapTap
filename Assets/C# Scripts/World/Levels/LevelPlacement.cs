using System;
using UnityEngine;

namespace TapTap
{
    [Serializable]
    public sealed class LevelPlacement
    {
        public string Id = Guid.NewGuid().ToString("N");
        public Vector2Int Cell;
        public LevelBrush Brush;
        [SerializeReference] public LevelPlacementSettings Settings = new EmptyPlacementSettings();
    }
}
