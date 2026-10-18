using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    // Discovery policy stays in WorldMapProgression. Override ApplyVisibility to add
    // another presentation later; no material replacement is needed by the default.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(500)]
    public class RegionRevealView : MonoBehaviour
    {
        [Tooltip("首次进入区域时，探索遮罩渐退的时长。设为 0 则立即显示。")]
        [Min(0f)] public float RevealDuration = 0.4f;

        private readonly List<Renderer> renderers = new List<Renderer>();
        private readonly Dictionary<Renderer, bool> originalHidden = new Dictionary<Renderer, bool>();
        private readonly Dictionary<Light, bool> hiddenLights = new Dictionary<Light, bool>();
        private readonly Dictionary<Object, float> visibilityOverrides = new Dictionary<Object, float>();
        private float discoveryOpacity, revealStart, revealElapsed;
        private bool revealing;
        // Models must already render behind the mask on the first (fully covered) frame.
        private bool VisuallyActive => Discovered || VisibleOpacity > 0f;
        public float VisibleOpacity
        {
            get
            {
                float value = discoveryOpacity;
                foreach (var entry in visibilityOverrides)
                    if (entry.Key != null) value = Mathf.Max(value, entry.Value);
                return value;
            }
        }

        public void SetVisibilityOverride(Object owner, float opacity)
        {
            if (owner == null) return;
            bool wasVisible = VisuallyActive;
            if (opacity <= 0f) visibilityOverrides.Remove(owner);
            else visibilityOverrides[owner] = Mathf.Clamp01(opacity);
            bool visible = VisuallyActive;
            if (visible != wasVisible) { DiscoverRenderers(); ApplyVisibility(visible, true); }
        }
        protected IReadOnlyList<Renderer> Renderers => renderers;
        // Shared with the exploration mask; these are the live styled models, not copies.
        public IReadOnlyList<Renderer> SceneRenderers => renderers;
        public bool Discovered { get; private set; }
        public float Opacity { get; protected set; } = 1f;

        public void SetDiscovered(bool value, bool immediate = false)
        {
            DiscoverRenderers();
            bool wasDiscovered = Discovered;
            Discovered = value;
            if (!value || immediate || !Application.isPlaying || RevealDuration <= 0f)
            {
                discoveryOpacity = value ? 1f : 0f;
                revealing = false;
            }
            else if (!wasDiscovered)
            {
                revealStart = discoveryOpacity;
                revealElapsed = 0f;
                revealing = discoveryOpacity < 1f;
            }
            ApplyVisibility(VisuallyActive, immediate);
        }

        // Match camera blending: unaffected by bullet time, frozen while the game is paused.
        private void Update() => AdvanceReveal(Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f);

        private void AdvanceReveal(float deltaTime)
        {
            if (!revealing || deltaTime <= 0f) return;
            revealElapsed += deltaTime;
            float t = RevealDuration > 0f ? Mathf.Clamp01(revealElapsed / RevealDuration) : 1f;
            discoveryOpacity = Mathf.SmoothStep(revealStart, 1f, t);
            if (t >= 1f) revealing = false;
        }

        // Call after spawning visuals inside an already initialized region.
        public void RefreshVisuals()
        {
            DiscoverRenderers();
            ApplyVisibility(VisuallyActive, true);
        }

        private void DiscoverRenderers()
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (originalHidden.ContainsKey(renderer)) continue;
                originalHidden.Add(renderer, renderer.forceRenderingOff);
                renderers.Add(renderer);
            }
        }

        protected virtual void ApplyVisibility(bool visible, bool immediate)
        {
            Opacity = visible ? 1f : 0f;
            foreach (Renderer renderer in renderers)
                if (renderer != null) renderer.forceRenderingOff = !visible || originalHidden[renderer];
            foreach (Light light in GetComponentsInChildren<Light>(true))
            {
                if (!visible)
                {
                    if (!hiddenLights.ContainsKey(light)) hiddenLights.Add(light, light.enabled);
                    light.enabled = false;
                }
                else if (hiddenLights.TryGetValue(light, out bool wasEnabled))
                {
                    light.enabled = wasEnabled;
                    hiddenLights.Remove(light);
                }
            }
            WorldPhaseState.Existing?.RefreshOutline();
        }

        protected virtual void OnDestroy()
        {
            foreach (Renderer renderer in renderers)
                if (renderer != null) renderer.forceRenderingOff = originalHidden[renderer];
            foreach (var entry in hiddenLights)
                if (entry.Key != null) entry.Key.enabled = entry.Value;
            hiddenLights.Clear();
        }
    }
}
