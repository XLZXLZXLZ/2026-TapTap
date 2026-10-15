using ArtStyleTest;
using ScreenFilters;
using TapTap;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Lives outside TapTap.Runtime so the renderer can also use the independent art-study components.
public sealed class TapTapScreenEffectsFeature : ScriptableRendererFeature
{
    private ScreenEffectsPass pass;

    public override void Create()
    {
        pass = new ScreenEffectsPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera camera = renderingData.cameraData.camera;
        if (camera.cameraType != CameraType.Game || renderingData.cameraData.renderType != CameraRenderType.Base) return;
        var rewind = camera.GetComponent<RewindScreenEffect>();
        if (rewind == null || !rewind.isActiveAndEnabled || !rewind.HasActiveEffect) rewind = null;
        var pixelation = camera.GetComponent<CrispPixelation>();
        if (pixelation == null || !pixelation.isActiveAndEnabled || pixelation.PixelSize <= 1) pixelation = null;
        // A camera migrated to the shared profile must not run pixelation twice.
        var filters = camera.GetComponent<ScreenFilterCamera>();
        if (filters != null && filters.HasPixelation) pixelation = null;
        if (rewind == null && pixelation == null) return;
        pass.Setup(renderer.cameraColorTarget, rewind, pixelation);
        renderer.EnqueuePass(pass);
    }

    private sealed class ScreenEffectsPass : ScriptableRenderPass
    {
        private static readonly int SourceId = Shader.PropertyToID("_TapTapScreenEffectsSource");
        private RenderTargetIdentifier cameraColor;
        private RewindScreenEffect rewind;
        private CrispPixelation pixelation;

        public void Setup(RenderTargetIdentifier target, RewindScreenEffect rewindEffect, CrispPixelation pixelEffect)
        {
            cameraColor = target;
            rewind = rewindEffect;
            pixelation = pixelEffect;
        }

        public override void OnCameraSetup(CommandBuffer commands, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            commands.GetTemporaryRT(SourceId, descriptor, pixelation != null ? FilterMode.Point : FilterMode.Bilinear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var commands = CommandBufferPool.Get("TapTap screen effects");
            try
            {
                var source = new RenderTargetIdentifier(SourceId);
                if (rewind != null)
                {
                    commands.Blit(cameraColor, source);
                    rewind.Render(commands, source, cameraColor, renderingData.cameraData.cameraTargetDescriptor);
                }
                if (pixelation != null && pixelation.TryGetMaterial(out Material material))
                {
                    commands.Blit(cameraColor, source);
                    commands.Blit(source, cameraColor, material);
                }
                context.ExecuteCommandBuffer(commands);
            }
            finally { CommandBufferPool.Release(commands); }
        }

        public override void OnCameraCleanup(CommandBuffer commands) => commands.ReleaseTemporaryRT(SourceId);
    }
}
