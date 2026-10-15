using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    [Serializable]
    public sealed class ConveyorArrowMaterialSlot
    {
        public Renderer Renderer;
        [Min(0)] public int MaterialIndex;
    }

    // One component represents one arrow sign (including all of its chevrons).
    [DisallowMultipleComponent, DefaultExecutionOrder(100)]
    public sealed class ConveyorArrowPulse : MonoBehaviour
    {
        [Tooltip("Only these material slots flash. Body and other submeshes keep their original materials.")]
        public ConveyorArrowMaterialSlot[] ArrowMaterials = Array.Empty<ConveyorArrowMaterialSlot>();
        public Material GlowMaterial;
        [Min(0.01f)] public float Period = 1.5f;
        [Min(0f)] public float FlashDuration = 0.6f;
        [SerializeField] private bool facingLeft;
        [SerializeField] private bool moving = true;
        private readonly List<Binding> bindings = new List<Binding>();
        private bool prepared;
        private Material preparedGlow;
        public bool IsLit { get; private set; }
        public float GlowAmount { get; private set; }
        public bool FacingLeft => facingLeft;
        public static double Clock => RewindManager.DrivesSimulation ? RewindManager.Current.SimulationTime : Time.timeAsDouble;

        private sealed class Binding
        {
            public Renderer Renderer;
            public Material[] Original;
            public Material[] Glowing;
            public readonly List<FadeMaterial> Fades = new List<FadeMaterial>();
        }

        private sealed class FadeMaterial
        {
            public Material Instance;
            public Color OriginalColor;
            public Color OriginalEmission;
        }

        public static double PhaseOffset(double worldX, bool left, double period)
        {
            if (!Finite(worldX) || !Finite(period) || period <= 0d) return 0d;
            return Remainder((left ? -0.2d : 0.2d) * worldX, period);
        }

        public static bool FlashAt(double time, double worldX, bool left, double period, double duration)
        {
            if (!Finite(time) || !Finite(worldX) || !Finite(period) || !Finite(duration) || period <= 0d || duration <= 0d) return false;
            return Remainder(time - PhaseOffset(worldX, left, period), period) < Math.Min(duration, period);
        }

        public static float IntensityAt(double time, double worldX, bool left, double period, double duration)
        {
            if (!FlashAt(time, worldX, left, period, duration)) return 0f;
            double progress = Remainder(time - PhaseOffset(worldX, left, period), period) / Math.Min(duration, period);
            // Smoothly rise for one third, hold for one third, then smoothly fade out.
            return Mathf.SmoothStep(0f, 1f, (float)(Math.Min(progress, 1d - progress) * 3d));
        }

        private static double Remainder(double value, double period)
        {
            double result = value % period;
            return result < 0d ? result + period : result;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public void Configure(Material glow, float period, float duration, bool left, bool isMoving)
        {
            GlowMaterial = glow; Period = period; FlashDuration = duration;
            facingLeft = left; moving = isMoving;
            RefreshAt(Clock);
        }

        private void OnEnable() => RefreshAt(Clock);
        private void LateUpdate() => RefreshAt(Clock);
        private void OnDisable() => RestoreMaterials();
        private void OnDestroy() => ReleaseMaterials();

        public void RefreshAt(double time)
        {
            if (!Application.isPlaying) { RestoreMaterials(); return; }
            if (!prepared || preparedGlow != GlowMaterial) PrepareMaterials();
            float amount = isActiveAndEnabled && moving && GlowMaterial != null ?
                IntensityAt(time, transform.position.x, facingLeft, Period, FlashDuration) : 0f;
            if (Mathf.Approximately(amount, GlowAmount)) return;
            bool lit = amount > 0f;
            foreach (Binding binding in bindings)
            {
                if (binding.Renderer == null) continue;
                if (lit)
                    foreach (FadeMaterial fade in binding.Fades)
                    {
                        fade.Instance.SetColor("_BaseColor", Color.Lerp(fade.OriginalColor, BaseColor(GlowMaterial), amount));
                        fade.Instance.SetColor("_EmissionColor", Color.Lerp(fade.OriginalEmission, Emission(GlowMaterial), amount));
                    }
                if (lit != IsLit) binding.Renderer.sharedMaterials = lit ? binding.Glowing : binding.Original;
            }
            GlowAmount = amount; IsLit = lit;
        }

        private void PrepareMaterials()
        {
            ReleaseMaterials();
            var byRenderer = new Dictionary<Renderer, Binding>();
            foreach (ConveyorArrowMaterialSlot slot in ArrowMaterials ?? Array.Empty<ConveyorArrowMaterialSlot>())
            {
                if (slot?.Renderer == null) continue;
                if (!byRenderer.TryGetValue(slot.Renderer, out Binding binding))
                {
                    Material[] original = slot.Renderer.sharedMaterials;
                    binding = new Binding { Renderer = slot.Renderer, Original = original, Glowing = (Material[])original.Clone() };
                    byRenderer.Add(slot.Renderer, binding); bindings.Add(binding);
                }
                if (slot.MaterialIndex < 0 || slot.MaterialIndex >= binding.Glowing.Length || GlowMaterial == null ||
                    binding.Glowing[slot.MaterialIndex] != binding.Original[slot.MaterialIndex]) continue;
                Material originalMaterial = binding.Original[slot.MaterialIndex];
                var fade = new FadeMaterial
                {
                    Instance = new Material(GlowMaterial) { name = "Conveyor Arrow Fade", hideFlags = HideFlags.HideAndDontSave },
                    OriginalColor = BaseColor(originalMaterial), OriginalEmission = Emission(originalMaterial)
                };
                binding.Fades.Add(fade);
                binding.Glowing[slot.MaterialIndex] = fade.Instance;
            }
            preparedGlow = GlowMaterial; prepared = true;
        }

        private void RestoreMaterials()
        {
            foreach (Binding binding in bindings)
                if (binding.Renderer != null) binding.Renderer.sharedMaterials = binding.Original;
            GlowAmount = 0f; IsLit = false;
        }

        private static Color BaseColor(Material material) => material == null ? Color.white :
            material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") :
            material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;

        private static Color Emission(Material material) => material != null && material.HasProperty("_EmissionColor") &&
            material.IsKeywordEnabled("_EMISSION") ? material.GetColor("_EmissionColor") : Color.black;

        private void ReleaseMaterials()
        {
            RestoreMaterials();
            foreach (Binding binding in bindings)
                foreach (FadeMaterial fade in binding.Fades)
                    if (fade.Instance != null)
                    {
                        if (Application.isPlaying) Destroy(fade.Instance);
                        else DestroyImmediate(fade.Instance);
                    }
            bindings.Clear(); prepared = false; preparedGlow = null;
        }
    }
}
