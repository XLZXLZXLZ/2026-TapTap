using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ScreenFilters
{
    // Blurs the scene after its post effects; overlay UI stays sharp.
    public sealed class PauseBlurFeature : ScriptableRendererFeature
    {
        [Range(0f, 1f)] public static float Amount;
        public Shader BlurShader;
        private BlurPass pass;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAmount() => Amount = 0f;
        public override void Create()
        {
            pass?.Dispose();
            // URP 12 checks for this exact event when retaining its post-process color buffer.
            pass = new BlurPass(BlurShader) { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            if (Amount <= 0f || data.cameraData.camera.cameraType != CameraType.Game || !data.cameraData.resolveFinalTarget) return;
            renderer.EnqueuePass(pass);
        }
        protected override void Dispose(bool disposing) { pass?.Dispose(); pass = null; }

        private sealed class BlurPass : ScriptableRenderPass
        {
            private readonly Material material;
            private readonly int original = Shader.PropertyToID("_PauseOriginal");
            private readonly int horizontal = Shader.PropertyToID("_PauseHorizontal");
            private readonly int blurred = Shader.PropertyToID("_PauseBlurred");
            public BlurPass(Shader shader) { if (shader != null) material = CoreUtils.CreateEngineMaterial(shader); }
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                if (material == null) return;
                CommandBuffer cmd = CommandBufferPool.Get("Pause menu background blur");
                try
                {
                    RenderTargetIdentifier source = data.cameraData.renderer.cameraColorTarget;
                    RenderTextureDescriptor descriptor = data.cameraData.cameraTargetDescriptor;
                    descriptor.depthBufferBits = 0; descriptor.msaaSamples = 1;
                    cmd.GetTemporaryRT(original, descriptor, FilterMode.Bilinear);
                    descriptor.width = Mathf.Max(1, descriptor.width / 2);
                    descriptor.height = Mathf.Max(1, descriptor.height / 2);
                    cmd.GetTemporaryRT(horizontal, descriptor, FilterMode.Bilinear);
                    cmd.GetTemporaryRT(blurred, descriptor, FilterMode.Bilinear);
                    cmd.Blit(source, original);
                    cmd.SetGlobalFloat("_PauseBlurAmount", Amount);
                    cmd.SetGlobalVector("_PauseBlurDirection", new Vector4(1f, 0f, 0f, 0f));
                    cmd.Blit(original, horizontal, material, 0);
                    cmd.SetGlobalVector("_PauseBlurDirection", new Vector4(0f, 1f, 0f, 0f));
                    cmd.Blit(horizontal, blurred, material, 0);
                    cmd.SetGlobalTexture("_PauseBlurTexture", blurred);
                    cmd.Blit(original, source, material, 1);
                    cmd.ReleaseTemporaryRT(original); cmd.ReleaseTemporaryRT(horizontal); cmd.ReleaseTemporaryRT(blurred);
                    context.ExecuteCommandBuffer(cmd);
                }
                finally { CommandBufferPool.Release(cmd); }
            }
            public void Dispose() => CoreUtils.Destroy(material);
        }
    }
}
