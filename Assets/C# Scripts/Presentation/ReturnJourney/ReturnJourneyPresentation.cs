using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TapTap
{
    [DisallowMultipleComponent]
    public sealed class ReturnJourneyPresentation : MonoBehaviour
    {
        public Camera TargetCamera;
        [Header("返程氛围")]
        [Range(0f, 0.5f)] public float Darkness = 0.22f;
        [Min(0f)] public float FadeDuration = 0.25f;
        [Range(0f, 20f)] public float TearPixels = 7f;
        [Tooltip("每秒短促撕裂的次数；两次之间恢复稳定画面。")]
        [Min(0.01f)] public float TearFrequency = 2f;
        [Min(0f)] public float TearDuration = 0.08f;
        [Tooltip("色彩错位基准像素数；平时轻微，撕裂瞬间加强。")]
        [Range(0f, 8f)] public float ChromaticPixels = 2.5f;
        [Header("返程色彩与暗角")]
        [Range(0f, 1f)] public float Saturation = 0.85f;
        [Range(0f, 0.5f)] public float VignetteStrength = 0.18f;
        [Tooltip("暗角从画面中心向外的起始半径，中心为 0，边缘中点为 1。")]
        [Range(0f, 0.95f)] public float VignetteStart = 0.55f;
        [Header("前两次警告：各闪暗两次")]
        [Range(0f, 0.5f)] public float WarningDarkness = 0.2f;
        [Min(0.01f)] public float FlashDuration = 0.24f;
        [Min(0f)] public float FlashGap = 0.24f;
        [Header("第三次：压暗过头后回弹")]
        [Range(0f, 1f)] public float EntryPeakDarkness = 0.62f;
        [Min(0.01f)] public float EntryDarkenDuration = 0.18f;
        [Min(0f)] public float EntryHoldDuration = 0.08f;
        [Min(0.01f)] public float EntryRecoveryDuration = 0.32f;
        public Shader SignalShader;
        private static readonly List<ReturnJourneyPresentation> active = new List<ReturnJourneyPresentation>();
        private Material material;
        private Coroutine warning, entry;
        private float blend, target, flash, entryDarkness, clock;
        public float Blend => blend;
        public bool IsWarningPlaying => warning != null;
        public bool IsEntryPlaying => entry != null;
        public float EffectiveDarkness => Mathf.Max(flash, Mathf.Max(Darkness * blend, entryDarkness));
        public int RenderCount { get; private set; }
        public bool HasActiveEffect => isActiveAndEnabled && (blend > 0f || flash > 0f || entryDarkness > 0f);

        public static ReturnJourneyPresentation InScene(Scene scene)
        {
            foreach (var view in active) if (view != null && view.gameObject.scene == scene) return view;
            return null;
        }
        public static ReturnJourneyPresentation ForCamera(Camera camera)
        {
            foreach (var view in active)
            {
                if (view == null || !view.HasActiveEffect || view.gameObject.scene != camera.gameObject.scene) continue;
                view.FindCamera();
                if (view.TargetCamera == camera) return view;
            }
            return null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => active.Clear();

        private void OnEnable()
        {
            active.Add(this);
            EventManager.Subscribe<GameStageChanged>(StageChanged);
            target = blend = GameStageProgression.HasProgressedIn(gameObject.scene) ? 1f : 0f;
        }
        private void FindCamera()
        {
            if (TargetCamera != null) return;
            foreach (DemoCamera rig in FindObjectsOfType<DemoCamera>())
                if (rig.gameObject.scene == gameObject.scene) { TargetCamera = rig.ViewCamera; break; }
        }
        private void StageChanged(GameStageChanged message)
        {
            if (message.Scene != gameObject.scene || message.Stage < GameStage.Returning) return;
            if (warning != null) StopCoroutine(warning);
            warning = null;
            flash = 0f;
            target = 1f;
            if (message.Previous == GameStage.Outbound && entry == null) entry = StartCoroutine(ReturnEntry());
        }
        private void Update()
        {
            if (Time.timeScale <= 0f || PauseMenu.IsOpen) return;
            clock = Mathf.Repeat(clock + Time.unscaledDeltaTime, 3600f);
            blend = Mathf.MoveTowards(blend, target, FadeDuration > 0f ? Time.unscaledDeltaTime / FadeDuration : 1f);
        }
        public void PlayWarning()
        {
            if (target > 0f) return;
            if (warning != null) StopCoroutine(warning);
            warning = StartCoroutine(Warning());
        }
        private IEnumerator Warning()
        {
            for (int i = 0; i < 2; i++)
            {
                while (Time.timeScale <= 0f || PauseMenu.IsOpen) yield return null;
                AudioManager.Instance?.Play(SoundId.PowerFlicker, owner: this);
                float elapsed = 0f;
                while (elapsed < FlashDuration)
                {
                    if (Time.timeScale > 0f) elapsed += Time.unscaledDeltaTime;
                    flash = Mathf.Sin(Mathf.Clamp01(elapsed / Mathf.Max(0.01f, FlashDuration)) * Mathf.PI) * WarningDarkness;
                    yield return null;
                }
                flash = 0f;
                if (i == 1) break;
                elapsed = 0f;
                while (elapsed < FlashGap)
                {
                    if (Time.timeScale > 0f) elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            warning = null;
        }
        private IEnumerator ReturnEntry()
        {
            AudioManager.Instance?.Play(SoundId.PowerDown, owner: this);
            float peak = Mathf.Max(Darkness, EntryPeakDarkness);
            yield return FadeEntry(EffectiveDarkness, peak, EntryDarkenDuration);
            float elapsed = 0f;
            while (elapsed < EntryHoldDuration)
            {
                if (Time.timeScale > 0f && !PauseMenu.IsOpen) elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return FadeEntry(peak, Darkness, EntryRecoveryDuration);
            entryDarkness = 0f;
            entry = null;
        }
        private IEnumerator FadeEntry(float from, float to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < Mathf.Max(0.01f, duration))
            {
                if (Time.timeScale > 0f && !PauseMenu.IsOpen) elapsed += Time.unscaledDeltaTime;
                entryDarkness = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration))));
                yield return null;
            }
        }
        public void Render(CommandBuffer commands, RenderTargetIdentifier source, RenderTargetIdentifier destination)
        {
            RenderCount++;
            if (SignalShader == null) SignalShader = Resources.Load<Shader>("Shaders/ReturnJourneySignal");
            if (SignalShader == null || !SignalShader.isSupported) { commands.Blit(source, destination); return; }
            if (material == null) material = new Material(SignalShader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetVector("_JourneyParams", new Vector4(EffectiveDarkness, TearPixels * blend, ChromaticPixels * blend, clock));
            material.SetVector("_JourneyTearTiming", new Vector4(TearFrequency, TearDuration, 0f, 0f));
            material.SetVector("_JourneyColor", new Vector4(Mathf.Lerp(1f, Saturation, blend), VignetteStrength * blend, VignetteStart, 0f));
            commands.Blit(source, destination, material);
        }
        private void OnDisable()
        {
            active.Remove(this);
            EventManager.Unsubscribe<GameStageChanged>(StageChanged);
            if (warning != null) StopCoroutine(warning);
            if (entry != null) StopCoroutine(entry);
            warning = entry = null; flash = entryDarkness = 0f;
            if (material != null) Destroy(material);
            material = null;
        }
    }
}
