using UnityEngine;

namespace TapTap
{
    public sealed class EffectManager : LazySingleton<EffectManager>
    {
        [Header("Shake strength (world units)")]
        [SerializeField, Min(0f)] private float headLandedShakeStrength = 0.07f;
        [SerializeField, Min(0f)] private float pullBlockedShakeStrength = 0.07f;
        [SerializeField, Min(0f)] private float deathShakeStrength = 0.1f;
        [SerializeField, Min(0f)] private float headReturnShakeStrength = 0.035f;
        [SerializeField, Min(0f)] private float landingShakeStrength = 0.045f;
        [Header("Shake motion")]
        [SerializeField, Min(0.001f)] private float shakeDuration = 0.12f;
        [Tooltip("Oscillation frequency in cycles per second.")]
        [SerializeField, Min(0f)] private Vector2 shakeFrequency = new Vector2(14.16f, 18.62f);
        [Tooltip("Relative horizontal and vertical displacement.")]
        [SerializeField] private Vector2 shakeAxisWeight = Vector2.one;
        [SerializeField, Range(0.1f, 4f)] private float shakeDecayPower = 1f;
        [Header("Assembly zoom")]
        [SerializeField, Min(0f)] private float zoomAmount = 0.3f;
        [SerializeField, Min(0.001f)] private float zoomInDuration = 0.07f;
        [SerializeField, Min(0.001f)] private float zoomOutDuration = 0.18f;
        private float shakeRemaining;
        private float shakeStrength;
        private float activeShakeDuration;
        private float shakeClock;
        private float zoomPulse;
        private float zoomStart;
        private float zoomElapsed;
        private bool zoomActive;
        private float slowRemaining;
        private float previousTimeScale = 1f;
        public float ZoomPulse => zoomPulse;
        public float ZoomAmount => Mathf.Max(0f, zoomAmount);
        public float ZoomProgress => zoomAmount > 0f ? Mathf.Clamp01(zoomPulse / zoomAmount) : 0f;
        public Vector2 ShakeOffset => shakeRemaining > 0f
            ? Vector2.Scale(new Vector2(
                Mathf.Sin(shakeClock * shakeFrequency.x * 2f * Mathf.PI + 0.7f),
                Mathf.Sin(shakeClock * shakeFrequency.y * 2f * Mathf.PI + 1.3f)), shakeAxisWeight)
                * shakeStrength * Mathf.Pow(Mathf.Clamp01(shakeRemaining / activeShakeDuration), shakeDecayPower)
            : Vector2.zero;

        public void Play(PlayerEffect effect, Transform visual)
        {
            if (effect == PlayerEffect.HeadLanded) Shake(headLandedShakeStrength);
            if (effect == PlayerEffect.PullBlocked) Shake(pullBlockedShakeStrength);
            if (effect == PlayerEffect.HeadReturned) Shake(headReturnShakeStrength);
            if (effect == PlayerEffect.Landed) Shake(landingShakeStrength);
            if (effect == PlayerEffect.Death) Shake(deathShakeStrength);
            if (effect == PlayerEffect.Joined || effect == PlayerEffect.DownwardJoined)
            {
                zoomStart = zoomPulse;
                zoomElapsed = 0f;
                zoomActive = true;
                if (slowRemaining <= 0f) previousTimeScale = Time.timeScale;
                slowRemaining = 0.07f;
                Time.timeScale = previousTimeScale * 0.75f;
            }
        }

        private void Shake(float strength)
        {
            if (strength <= 0f) return;
            bool active = shakeRemaining > 0f;
            // Preserve the current envelope when a weaker event arrives; never sum amplitudes.
            float currentStrength = active ? shakeStrength *
                Mathf.Pow(Mathf.Clamp01(shakeRemaining / activeShakeDuration), shakeDecayPower) : 0f;
            shakeStrength = Mathf.Max(currentStrength, strength);
            if (!active) shakeClock = 0f;
            activeShakeDuration = Mathf.Max(0.001f, shakeDuration);
            shakeRemaining = activeShakeDuration;
        }

        private void Update() => Simulate(Time.unscaledDeltaTime);

        public void Simulate(float unscaledDt)
        {
            if (unscaledDt <= 0f) return;
            shakeClock += unscaledDt;
            shakeRemaining = Mathf.Max(0f, shakeRemaining - unscaledDt);
            if (zoomActive)
            {
                zoomElapsed += unscaledDt;
                float attack = Mathf.Max(0.001f, zoomInDuration);
                float recovery = Mathf.Max(0.001f, zoomOutDuration);
                zoomPulse = zoomElapsed <= attack
                    ? Mathf.SmoothStep(zoomStart, zoomAmount, zoomElapsed / attack)
                    : Mathf.SmoothStep(zoomAmount, 0f, (zoomElapsed - attack) / recovery);
                if (zoomElapsed >= attack + recovery)
                {
                    zoomPulse = 0f;
                    zoomActive = false;
                }
            }
            if (slowRemaining <= 0f) return;
            slowRemaining -= unscaledDt;
            if (slowRemaining <= 0f) Time.timeScale = previousTimeScale;
        }

        protected override void OnDestroy()
        {
            if (slowRemaining > 0f) Time.timeScale = previousTimeScale;
            base.OnDestroy();
        }
    }
}
