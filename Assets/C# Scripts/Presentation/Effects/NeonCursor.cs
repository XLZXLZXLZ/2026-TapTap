using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TapTap
{
    // Screen-space UI keeps the cursor sharp and independent of camera post-processing.
    [DefaultExecutionOrder(10000)]
    public sealed class NeonCursor : MaskableGraphic
    {
        public const float TrailLifetime = 0.1f;
        private static NeonCursor instance;
        private readonly List<Sample> samples = new List<Sample>(128);
        private static readonly Color GlowColor = Color.white;
        private Texture2D glowTexture;
        private Vector2 pointer;
        private bool showing;
        private bool previousCursorVisible;
        private Vector2 previousScreenSize;

        private struct Sample
        {
            public Vector2 Position;
            public float Time;
            public Sample(Vector2 position, float time) { Position = position; Time = time; }
        }

        public override Texture mainTexture => glowTexture != null ? glowTexture : Texture2D.whiteTexture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        // 暂停使用荧光鼠标；恢复下面的自动初始化标记即可重新启用。
        // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (instance != null) return;
            // Reuse the persistent object when entering play with scene reload disabled.
            instance = FindObjectOfType<NeonCursor>();
            if (instance != null) return;
            var root = new GameObject("Neon Cursor", typeof(RectTransform), typeof(Canvas));
            DontDestroyOnLoad(root);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767;
            var graphic = new GameObject("Glow", typeof(RectTransform), typeof(CanvasRenderer));
            graphic.transform.SetParent(root.transform, false);
            instance = graphic.AddComponent<NeonCursor>();
            instance.rectTransform.anchorMin = Vector2.zero;
            instance.rectTransform.anchorMax = Vector2.one;
            instance.rectTransform.offsetMin = instance.rectTransform.offsetMax = Vector2.zero;
            instance.rectTransform.pivot = Vector2.zero;
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            maskable = false;
            const int size = 64;
            glowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Cursor Soft Glow",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float radius = (new Vector2(x, y) - Vector2.one * ((size - 1) * 0.5f)).magnitude / (size * 0.5f);
                float alpha = Mathf.Exp(-radius * radius * 5f) * (1f - Mathf.SmoothStep(0.7f, 1f, radius));
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
            glowTexture.SetPixels(pixels);
            glowTexture.Apply(false, true);
            SetMaterialDirty();
        }

        private void LateUpdate()
        {
            Vector2 screen = new Vector2(Screen.width, Screen.height);
            Vector2 mouse = Input.mousePosition;
            bool visible = Application.isFocused && Input.mousePresent
                && Cursor.lockState != CursorLockMode.Locked
                && mouse.x >= 0f && mouse.y >= 0f && mouse.x < screen.x && mouse.y < screen.y;
            if (!visible)
            {
                ReleaseCursor();
                return;
            }

            if (!showing)
            {
                previousCursorVisible = Cursor.visible;
                showing = true;
            }
            Cursor.visible = false;
            if (screen != previousScreenSize) samples.Clear();
            previousScreenSize = screen;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, mouse, null, out pointer);
            float now = Time.unscaledTime;
            float cutoff = now - TrailLifetime;
            // Keep one sample before the cutoff to interpolate the exact tail endpoint.
            while (samples.Count > 1 && samples[1].Time <= cutoff) samples.RemoveAt(0);
            if (samples.Count == 1 && samples[0].Time <= cutoff) samples.Clear();
            if (samples.Count > 1 && samples[0].Time < cutoff)
            {
                Sample a = samples[0], b = samples[1];
                samples[0] = new Sample(Vector2.Lerp(a.Position, b.Position,
                    Mathf.InverseLerp(a.Time, b.Time, cutoff)), cutoff);
            }
            // Bound geometry even on unusually high refresh-rate displays.
            if (samples.Count >= 128) samples.RemoveAt(0);
            samples.Add(new Sample(pointer, now));
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!showing) return;
            float length = 0f;
            for (int i = 1; i < samples.Count; i++)
                length += Vector2.Distance(samples[i - 1].Position, samples[i].Position);
            // Soft overlapping stamps form a continuous trail, capped at ~512 stamps.
            float spacing = Mathf.Max(2f, length / 512f);
            float distanceToNext = 0f;
            for (int i = 1; i < samples.Count; i++)
            {
                Sample a = samples[i - 1], b = samples[i];
                float distance = Vector2.Distance(a.Position, b.Position);
                if (distance < 0.001f) continue;
                for (; distanceToNext < distance; distanceToNext += spacing)
                {
                    float t = distanceToNext / distance;
                    float life = Mathf.Clamp01(1f - (Time.unscaledTime - Mathf.Lerp(a.Time, b.Time, t)) / TrailLifetime);
                    Color tint = GlowColor;
                    tint.a = life * life * 0.6f;
                    Stamp(vh, Vector2.Lerp(a.Position, b.Position, t), Mathf.Lerp(8f, 28f, life), tint);
                }
                distanceToNext -= distance;
            }
            Stamp(vh, pointer, 64f, new Color(1f, 1f, 1f, 0.65f));
            Stamp(vh, pointer, 32f, GlowColor);
            Stamp(vh, pointer, 18f, Color.white);
        }

        private static void Stamp(VertexHelper vh, Vector2 center, float diameter, Color tint)
        {
            float r = diameter * 0.5f;
            int start = vh.currentVertCount;
            vh.AddVert(center + new Vector2(-r, -r), tint, new Vector2(0f, 0f));
            vh.AddVert(center + new Vector2(-r, r), tint, new Vector2(0f, 1f));
            vh.AddVert(center + new Vector2(r, r), tint, new Vector2(1f, 1f));
            vh.AddVert(center + new Vector2(r, -r), tint, new Vector2(1f, 0f));
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }

        private void ReleaseCursor()
        {
            if (!showing) return;
            showing = false;
            samples.Clear();
            Cursor.visible = previousCursorVisible;
            SetVerticesDirty();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) ReleaseCursor();
        }

        protected override void OnDisable()
        {
            ReleaseCursor();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            ReleaseCursor();
            if (instance == this) instance = null;
            if (glowTexture != null) Destroy(glowTexture);
            base.OnDestroy();
        }
    }
}
