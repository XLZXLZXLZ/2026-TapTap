using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScreenFilters
{
    public enum ScreenFilterKind { Pixelation, Brush, Tear, Chromatic, CRT }

    [Serializable]
    public abstract class FilterSettings
    {
        public bool Enabled;
        [Range(0f, 1f)] public float Strength = 1f;
        public bool IsActive => Enabled && Strength > 0f;
    }

    [Serializable]
    public sealed class PixelSettings : FilterSettings
    {
        [Range(1, 64)] public int Size = 4;
    }

    [Serializable]
    public sealed class BrushSettings : FilterSettings
    {
        [Range(2f, 48f)] public float BlockSize = 8f;
        [Range(2, 64)] public int ColorLevels = 16;
        [Range(0f, 1f)] public float EdgePreservation = 0.85f;
        [Range(0.01f, 1f)] public float EdgeThreshold = 0.15f;
        [Range(0f, 4f)] public float Disturbance = 0.65f;
        [Range(0f, 2f)] public float MotionSpeed = 0.2f;
    }

    [Serializable]
    public sealed class TearSettings : FilterSettings
    {
        [Range(0f, 100f)] public float Offset = 24f;
        [Range(0.005f, 0.25f)] public float BandHeight = 0.045f;
        [Range(1, 8)] public int BandCount = 3;
        [Range(0f, 20f)] public float Frequency = 3f;
        [Range(0f, 1f)] public float Duty = 0.35f;
    }

    [Serializable]
    public sealed class ChromaticSettings : FilterSettings
    {
        [Range(0f, 30f)] public float Offset = 3f;
        [Range(-180f, 180f)] public float Angle;
        [Range(0f, 1f)] public float EdgeOnly = 0.6f;
        [Range(0f, 1f)] public float Jitter = 0.25f;
        [Range(0f, 30f)] public float Frequency = 8f;
    }

    [Serializable]
    public sealed class CRTSettings : FilterSettings
    {
        [Range(0f, 1f)] public float Scanlines = 0.18f;
        [Range(1f, 8f)] public float ScanlineSpacing = 3f;
        [Range(0f, 1f)] public float Mask = 0.12f;
        [Range(1f, 6f)] public float MaskSize = 1f;
        [Range(0f, 0.3f)] public float Curvature = 0.035f;
        [Range(0f, 1f)] public float Vignette = 0.2f;
        [Range(0f, 0.15f)] public float Flicker = 0.015f;
        [Range(0.5f, 2f)] public float Brightness = 1.12f;
    }

    [CreateAssetMenu(menuName = "Rendering/屏幕滤镜配置", fileName = "ScreenFilters")]
    public sealed class ScreenFilterProfile : ScriptableObject
    {
        public bool Enabled = true;
        [Min(0)] public int Seed = 17;
        public List<ScreenFilterKind> Order = new List<ScreenFilterKind>
        {
            ScreenFilterKind.Brush, ScreenFilterKind.Pixelation, ScreenFilterKind.Tear,
            ScreenFilterKind.Chromatic, ScreenFilterKind.CRT
        };
        public PixelSettings Pixelation = new PixelSettings();
        public BrushSettings Brush = new BrushSettings();
        public TearSettings Tear = new TearSettings();
        public ChromaticSettings Chromatic = new ChromaticSettings();
        public CRTSettings CRT = new CRTSettings();

        public FilterSettings GetSettings(ScreenFilterKind kind)
        {
            switch (kind)
            {
                case ScreenFilterKind.Pixelation: return Pixelation;
                case ScreenFilterKind.Brush: return Brush;
                case ScreenFilterKind.Tear: return Tear;
                case ScreenFilterKind.Chromatic: return Chromatic;
                case ScreenFilterKind.CRT: return CRT;
                default: return null;
            }
        }

        public bool IsActive(ScreenFilterKind kind) => Enabled && GetSettings(kind)?.IsActive == true
            && (kind != ScreenFilterKind.Pixelation || Pixelation.Size > 1);

        public bool HasActiveFilters
        {
            get
            {
                if (!Enabled || Order == null) return false;
                foreach (ScreenFilterKind kind in Order) if (IsActive(kind)) return true;
                return false;
            }
        }

        // This is also called after loading a JSON preset or copying a profile in the editor.
        public void Normalize()
        {
            if (Order == null) Order = new List<ScreenFilterKind>();
            int seen = 0;
            for (int i = 0; i < Order.Count;)
            {
                int value = (int)Order[i];
                if (value < 0 || value > 4 || (seen & (1 << value)) != 0) Order.RemoveAt(i);
                else { seen |= 1 << value; i++; }
            }
            for (int i = 0; i < 5; i++) if ((seen & (1 << i)) == 0) Order.Add((ScreenFilterKind)i);
            if (Pixelation == null) Pixelation = new PixelSettings();
            if (Brush == null) Brush = new BrushSettings();
            if (Tear == null) Tear = new TearSettings();
            if (Chromatic == null) Chromatic = new ChromaticSettings();
            if (CRT == null) CRT = new CRTSettings();
            Pixelation.Size = Mathf.Clamp(Pixelation.Size, 1, 64);
            Seed = Mathf.Max(0, Seed);
        }

        private void OnValidate() => Normalize();
    }
}
