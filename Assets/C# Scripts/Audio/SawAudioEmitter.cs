using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    // Put on the animated saw, not on every decorative cog or generic lethal zone.
    [DisallowMultipleComponent]
    public sealed class SawAudioEmitter : MonoBehaviour
    {
        private static readonly List<SawAudioEmitter> active = new List<SawAudioEmitter>();
        public static IReadOnlyList<SawAudioEmitter> ActiveEmitters => active;
        [Min(0f)] public float FullVolumeRadius = 1.5f;
        [Min(0.1f)] public float ListeningRadius = 6f;
        public Collider2D SourceCollider;
        public Animator MotorAnimation;
        private RegionRevealView region;

        private void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
            if (SourceCollider == null) SourceCollider = GetComponentInParent<LethalZone>()?.GetComponent<Collider2D>();
            if (MotorAnimation == null) MotorAnimation = GetComponent<Animator>();
            region = GetComponentInParent<RegionRevealView>();
        }
        private void OnDisable() => active.Remove(this);

        public float GainAt(Vector2 body, float unit, out Vector2 point)
        {
            if (region == null) region = GetComponentInParent<RegionRevealView>();
            point = transform.position;
            if (!isActiveAndEnabled || SourceCollider != null && !SourceCollider.enabled
                || MotorAnimation != null && (!MotorAnimation.isActiveAndEnabled || Mathf.Approximately(MotorAnimation.speed, 0f))
                || region != null && !region.Discovered && region.VisibleOpacity <= 0.01f) return 0f;
            if (SourceCollider != null) point = SourceCollider.ClosestPoint(body);
            float distance = Vector2.Distance(body, point) / Mathf.Max(0.01f, unit);
            return 1f-Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FullVolumeRadius,
                Mathf.Max(FullVolumeRadius+0.01f, ListeningRadius), distance));
        }
    }
}
