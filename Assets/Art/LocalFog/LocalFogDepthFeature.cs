using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TapTap.Art.LocalFog
{
    // Scene View always uses a depth prepass. Request the same depth for visible
    // fog in Game cameras, rather than URP's copy of the opaque color-pass depth.
    public sealed class LocalFogDepthFeature : ScriptableRendererFeature
    {
        private FogDepthPass pass;

        public override void Create() => pass = new FogDepthPass();

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (camera.cameraType == CameraType.Game && LocalFogVolume.HasVisibleVolumes(camera))
                renderer.EnqueuePass(pass);
        }

        private sealed class FogDepthPass : ScriptableRenderPass
        {
            public FogDepthPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            // Declaring depth before opaque rendering makes URP enqueue its own
            // DepthOnly prepass. No custom geometry or color pass is needed here.
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }
        }
    }
}
