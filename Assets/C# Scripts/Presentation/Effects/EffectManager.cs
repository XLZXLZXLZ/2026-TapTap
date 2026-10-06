using DG.Tweening;
using UnityEngine;

namespace TapTap
{
    public sealed class EffectManager : LazySingleton<EffectManager>
    {
        [SerializeField, Min(0f)] private float headReturnShakeStrength = 0.035f;
        [SerializeField, Min(0f)] private float landingShakeStrength = 0.045f;
        [SerializeField, Min(0f)] private float zoomAmount = 0.3f;
        [SerializeField, Min(0.001f)] private float zoomInDuration = 0.07f;
        [SerializeField, Min(0.001f)] private float zoomOutDuration = 0.18f;
        private float shakeRemaining;
        private float shakeStrength;
        private float zoomPulse;
        private float zoomStart;
        private float zoomElapsed;
        private bool zoomActive;
        private float slowRemaining;
        private float previousTimeScale = 1f;
        public float ZoomPulse => zoomPulse;
        public Vector2 ShakeOffset => shakeRemaining > 0f
            ? new Vector2(Mathf.Sin(Time.unscaledTime * 89f), Mathf.Sin(Time.unscaledTime * 117f))
                * shakeStrength * Mathf.Clamp01(shakeRemaining / 0.12f)
            : Vector2.zero;

        public void Play(PlayerEffect effect, Transform visual)
        {
            if (effect == PlayerEffect.HeadLanded || effect == PlayerEffect.PullBlocked)
                Shake(0.07f);
            if (effect == PlayerEffect.HeadReturned) Shake(headReturnShakeStrength);
            if (effect == PlayerEffect.Landed) Shake(landingShakeStrength);
            if (effect == PlayerEffect.Death) Shake(0.1f);
            if (effect == PlayerEffect.Joined || effect == PlayerEffect.DownwardJoined)
            {
                zoomStart = zoomPulse;
                zoomElapsed = 0f;
                zoomActive = true;
                if (slowRemaining <= 0f) previousTimeScale = Time.timeScale;
                slowRemaining = 0.07f;
                Time.timeScale = previousTimeScale * 0.75f;
            }
            if (visual == null) return;
            if (effect == PlayerEffect.Joined || effect == PlayerEffect.DownwardJoined || effect == PlayerEffect.Bounce)
            {
                visual.DOKill(true);
                visual.DOPunchScale(new Vector3(0.12f, -0.12f, 0f), 0.2f, 4, 0.4f);
            }
        }

        private void Shake(float strength)
        {
            shakeStrength = shakeRemaining > 0f ? Mathf.Max(shakeStrength, strength) : strength;
            shakeRemaining = 0.12f;
        }

        private void Update() => Simulate(Time.unscaledDeltaTime);

        public void Simulate(float unscaledDt)
        {
            if (unscaledDt <= 0f) return;
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
