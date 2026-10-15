using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScreenFilters
{
    /// <summary>Shared GPU execution for camera rendering, image previews and validation.</summary>
    public sealed class ScreenFilterRenderer : IDisposable
    {
        public const string ShaderResource = "ScreenFilters/ScreenFilters";
        private static readonly int BufferA = Shader.PropertyToID("_ScreenFilterBufferA");
        private static readonly int BufferB = Shader.PropertyToID("_ScreenFilterBufferB");
        private readonly Material[] materials = new Material[5];
        private Shader shader;

        public bool IsSupported
        {
            get
            {
                if (shader == null) shader = Resources.Load<Shader>(ShaderResource);
                return shader != null && shader.isSupported;
            }
        }

        public bool Record(CommandBuffer commands, RenderTargetIdentifier source,
            RenderTargetIdentifier destination, RenderTextureDescriptor descriptor,
            ScreenFilterProfile profile, float clock, int? seedOverride = null)
        {
            if (profile == null || !profile.HasActiveFilters || !IsSupported)
            {
                if (!source.Equals(destination)) commands.Blit(source, destination);
                return false;
            }
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            commands.GetTemporaryRT(BufferA, descriptor, FilterMode.Bilinear);
            commands.GetTemporaryRT(BufferB, descriptor, FilterMode.Bilinear);
            var current = new RenderTargetIdentifier(BufferA);
            var next = new RenderTargetIdentifier(BufferB);
            commands.Blit(source, current);
            int seen = 0;
            foreach (ScreenFilterKind kind in profile.Order)
            {
                int index = (int)kind;
                if (index < 0 || index >= 5 || (seen & (1 << index)) != 0 || !profile.IsActive(kind)) continue;
                seen |= 1 << index;
                Material material = materials[index];
                if (material == null) materials[index] = material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                SetParameters(material, profile, kind, clock, seedOverride ?? profile.Seed);
                commands.Blit(current, next, material, index);
                var swap = current; current = next; next = swap;
            }
            commands.Blit(current, destination);
            commands.ReleaseTemporaryRT(BufferA);
            commands.ReleaseTemporaryRT(BufferB);
            return true;
        }

        private static void SetParameters(Material material, ScreenFilterProfile profile,
            ScreenFilterKind kind, float clock, int seed)
        {
            material.SetFloat("_Strength", Mathf.Clamp01(profile.GetSettings(kind).Strength));
            material.SetFloat("_FilterClock", Mathf.Max(0f, clock));
            material.SetFloat("_Seed", seed % 65536);
            switch (kind)
            {
                case ScreenFilterKind.Pixelation:
                    material.SetFloat("_PixelSize", Mathf.Clamp(profile.Pixelation.Size, 1, 64));
                    break;
                case ScreenFilterKind.Brush:
                    BrushSettings b = profile.Brush;
                    material.SetVector("_Brush", new Vector4(b.BlockSize, b.ColorLevels, b.EdgePreservation, b.EdgeThreshold));
                    material.SetVector("_BrushMotion", new Vector4(b.Disturbance, b.MotionSpeed, 0f, 0f));
                    break;
                case ScreenFilterKind.Tear:
                    TearSettings t = profile.Tear;
                    material.SetVector("_Tear", new Vector4(t.Offset, t.BandHeight, Mathf.Clamp(t.BandCount, 1, 8), t.Frequency));
                    material.SetFloat("_Duty", t.Duty);
                    break;
                case ScreenFilterKind.Chromatic:
                    ChromaticSettings c = profile.Chromatic;
                    float radians = c.Angle * Mathf.Deg2Rad;
                    material.SetVector("_Chromatic", new Vector4(c.Offset, c.EdgeOnly, c.Jitter, c.Frequency));
                    material.SetVector("_Direction", new Vector4(Mathf.Cos(radians), Mathf.Sin(radians), 0f, 0f));
                    break;
                case ScreenFilterKind.CRT:
                    CRTSettings crt = profile.CRT;
                    material.SetVector("_CRT", new Vector4(crt.Scanlines, crt.ScanlineSpacing, crt.Mask, crt.MaskSize));
                    material.SetVector("_CRTShape", new Vector4(crt.Curvature, crt.Vignette, crt.Flicker, crt.Brightness));
                    break;
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(materials[i]);
                else UnityEngine.Object.DestroyImmediate(materials[i]);
                materials[i] = null;
            }
        }
    }
}
