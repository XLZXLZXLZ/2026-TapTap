using UnityEngine;

namespace TapTap
{
    public enum EntityPart { Body, Head, Other }

    public struct MotionSegment
    {
        public Vector2 From;
        public Vector2 To;

        public MotionSegment(Vector2 from, Vector2 to)
        {
            From = from;
            To = to;
        }
    }

    public struct SweepResult
    {
        public float Fraction;
        public bool Blocked;
        public RaycastHit2D Hit;
        public Vector2 Normal;
        public MovableEntity Entity;
        public WorldSurface Surface;

        public static SweepResult Clear => new SweepResult { Fraction = 1f };
    }

    public struct MoveResult
    {
        public Vector2 ActualDelta;
        public bool Blocked;
        public RaycastHit2D Hit;
        public Vector2 Normal;
        public MovableEntity Entity;
        public WorldSurface Surface;

        public MoveResult(Vector2 actualDelta, SweepResult sweep)
        {
            ActualDelta = actualDelta;
            Blocked = sweep.Blocked;
            Hit = sweep.Hit;
            Normal = sweep.Normal;
            Entity = sweep.Entity;
            Surface = sweep.Surface;
        }
    }
}
