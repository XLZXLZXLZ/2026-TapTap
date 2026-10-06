using UnityEngine;

namespace TapTap
{
    public enum SurfaceKind { Solid, OneWay }

    [DisallowMultipleComponent]
    public sealed class WorldSurface : MonoBehaviour
    {
        public SurfaceKind Kind = SurfaceKind.Solid;
        public float ConveyorSpeed;

        public void Configure(SurfaceKind kind, float speed = 0f)
        {
            Kind = kind;
            ConveyorSpeed = speed;
        }
    }
}
