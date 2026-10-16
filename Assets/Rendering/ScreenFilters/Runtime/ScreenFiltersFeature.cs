using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ScreenFilters
{
    public sealed class ScreenFiltersFeature : ScriptableRendererFeature
    {
        private FilterPass pass;
        public override void Create()
        {
            pass?.Dispose();
            pass = new FilterPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (camera.cameraType != CameraType.Game || renderingData.cameraData.renderType != CameraRenderType.Base) return;
            var binding = camera.GetComponent<ScreenFilterCamera>();
            if (binding == null || !binding.isActiveAndEnabled || binding.Profile == null || !binding.Profile.HasActiveFilters) return;
            pass.Setup(binding.Profile);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) { pass?.Dispose(); pass = null; }

        private sealed class FilterPass : ScriptableRenderPass
        {
            private readonly ScreenFilterRenderer filters = new ScreenFilterRenderer();
            private ScreenFilterProfile profile;
            public void Setup(ScreenFilterProfile settings) => profile = settings;

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var commands = CommandBufferPool.Get("Screen filter stack");
                try
                {
                    // URP post-processing swaps color buffers. Resolve the active target here,
                    // after Bloom and the other post effects, rather than during pass enqueue.
                    RenderTargetIdentifier target = renderingData.cameraData.renderer.cameraColorTarget;
                    filters.Record(commands, target, target, renderingData.cameraData.cameraTargetDescriptor,
                        profile, Time.unscaledTime);
                    context.ExecuteCommandBuffer(commands);
                }
                finally { CommandBufferPool.Release(commands); }
            }

            public void Dispose() => filters.Dispose();
        }
    }
}
