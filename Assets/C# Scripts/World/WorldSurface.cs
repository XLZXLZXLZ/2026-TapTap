using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    public enum SurfaceKind { Solid, OneWay }

    [DisallowMultipleComponent]
    public sealed class WorldSurface : MonoBehaviour
    {
        private static readonly List<WorldSurface> activeSurfaces = new List<WorldSurface>();
        public static IReadOnlyList<WorldSurface> ActiveSurfaces => activeSurfaces;
        public SurfaceKind Kind = SurfaceKind.Solid;
        public float ConveyorSpeed;

        private void OnEnable() { if (!activeSurfaces.Contains(this)) activeSurfaces.Add(this); }
        private void OnDisable() => activeSurfaces.Remove(this);

        public void Configure(SurfaceKind kind, float speed = 0f)
        {
            Kind = kind;
            ConveyorSpeed = speed;
        }
    }
}
