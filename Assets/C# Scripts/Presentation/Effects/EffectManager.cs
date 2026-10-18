using System.Collections;
using UnityEngine;

namespace TapTap
{
    public sealed class EffectManager : LazySingleton<EffectManager>
    {
        [Header("Shake strength (world units)")]
        [SerializeField, Min(0f)] private float headLandedShakeStrength = 0.07f;
        [SerializeField, Min(0f)] private float pullBlockedShakeStrength = 0.07f;
        [Tooltip("头部伸出上升受阻时的轻微震屏强度。")]
        [SerializeField, Min(0f)] private float extensionBlockedShakeStrength = 0.035f;
        [SerializeField, Min(0f)] private float deathShakeStrength = 0.1f;
        [SerializeField, Min(0f)] private float headReturnShakeStrength = 0.035f;
        [SerializeField, Min(0f)] private float landingShakeStrength = 0.045f;
        [Tooltip("远距离吸附成功合体瞬间的轻微震屏强度。")]
        [SerializeField, Min(0f)] private float distantJoinShakeStrength = 0.035f;
        [Header("Shake motion")]
        [SerializeField, Min(0.001f)] private float shakeDuration = 0.12f;
        [Tooltip("Oscillation frequency in cycles per second.")]
        [SerializeField, Min(0f)] private Vector2 shakeFrequency = new Vector2(14.16f, 18.62f);
        [Tooltip("Relative horizontal and vertical displacement.")]
        [SerializeField] private Vector2 shakeAxisWeight = Vector2.one;
        [SerializeField, Range(0.1f, 4f)] private float shakeDecayPower = 1f;
        [Header("Assembly zoom")]
        [Tooltip("合体时视野缩小的比例；0.035 对应原有默认幅度，0 可关闭缩放。")]
        [SerializeField, Range(0f, 0.9f)] private float zoomAmount = 0.035f;
        [SerializeField, Min(0.001f)] private float zoomInDuration = 0.07f;
        [SerializeField, Min(0.001f)] private float zoomOutDuration = 0.18f;
        [Header("合体顿帧（真实秒数）")]
        [SerializeField, InspectorName("近距离/落下合体速度比例"), Range(0.01f, 1f)] private float joinTimeScale = 0.5f;
        [SerializeField, InspectorName("近距离/落下合体顿帧时长"), Min(0f)] private float joinHitDuration = 0.09f;
        [Tooltip("按吸附开始时的远距离判定使用强顿帧，与跃起高度无关。")]
        [SerializeField, InspectorName("远距离合体速度比例"), Range(0.01f, 1f)] private float launchJoinTimeScale = 0.25f;
        [SerializeField, InspectorName("远距离合体顿帧时长"), Min(0f)] private float launchJoinHitDuration = 0.16f;
        [SerializeField, Min(1f)] private float launchJoinZoomDurationMultiplier = 1.8f;
        [Header("黑屏与画面淡入淡出")]
        [Tooltip("画面变为全黑所需的时间（秒）。不受游戏慢动作或暂停影响。")]
        [SerializeField, Min(0f)] private float fadeOutDuration = 0.2f;
        [Tooltip("从黑屏恢复画面所需的时间（秒）。场景加载完成后才开始恢复。")]
        [SerializeField, Min(0f)] private float fadeInDuration = 0.25f;
        [Header("回溯：画面过渡")]
        [SerializeField] private bool rewindVisualEnabled = true;
        [SerializeField] private Shader rewindShader;
        [SerializeField, Min(0f)] private float rewindFadeIn = 0.15f;
        [SerializeField, Min(0f)] private float rewindFadeOut = 0.2f;
        [Header("回溯：信号质感")]
        [SerializeField, Range(0f, 1f)] private float rewindSaturation = 0.72f;
        [SerializeField, Range(0f, 0.2f)] private float rewindColdTint = 0.12f;
        [Tooltip("以 1080p 为基准的横向撕裂像素偏移。")]
        [SerializeField, Min(0f)] private float rewindTearPixels = 16f;
        [SerializeField, Min(0f)] private float rewindTearFrequency = 2.8f;
        [SerializeField, Range(0.005f, 0.15f)] private float rewindBandHeight = 0.035f;
        [SerializeField, Range(0.05f, 0.5f)] private float rewindTearDuty = 0.3f;
        [SerializeField, Min(0f)] private float rewindChromaticPixels = 2.25f;
        [SerializeField, Range(0f, 0.15f)] private float rewindScanlines = 0.035f;
        [SerializeField, Range(0f, 0.05f)] private float rewindGrain = 0.006f;
        [SerializeField, Range(0f, 0.3f)] private float rewindVignette = 0.16f;
        [Tooltip("达到最大回溯速度时，动态干扰的增幅。")]
        [SerializeField, Range(0f, 1f)] private float rewindSpeedBoost = 0.5f;
        [Header("回溯：时间倒流")]
        [Tooltip("最近两次画面采样产生的青色、紫色时间残影。仅突出移动和变化的区域。")]
        [SerializeField, Range(0f, 0.5f)] private float rewindGhostStrength = 0.24f;
        [Tooltip("残影采样间隔（真实秒数）；两张半分辨率画面只在回溯期间保留。")]
        [SerializeField, Range(0.02f, 0.15f)] private float rewindGhostInterval = 0.055f;
        [SerializeField, Range(0f, 0.4f)] private float rewindSweepStrength = 0.18f;
        [Tooltip("倒扫亮带每秒移动的屏幕高度。随回溯加速提升。")]
        [SerializeField, Min(0f)] private float rewindSweepSpeed = 0.45f;
        [SerializeField, Range(0.02f, 0.25f)] private float rewindSweepWidth = 0.12f;
        [Tooltip("从屏幕边缘向内收拢的波纹幅度（屏幕 UV 比例）。中心区域保持清晰。")]
        [SerializeField, Range(0f, 0.02f)] private float rewindEdgeRipple = 0.002f;
        [Tooltip("边缘波纹的变化速度；越小越平缓。")]
        [SerializeField, Min(0f)] private float rewindEdgeRippleSpeed = 4f;
        [SerializeField, Min(0f)] private float rewindEdgeChromaticPixels = 2f;
        [Tooltip("屏幕上下角落向左流动的倒带箭纹强度。")]
        [SerializeField, Range(0f, 0.4f)] private float rewindChevronStrength = 0.2f;
        private float shakeRemaining;
        private float shakeStrength;
        private float activeShakeDuration;
        private float shakeClock;
        private float zoomPulse;
        private float zoomStart;
        private float zoomElapsed;
        private float activeZoomDurationMultiplier = 1f;
        private bool zoomActive;
        private float slowRemaining;
        private float activeJoinTimeScale = 1f;
        private float previousTimeScale = 1f;
        private bool progressEmphasisActive;
        private float progressSlowScale = 1f;
        private bool rewindVisualActive;
        private bool rewindAtOldest;
        private float rewindBlend;
        private float rewindMotion;
        private float rewindSpeedProgress;
        private float rewindClock;
        private float rewindSignalClock;
        private float rewindFlowClock;
        public bool RewindVisualActive => rewindVisualActive && rewindVisualEnabled;
        public int RewindVisualSession { get; private set; }
        public float RewindPresentationClock => rewindClock;
        public float RewindGhostStrength => rewindGhostStrength;
        public float RewindGhostInterval => Mathf.Max(0.02f, rewindGhostInterval);
        public float RewindVisualStrength => rewindVisualEnabled ? rewindBlend : 0f;
        public float RewindMotionStrength => rewindMotion;
        public Shader RewindShader
        {
            get
            {
                // Resources also keeps the shader in builds when the manager is created at runtime.
                if (rewindShader == null) rewindShader = Resources.Load<Shader>("Shaders/RewindSignal");
                return rewindShader;
            }
        }
        private static readonly int RewindParamsId = Shader.PropertyToID("_RewindParams");
        private static readonly int SignalParamsId = Shader.PropertyToID("_SignalParams");
        private static readonly int DetailParamsId = Shader.PropertyToID("_DetailParams");
        private static readonly int ClockParamsId = Shader.PropertyToID("_ClockParams");
        private static readonly int TearDutyId = Shader.PropertyToID("_TearDuty");
        private static readonly int TimeParamsId = Shader.PropertyToID("_TimeParams");
        private static readonly int FlowParamsId = Shader.PropertyToID("_FlowParams");
        public float ZoomPulse => zoomPulse;
        public float ZoomAmount => Mathf.Clamp(zoomAmount, 0f, 0.9f);
        public float ZoomProgress => ZoomAmount > 0f ? Mathf.Clamp01(zoomPulse / ZoomAmount) : 0f;
        public void BlackScreen() => ScreenFadeOverlay.Instance.SetOpacity(1f);
        public void ClearBlackScreen() => ScreenFadeOverlay.Instance.SetOpacity(0f);
        public IEnumerator FadeToBlack() => ScreenFadeOverlay.Instance.FadeTo(1f, fadeOutDuration);
        public IEnumerator FadeFromBlack() => ScreenFadeOverlay.Instance.FadeTo(0f, fadeInDuration);
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
            if (effect == PlayerEffect.ExtensionBlocked) Shake(extensionBlockedShakeStrength);
            if (effect == PlayerEffect.HeadReturned) Shake(headReturnShakeStrength);
            if (effect == PlayerEffect.Landed) Shake(landingShakeStrength);
            if (effect == PlayerEffect.Death) Shake(deathShakeStrength);
            if (effect == PlayerEffect.DistantJoined) Shake(distantJoinShakeStrength);
            if (effect == PlayerEffect.Joined || effect == PlayerEffect.DownwardJoined || effect == PlayerEffect.DistantJoined)
            {
                // Distance is captured before gameplay clears the completed pull state.
                // Launch height can be tuned independently and must not choose the feedback.
                bool launching = effect == PlayerEffect.DistantJoined;
                zoomStart = zoomPulse;
                zoomElapsed = 0f;
                activeZoomDurationMultiplier = launching ? Mathf.Max(1f, launchJoinZoomDurationMultiplier) : 1f;
                zoomActive = true;
                if (slowRemaining <= 0f) previousTimeScale = Time.timeScale;
                float relativeSpeed = Mathf.Clamp(launching ? launchJoinTimeScale : joinTimeScale, 0.01f, 1f);
                activeJoinTimeScale = slowRemaining > 0f ? Mathf.Min(activeJoinTimeScale, relativeSpeed) : relativeSpeed;
                slowRemaining = Mathf.Max(slowRemaining, Mathf.Max(0f, launching ? launchJoinHitDuration : joinHitDuration));
                Time.timeScale = slowRemaining > 0f ? previousTimeScale * activeJoinTimeScale : previousTimeScale;
            }
        }

