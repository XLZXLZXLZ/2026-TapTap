using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    // Only render state changes here: colliders, scripts and rewind participants stay resident.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(500)]
    public sealed class RegionRevealView : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float fadeDuration = 0.45f;
        private static readonly int OpacityId = Shader.PropertyToID("_RegionOpacity");
        private sealed class Visual
        {
            public Renderer Renderer;
            public Material[] Originals, Fading;
            public bool OriginallyHidden;
        }
        private readonly List<Visual> visuals = new List<Visual>();
        private readonly Dictionary<Material, Material> fadeMaterials = new Dictionary<Material, Material>();
        private MaterialPropertyBlock properties;
        private bool initialized;
        public bool Discovered { get; private set; }
        public float Opacity { get; private set; } = 1f;

        public void SetDiscovered(bool value, bool immediate = false)
        {
            Initialize();
            Discovered = value;
            if (immediate || !value || fadeDuration <= 0f) Opacity = value ? 1f : 0f;
            Apply();
        }

        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            properties = new MaterialPropertyBlock();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                visuals.Add(new Visual { Renderer = renderer, Originals = renderer.sharedMaterials,
                    OriginallyHidden = renderer.forceRenderingOff });
        }

        private void LateUpdate() => UpdateFade(Time.deltaTime);

        private void UpdateFade(float deltaTime)
        {
            if (!Discovered || Opacity >= 1f) return;
            Opacity = fadeDuration <= 0f ? 1f : Mathf.MoveTowards(Opacity, 1f, Mathf.Max(0f, deltaTime) / fadeDuration);
            Apply();
        }

        private void Apply()
        {
            foreach (Visual visual in visuals)
            {
                Renderer renderer = visual.Renderer;
                if (renderer == null) continue;
                renderer.forceRenderingOff = visual.OriginallyHidden || Opacity <= 0f;
                if (Opacity <= 0f || Opacity >= 1f)
                {
                    if (visual.Fading != null) renderer.sharedMaterials = visual.Originals;
                    continue;
                }
                if (visual.Fading == null)
                {
                    Shader shader = Resources.Load<Shader>("Shaders/RegionReveal");
                    visual.Fading = new Material[visual.Originals.Length];
                    for (int i = 0; i < visual.Fading.Length; i++)
                    {
                        Material original = visual.Originals[i];
                        if (original == null) continue;
                        // A temporary unlit pass preserves texture, material tint and live vertex/sprite colors.
                        if (!fadeMaterials.TryGetValue(original, out Material fading))
                        {
                            fading = new Material(original) { shader = shader, name = original.name + " (region fade)" };
                            fadeMaterials.Add(original, fading);
                        }
                        visual.Fading[i] = fading;
                    }
                }
                renderer.sharedMaterials = visual.Fading;
                renderer.GetPropertyBlock(properties);
                properties.SetFloat(OpacityId, Opacity);
                renderer.SetPropertyBlock(properties);
            }
            WorldPhaseState.Existing?.RefreshOutline();
        }

        private void OnDestroy()
        {
            foreach (Visual visual in visuals)
            {
                if (visual.Renderer != null)
                {
                    visual.Renderer.sharedMaterials = visual.Originals;
                    visual.Renderer.forceRenderingOff = visual.OriginallyHidden;
                }
            }
            foreach (Material material in fadeMaterials.Values)
                if (material != null)
                {
                    if (Application.isPlaying) Destroy(material);
                    else DestroyImmediate(material);
                }
        }
    }
}
