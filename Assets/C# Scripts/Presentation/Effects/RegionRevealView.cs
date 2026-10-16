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
        private readonly List<Renderer> renderers = new List<Renderer>();
        private readonly Dictionary<Renderer, bool> originalHidden = new Dictionary<Renderer, bool>();
        private readonly Dictionary<Light, bool> hiddenLights = new Dictionary<Light, bool>();
        private readonly Dictionary<Object, float> visibilityOverrides = new Dictionary<Object, float>();
        public float VisibleOpacity
        {
            get
            {
                float value = Discovered ? 1f : 0f;
                foreach (var entry in visibilityOverrides)
                    if (entry.Key != null) value = Mathf.Max(value, entry.Value);
                return value;
            }
        }

        public void SetVisibilityOverride(Object owner, float opacity)
        {
            if (owner == null) return;
            bool wasVisible = VisibleOpacity > 0f;
            if (opacity <= 0f) visibilityOverrides.Remove(owner);
            else visibilityOverrides[owner] = Mathf.Clamp01(opacity);
            bool visible = VisibleOpacity > 0f;
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
            Discovered = value;
            ApplyVisibility(VisibleOpacity > 0f, immediate);
        }

        // Call after spawning visuals inside an already initialized region.
        public void RefreshVisuals()
        {
            DiscoverRenderers();
            ApplyVisibility(VisibleOpacity > 0f, true);
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
