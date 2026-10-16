using TapTap;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class WorldExplorationMaskFeature : ScriptableRendererFeature
{
    private ExplorationPass pass;
    private EnvironmentPass environmentPass;
    private static readonly int BackgroundTexture = Shader.PropertyToID("_WorldExplorationBackground");

    public override void Create()
    {
        pass?.Dispose();
        environmentPass = new EnvironmentPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        // Last feature at this event: after Bloom, rewind and screen filters, before UI.
        // Use the exact event so URP 12 keeps an intermediate post-processing target.
        pass = new ExplorationPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera camera = renderingData.cameraData.camera;
        if (camera.cameraType != CameraType.Game || renderingData.cameraData.renderType != CameraRenderType.Base) return;
        DemoCamera rig = camera.GetComponentInParent<DemoCamera>();
        if (rig == null || rig.ViewCamera != camera || rig.Region == null) return;
        WorldMapRoot world = rig.Region.GetComponentInParent<WorldMapRoot>();
        WorldExplorationMask mask = world != null ? world.GetComponent<WorldExplorationMask>() : null;
        if (mask == null || !mask.isActiveAndEnabled || !pass.Setup(mask)) return;
        if (mask.Background != null && mask.Background.CanRender)
        {
            environmentPass.Setup(mask.Background);
            renderer.EnqueuePass(environmentPass);
        }
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing) { pass?.Dispose(); pass = null; }

    // Render only the existing environment particle layer, with its own cleared depth.
    // The same particles also render normally inside explored rooms; no second simulation.
    private sealed class EnvironmentPass : ScriptableRenderPass
    {
        private readonly List<ShaderTagId> tags = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };
        private WorldEnvironmentParticles background;

        public void Setup(WorldEnvironmentParticles source) => background = source;

        public override void OnCameraSetup(CommandBuffer commands, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 24;
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            commands.GetTemporaryRT(BackgroundTexture, descriptor, FilterMode.Bilinear);
            var target = new RenderTargetIdentifier(BackgroundTexture);
            ConfigureTarget(target, target);
            ConfigureClear(ClearFlag.All, CoreUtils.ConvertSRGBToActiveColorSpace(renderingData.cameraData.camera.backgroundColor));
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (background == null || !background.CanRender) return;
            DrawingSettings drawing = CreateDrawingSettings(tags, ref renderingData, SortingCriteria.CommonTransparent);
            var filtering = new FilteringSettings(RenderQueueRange.all, background.LayerMask);
            var state = new RenderStateBlock(RenderStateMask.Depth)
            { depthState = new DepthState(false, CompareFunction.Always) };
            context.DrawRenderers(renderingData.cullResults, ref drawing, ref filtering, ref state);
        }

        public override void OnCameraCleanup(CommandBuffer commands) => commands.ReleaseTemporaryRT(BackgroundTexture);
    }

    private sealed class ExplorationPass : ScriptableRenderPass
    {
        private static readonly int MaskTexture = Shader.PropertyToID("_WorldExplorationMask");
        private static readonly int MaskColor = Shader.PropertyToID("_WorldExplorationMaskColor");
        private static readonly int ViewProjection = Shader.PropertyToID("_WorldExplorationViewProjection");
        private static readonly int UseBackground = Shader.PropertyToID("_WorldExplorationUseBackground");
        private Material material;
        private WorldExplorationMask mask;

        public bool Setup(WorldExplorationMask source)
        {
            mask = source;
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/WorldExplorationMask");
                if (shader == null) return false;
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            return true;
        }

        public override void OnCameraSetup(CommandBuffer commands, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            descriptor.colorFormat = RenderTextureFormat.R8;
            descriptor.sRGB = false;
            commands.GetTemporaryRT(MaskTexture, descriptor, FilterMode.Point);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (mask == null || material == null) return;
            var commands = CommandBufferPool.Get("World exploration mask");
            try
            {
                Camera camera = renderingData.cameraData.camera;
                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                commands.SetRenderTarget(MaskTexture);
                commands.SetViewport(new Rect(0f, 0f, descriptor.width, descriptor.height));
                commands.ClearRenderTarget(false, true, Color.black);
                // Match URP's actual camera projection, including its current RT flip.
                // A dedicated matrix also isolates this draw from preceding fullscreen blits.
                commands.SetGlobalMatrix(ViewProjection, renderingData.cameraData.GetGPUProjectionMatrix()
                    * renderingData.cameraData.GetViewMatrix());
                mask.RecordVisibleRegions(commands, material, camera);
                commands.SetGlobalTexture(MaskTexture, new RenderTargetIdentifier(MaskTexture));
                Color color = mask.MaskColor;
                color.a = 1f; // Unexplored background must be fully covered.
                commands.SetGlobalColor(MaskColor, color);
                bool hasBackground = mask.Background != null && mask.Background.CanRender;
                commands.SetGlobalFloat(UseBackground, hasBackground ? 1f : 0f);
                commands.SetGlobalTexture(BackgroundTexture, hasBackground
                    ? new RenderTargetIdentifier(BackgroundTexture) : new RenderTargetIdentifier(Texture2D.blackTexture));
                commands.SetRenderTarget(renderingData.cameraData.renderer.cameraColorTarget);
                commands.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3);
                context.ExecuteCommandBuffer(commands);
            }
            finally { CommandBufferPool.Release(commands); }
        }

        public override void OnCameraCleanup(CommandBuffer commands) => commands.ReleaseTemporaryRT(MaskTexture);
        public void Dispose() => CoreUtils.Destroy(material);
    }
}
