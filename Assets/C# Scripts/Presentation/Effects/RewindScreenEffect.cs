using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class RewindScreenEffect : MonoBehaviour
    {
        private Material material;
        private RenderTexture recentFrame;
        private RenderTexture olderFrame;
        private int historySamples;
        private int historySession;
        private float lastCaptureClock;
        private static readonly int HistoryTexId = Shader.PropertyToID("_HistoryTex");
        private static readonly int OlderHistoryTexId = Shader.PropertyToID("_OlderHistoryTex");
        private static readonly int HistoryStateId = Shader.PropertyToID("_HistoryState");

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            EffectManager effects = EffectManager.Existing;
            if (effects == null || !effects.isActiveAndEnabled || effects.RewindVisualStrength <= 0f)
            {
                ReleaseHistory();
                Graphics.Blit(source, destination);
                return;
            }
            Shader shader = effects.RewindShader;
            if (shader == null || !shader.isSupported)
            {
                ReleaseHistory();
                Graphics.Blit(source, destination);
                return;
            }
            if (material == null || material.shader != shader)
            {
                ReleaseMaterial();
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            bool captureHistory = effects.RewindVisualActive && effects.RewindGhostStrength > 0f;
            if (captureHistory) PrepareHistory(source, effects);
            else ReleaseHistory();
            effects.ApplyRewindMaterial(material);
            material.SetTexture(HistoryTexId, historySamples > 0 ? (Texture)recentFrame : Texture2D.blackTexture);
            material.SetTexture(OlderHistoryTexId, historySamples > 1 ? (Texture)olderFrame : Texture2D.blackTexture);
            material.SetVector(HistoryStateId, new Vector4(historySamples > 0 ? 1f : 0f,
                historySamples > 1 ? 1f : 0f, 0f, 0f));
            Graphics.Blit(source, destination, material);
            if (captureHistory && (historySamples == 0
                || effects.RewindPresentationClock - lastCaptureClock >= effects.RewindGhostInterval))
            {
                // Store the raw scene, never the processed output: trails cannot feed back into themselves.
                RenderTexture spare = olderFrame;
                olderFrame = recentFrame;
                recentFrame = spare;
                Graphics.Blit(source, recentFrame);
                RenderTexture.active = destination;
                historySamples = Mathf.Min(2, historySamples + 1);
                lastCaptureClock = effects.RewindPresentationClock;
            }
        }

        private void PrepareHistory(RenderTexture source, EffectManager effects)
        {
            int width = Mathf.Max(1, source.width / 2);
            int height = Mathf.Max(1, source.height / 2);
            if (recentFrame != null && (recentFrame.width != width || recentFrame.height != height
                || recentFrame.graphicsFormat != source.graphicsFormat || historySession != effects.RewindVisualSession
                || effects.RewindPresentationClock < lastCaptureClock)) ReleaseHistory();
            if (recentFrame != null) return;
            var descriptor = source.descriptor;
            descriptor.width = width;
            descriptor.height = height;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.useDynamicScale = false;
            recentFrame = CreateHistoryTexture(descriptor, "Rewind Recent Frame");
            olderFrame = CreateHistoryTexture(descriptor, "Rewind Older Frame");
            historySession = effects.RewindVisualSession;
        }

        private static RenderTexture CreateHistoryTexture(RenderTextureDescriptor descriptor, string label)
        {
            var texture = new RenderTexture(descriptor)
            {
                name = label, hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            texture.Create();
            return texture;
        }

        private void ReleaseHistory()
        {
            DestroyHistoryTexture(recentFrame);
            DestroyHistoryTexture(olderFrame);
            recentFrame = olderFrame = null;
            historySamples = 0;
        }

        private static void DestroyHistoryTexture(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) Destroy(texture);
            else DestroyImmediate(texture);
        }

        private void ReleaseMaterial()
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
            material = null;
        }

        private void OnDisable() { ReleaseHistory(); ReleaseMaterial(); }
        private void OnDestroy() { ReleaseHistory(); ReleaseMaterial(); }
    }
}
