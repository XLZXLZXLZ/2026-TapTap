using UnityEngine;

namespace TapTap
{
    [CreateAssetMenu(menuName = "TapTap/Player Visual Configuration")]
    public sealed class PlayerVisualConfig : ScriptableObject
    {
        [HideInInspector] public Sprite DotSprite;
        [HideInInspector] public Material EffectsMaterial;
        [HideInInspector] public Material ParticleMaterial;
        [HideInInspector] public Color MagneticColor = new Color(0.52f, 0.85f, 1f, 0.65f);
        [HideInInspector] [Min(0.002f)] public float MagneticWidth = 0.025f;
        [HideInInspector] [Min(0f)] public float MagneticAmplitude = 0.075f;
        [HideInInspector] [Min(0f)] public float MagneticSpeed = 2.1f;
        [HideInInspector] [Range(3, 64)] public int MagneticSegments = 18;
        [HideInInspector] [Range(1, 12)] public int MagneticDotCount = 5;
        [HideInInspector] [Min(0.01f)] public float MagneticDotSize = 0.065f;
        [HideInInspector] [Range(1, 24)] public int LandingParticles = 7;
        [HideInInspector] [Range(1, 24)] public int AssemblyParticles = 10;
        [HideInInspector] [Min(0.02f)] public float ParticleLifetime = 0.28f;
        [HideInInspector] [Min(0.01f)] public float ParticleSize = 0.055f;
        [HideInInspector] [Min(0.01f)] public float ParticleSpeed = 2.1f;
        [HideInInspector] [Range(1f, 85f)] public float ParticleConeAngle = 38f;
        [Range(0f, 0.4f)] public float LandingSquash = 0.12f;
        [Range(0f, 0.4f)] public float AssemblySquash = 0.16f;
        [Range(0f, 0.4f)] public float MotionStretch = 0.05f;
        [Min(0.02f)] public float InteractionDuration = 0.22f;
        [Header("Breathing")]
        [Range(0f, 0.03f)] public float BreathingAmplitude = 0.008f;
        [Tooltip("Breathing cycles per second.")]
        [Min(0f)] public float BreathingFrequency = 0.6f;
        [Header("Magnetic movement")]
        [Tooltip("Maximum visual lag behind movement, in grid units.")]
        [Range(0f, 0.2f)] public float MagneticLagDistance = 0.075f;
        [Min(0.01f)] public float MagneticLagSmoothTime = 0.1f;
        [Range(0f, 10f)] public float MagneticLeanAngle = 5f;
        [Range(0f, 1f)] public float MagneticHeadLeanMultiplier = 0.75f;
        [HideInInspector] [Min(0.01f)] public float RespawnDotSize = 0.13f;
        [HideInInspector] [Min(0.01f)] public float RespawnTrailTime = 0.18f;
    }
}
