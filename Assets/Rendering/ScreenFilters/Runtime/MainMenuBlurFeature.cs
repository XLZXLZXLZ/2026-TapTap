using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ScreenFilters
{
    // Camera-local opt-in: other scenes using this renderer do not pay for the blur.
    public sealed class MainMenuBlurFeature : ScriptableRendererFeature
    {
        public Shader BlurShader;
        private BlurPass pass;

        public override void Create()
        {
            pass?.Dispose();
            pass = new BlurPass(BlurShader) { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            Camera camera = data.cameraData.camera;
            if (camera.cameraType != CameraType.Game || data.cameraData.renderType != CameraRenderType.Base) return;
            MainMenuBlurCamera settings = camera.GetComponent<MainMenuBlurCamera>();
            if (settings == null || !settings.isActiveAndEnabled || settings.Amount <= 0f || settings.Radius <= 0f) return;
            pass.Settings = settings;
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) { pass?.Dispose(); pass = null; }

        private sealed class BlurPass : ScriptableRenderPass
        {
            public MainMenuBlurCamera Settings;
            private readonly Material material;
            private static readonly int Original = Shader.PropertyToID("_MenuOriginal");
            private static readonly int Horizontal = Shader.PropertyToID("_MenuHorizontal");
            private static readonly int Blurred = Shader.PropertyToID("_MenuBlurred");
            private static readonly int Direction = Shader.PropertyToID("_MenuBlurDirection");
            private static readonly int Mask = Shader.PropertyToID("_MenuBlurMask");

            public BlurPass(Shader shader)
            {
                if (shader != null) material = CoreUtils.CreateEngineMaterial(shader);
                ConfigureInput(ScriptableRenderPassInput.Color);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                if (material == null || Settings == null) return;
                CommandBuffer cmd = CommandBufferPool.Get("Main menu diagonal blur");
                try
                {
                    RenderTargetIdentifier source = data.cameraData.renderer.cameraColorTarget;
                    RenderTextureDescriptor descriptor = data.cameraData.cameraTargetDescriptor;
                    descriptor.depthBufferBits = 0;
                    descriptor.msaaSamples = 1;
                    descriptor.bindMS = false;
                    cmd.GetTemporaryRT(Original, descriptor, FilterMode.Bilinear);
                    float radius = Settings.Radius * descriptor.height / 1080f;
                    descriptor.width = Mathf.Max(1, descriptor.width / 2);
                    descriptor.height = Mathf.Max(1, descriptor.height / 2);
                    cmd.GetTemporaryRT(Horizontal, descriptor, FilterMode.Bilinear);
                    cmd.GetTemporaryRT(Blurred, descriptor, FilterMode.Bilinear);
                    cmd.Blit(source, Original);
                    cmd.Blit(Original, Blurred);
                    // Shorter repeated kernels avoid sparse sampling streaks in pixelated foliage.
                    for (int iteration = 0; iteration < 2; iteration++)
                    {
                        cmd.SetGlobalVector(Direction, new Vector4(radius * 0.25f, 0f, 0f, 0f));
                        cmd.Blit(Blurred, Horizontal, material, 0);
                        cmd.SetGlobalVector(Direction, new Vector4(0f, radius * 0.25f, 0f, 0f));
                        cmd.Blit(Horizontal, Blurred, material, 0);
                    }
                    cmd.SetGlobalVector(Mask, new Vector4(Settings.BottomBoundary, Settings.TopBoundary,
                        Mathf.Max(0.001f, Settings.Feather), Settings.Amount));
                    cmd.SetGlobalTexture(Blurred, new RenderTargetIdentifier(Blurred));
                    cmd.Blit(Original, source, material, 1);
                    cmd.ReleaseTemporaryRT(Original);
                    cmd.ReleaseTemporaryRT(Horizontal);
                    cmd.ReleaseTemporaryRT(Blurred);
                    context.ExecuteCommandBuffer(cmd);
                }
                finally { CommandBufferPool.Release(cmd); }
            }

            public void Dispose() => CoreUtils.Destroy(material);
        }
    }
}
