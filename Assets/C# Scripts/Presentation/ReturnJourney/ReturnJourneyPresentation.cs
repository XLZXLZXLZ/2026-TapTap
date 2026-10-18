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
        [Range(0f, 0.3f)] public float Darkness = 0.16f;
        [Min(0f)] public float FadeDuration = 0.65f;
        [Range(0f, 20f)] public float TearPixels = 2.5f;
        [Range(0f, 8f)] public float ChromaticPixels = 0.7f;
        [Header("前两次警告：各闪暗两次")]
        [Range(0f, 0.5f)] public float WarningDarkness = 0.2f;
        [Min(0.01f)] public float FlashDuration = 0.24f;
        [Min(0f)] public float FlashGap = 0.24f;
        public Shader SignalShader;
        private static readonly List<ReturnJourneyPresentation> active = new List<ReturnJourneyPresentation>();
        private Material material;
        private Coroutine warning;
        private float blend, target, flash, clock;
        public float Blend => blend;
        public int RenderCount { get; private set; }
        public bool HasActiveEffect => isActiveAndEnabled && (blend > 0f || flash > 0f);

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
                float elapsed = 0f;
                while (elapsed < FlashDuration)
                {
                    if (Time.timeScale > 0f) elapsed += Time.unscaledDeltaTime;
                    flash = Mathf.Sin(Mathf.Clamp01(elapsed / Mathf.Max(0.01f, FlashDuration)) * Mathf.PI) * WarningDarkness;
                    yield return null;
                }
                flash = 0f;
                elapsed = 0f;
                while (elapsed < FlashGap)
                {
                    if (Time.timeScale > 0f) elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            warning = null;
        }
        public void Render(CommandBuffer commands, RenderTargetIdentifier source, RenderTargetIdentifier destination)
        {
            RenderCount++;
            if (SignalShader == null) SignalShader = Resources.Load<Shader>("Shaders/ReturnJourneySignal");
            if (SignalShader == null || !SignalShader.isSupported) { commands.Blit(source, destination); return; }
            if (material == null) material = new Material(SignalShader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetVector("_JourneyParams", new Vector4(Mathf.Max(flash, Darkness * blend), TearPixels * blend, ChromaticPixels * blend, clock));
            commands.Blit(source, destination, material);
        }
        private void OnDisable()
        {
            active.Remove(this);
            EventManager.Unsubscribe<GameStageChanged>(StageChanged);
            if (warning != null) StopCoroutine(warning);
            warning = null; flash = 0f;
            if (material != null) Destroy(material);
            material = null;
        }
    }
}