        public void ShakeCamera(float strength) => Shake(strength);

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
            SimulateRewindVisual(unscaledDt);
            if (PauseMenu.IsOpen || RewindManager.Rewinding) return;
            shakeClock += unscaledDt;
            shakeRemaining = Mathf.Max(0f, shakeRemaining - unscaledDt);
            if (zoomActive)
            {
                zoomElapsed += unscaledDt;
                float attack = Mathf.Max(0.001f, zoomInDuration) * activeZoomDurationMultiplier;
                float recovery = Mathf.Max(0.001f, zoomOutDuration) * activeZoomDurationMultiplier;
                zoomPulse = zoomElapsed <= attack
                    ? Mathf.SmoothStep(zoomStart, ZoomAmount, zoomElapsed / attack)
                    : Mathf.SmoothStep(ZoomAmount, 0f, (zoomElapsed - attack) / recovery);
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

        public void SetRewindVisual(bool active, float speedProgress = 0f, bool atOldest = false)
        {
            if (active && !rewindVisualActive) RewindVisualSession++;
            rewindVisualActive = active;
            if (!active) return;
            rewindSpeedProgress = Mathf.Clamp01(speedProgress);
            rewindAtOldest = atOldest;
        }

        private void SimulateRewindVisual(float dt)
        {
            bool active = rewindVisualActive && rewindVisualEnabled;
            float duration = active ? rewindFadeIn : rewindFadeOut;
            rewindBlend = duration > 0f ? Mathf.MoveTowards(rewindBlend, active ? 1f : 0f, dt / duration)
                : (active ? 1f : 0f);
            float motionTarget = active ? (rewindAtOldest ? 0.15f : 1f) : 0f;
            rewindMotion = Mathf.MoveTowards(rewindMotion, motionTarget, dt / 0.2f);
            if (rewindBlend <= 0f) return;
            // These clocks belong to the presentation and keep advancing while world time goes backwards.
            rewindClock = Mathf.Repeat(rewindClock + dt, 3600f);
            rewindSignalClock = Mathf.Repeat(rewindSignalClock
                + dt * rewindTearFrequency * (1f + rewindSpeedProgress * rewindSpeedBoost), 4096f);
            rewindFlowClock = Mathf.Repeat(rewindFlowClock
                + dt * rewindMotion * (1f + rewindSpeedProgress * rewindSpeedBoost), 3600f);
        }

        public void ApplyRewindMaterial(Material material)
        {
            float blend = Mathf.SmoothStep(0f, 1f, RewindVisualStrength);
            float motion = rewindMotion * (1f + rewindSpeedProgress * rewindSpeedBoost);
            material.SetVector(RewindParamsId, new Vector4(blend, rewindSaturation, rewindColdTint, rewindVignette));
            material.SetVector(SignalParamsId, new Vector4(rewindTearPixels, rewindBandHeight, rewindChromaticPixels, motion));
            material.SetVector(DetailParamsId, new Vector4(rewindScanlines, rewindGrain, 0f, 0f));
            material.SetVector(ClockParamsId, new Vector4(rewindClock, rewindSignalClock, rewindEdgeRippleSpeed, 0f));
            material.SetFloat(TearDutyId, rewindTearDuty);
            material.SetVector(TimeParamsId, new Vector4(rewindGhostStrength * motion,
                rewindSweepStrength, rewindSweepWidth, rewindEdgeRipple));
            material.SetVector(FlowParamsId, new Vector4(rewindFlowClock,
                rewindSweepSpeed, rewindChevronStrength, rewindEdgeChromaticPixels));
        }

        public void ClearTransientEffects()
        {
            EndProgressEmphasis();
            if (slowRemaining > 0f) Time.timeScale = previousTimeScale;
            slowRemaining = shakeRemaining = shakeStrength = zoomPulse = zoomElapsed = 0f;
            activeJoinTimeScale = activeZoomDurationMultiplier = 1f;
            zoomActive = false;
        }

        public void SetGameplayTimeScale(float speed)
        {
            if (progressEmphasisActive)
            {
                previousTimeScale = speed;
                Time.timeScale = speed * progressSlowScale;
                return;
            }
            // Preserve the join effect's relative slowdown while updating its eventual resume speed.
            if (slowRemaining > 0f)
            {
                previousTimeScale = speed;
                Time.timeScale = speed * activeJoinTimeScale;
            }
            else Time.timeScale = speed;
        }

        public void BeginProgressEmphasis(float relativeSpeed)
        {
            ClearTransientEffects();
            previousTimeScale = Time.timeScale;
            progressSlowScale = Mathf.Clamp01(relativeSpeed);
            progressEmphasisActive = true;
            Time.timeScale = previousTimeScale * progressSlowScale;
        }

        public void EndProgressEmphasis()
        {
            if (!progressEmphasisActive) return;
            progressEmphasisActive = false;
            Time.timeScale = previousTimeScale;
        }

        protected override void OnDestroy()
        {
            EndProgressEmphasis();
            if (slowRemaining > 0f) Time.timeScale = previousTimeScale;
            base.OnDestroy();
        }
    }
}
