using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ScreenFilters.Editor
{
    /// <summary>Disposable preview sources; no dependency on a gameplay scene or runtime objects.</summary>
    internal sealed class FilterPreviewSource : System.IDisposable
    {
        private Camera camera;
        private Texture2D chart;

        internal Texture2D Chart
        {
            get
            {
                if (chart != null) return chart;
                const int width = 960, height = 600;
                chart = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                    { name = "滤镜测试画面", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                var pixels = new Color[width * height];
                Color[] palette = { new Color(0.9f, 0.2f, 0.13f), new Color(0.95f, 0.7f, 0.2f),
                    new Color(0.18f, 0.65f, 0.44f), new Color(0.15f, 0.5f, 0.85f), new Color(0.64f, 0.28f, 0.72f) };
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width, v = (y + 0.5f) / height;
                    Color color = Color.Lerp(new Color(0.025f, 0.04f, 0.08f), new Color(0.32f, 0.4f, 0.48f), v);
                    if (x % 48 < 1 || y % 48 < 1) color += new Color(0.09f, 0.09f, 0.09f, 0f);
                    if (v < 0.17f) color = Color.Lerp(Color.black, Color.white, u);
                    if (v > 0.79f) color = palette[Mathf.Min(4, (int)(u * 5))] * Mathf.Lerp(0.45f, 1f, (v - 0.79f) / 0.21f);
                    for (int i = 0; i < 5; i++)
                    {
                        float cx = 0.12f + i * 0.19f;
                        Vector2 local = new Vector2((u - cx) * 1.6f, v - 0.52f);
                        if (local.magnitude < 0.105f)
                        {
                            float lighting = Mathf.Clamp01(0.6f - local.x * 3f + local.y * 3f);
                            color = palette[i] * Mathf.Lerp(0.3f, 1.1f, lighting);
                        }
                        if (Mathf.Abs(u - cx) < 0.065f && v > 0.22f && v < 0.34f)
                            color = ((x / 3) % 2 == 0) ? palette[i] : Color.white;
                    }
                    if (Mathf.Abs(v - 0.35f - u * 0.31f) < 0.002f) color = Color.white;
                    color.a = 1f;
                    pixels[y * width + x] = color;
                }
                chart.SetPixels(pixels); chart.Apply(false, false);
                return chart;
            }
        }

        internal void RenderScene(Camera source, RenderTexture target)
        {
            if (source == null) { Graphics.Blit(Chart, target); return; }
            if (camera == null)
            {
                var owner = new GameObject("Screen Filter Preview Camera") { hideFlags = HideFlags.HideAndDontSave };
                camera = owner.AddComponent<Camera>();
                camera.enabled = false;
                owner.AddComponent<UniversalAdditionalCameraData>();
            }
            camera.CopyFrom(source);
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            camera.targetTexture = target;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.aspect = (float)target.width / target.height;
            camera.allowMSAA = false;
            var data = camera.GetUniversalAdditionalCameraData();
            var original = source.GetComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = original != null && original.renderPostProcessing;
            data.renderShadows = original == null || original.renderShadows;
            if (original != null)
            {
                var serialized = new SerializedObject(original);
                data.SetRenderer(serialized.FindProperty("m_RendererIndex").intValue);
                data.volumeLayerMask = original.volumeLayerMask;
                data.volumeTrigger = original.volumeTrigger != null ? original.volumeTrigger : source.transform;
            }
            else
            {
                data.SetRenderer(-1);
                data.volumeLayerMask = 1;
                data.volumeTrigger = source.transform;
            }
            data.antialiasing = AntialiasingMode.None;
            camera.Render();
            camera.targetTexture = null;
        }

        public void Dispose()
        {
            if (camera != null) Object.DestroyImmediate(camera.gameObject);
            if (chart != null) Object.DestroyImmediate(chart);
            camera = null; chart = null;
        }
    }
}
