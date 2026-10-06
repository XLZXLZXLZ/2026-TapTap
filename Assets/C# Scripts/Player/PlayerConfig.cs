using UnityEngine;

namespace TapTap
{
    [CreateAssetMenu(menuName = "TapTap/Player Configuration")]
    public sealed class PlayerConfig : ScriptableObject
    {
        [Min(0.01f)] public float UnitSize = 1f;
        [Min(0.1f)] public float Gravity = 24f;
        [Min(0f)] public float MoveSpeed = 5f;
        [Min(0.1f)] public float LandingShakeSpeed = 5f;
        [Min(0.1f)] public float JoinedOffset = 0.75f;
        [Min(0.1f)] public float MaxExtension = 4.4f;
        [Min(0.02f)] public float ExtensionDuration = 0.45f;
        [Min(0.02f)] public float RetractionDuration = 0.35f;
        [Min(0f)] public float NormalLaunchHeight = 1f;
        [Min(0f)] public float DistantLaunchHeight = 2f;
        [Min(0f)] public float DistantThreshold = 5f;
        [Min(0f)] public float SpringHeight = 3f;
        [Min(0.01f)] public float AlignmentTolerance = 0.35f;
        [Min(0.02f)] public float AlignmentDuration = 0.18f;
        [Min(0.1f)] public float PullStartSpeed = 1f;
        [Min(0.1f)] public float PullMaxSpeed = 12f;
        [Min(0.02f)] public float PullAccelerationTime = 0.45f;
        [Min(0f)] public float RecallCooldown = 0.22f;
        [Min(0.02f)] public float RespawnDuration = 0.45f;
        [Min(0.01f)] public float DeathShakeDuration = 0.08f;
        [Min(0.01f)] public float RespawnCollapseDuration = 0.18f;
        [Min(0.01f)] public float RespawnDotDuration = 0.1f;
        [Min(0.01f)] public float RespawnRebuildDuration = 0.2f;
        [Min(0.001f)] public float JoinTolerance = 0.015f;
        [Min(0.001f)] public float Skin = 0.01f;
        public float DeathBoundary = -15f;

        public float ToWorld(float units) => units * UnitSize;
        public float WorldGravity => Gravity * UnitSize;
        public float LaunchSpeed(float height) =>
            Mathf.Sqrt(2f * WorldGravity * ToWorld(height));
    }
}
