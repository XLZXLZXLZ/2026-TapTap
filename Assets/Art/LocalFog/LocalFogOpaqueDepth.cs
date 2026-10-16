using UnityEngine;
using UnityEngine.Rendering;

namespace TapTap.Art.LocalFog
{
    // Private fallback for cameras without URP additional data. Never attaches camera components.
    internal static class LocalFogOpaqueDepth
    {
        static RenderTexture depth;
        static Material material;
        static readonly int vpId = Shader.PropertyToID("_LocalFogOpaqueVP");
        static readonly int viewId = Shader.PropertyToID("_LocalFogOpaqueView");
        static readonly int farId = Shader.PropertyToID("_LocalFogOpaqueFar");
        public static bool Render(ScriptableRenderContext context, Camera cam, Shader shader)
        {
            if (shader == null || !shader.isSupported) return false;
            if (material == null) material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            int width = Mathf.Max(1, cam.scaledPixelWidth), height = Mathf.Max(1, cam.scaledPixelHeight);
            if (depth == null || depth.width != width || depth.height != height)
            {
                ReleaseTexture();
                var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)
                    ? RenderTextureFormat.RFloat : RenderTextureFormat.ARGBFloat;
                if (!SystemInfo.SupportsRenderTextureFormat(format)) return false;
                depth = new RenderTexture(width, height, 24, format, RenderTextureReadWrite.Linear)
                { name = "LocalFog Opaque Depth", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                if (!depth.Create()) { ReleaseTexture(); return false; }
            }
            var vp = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) * cam.worldToCameraMatrix;
            var cmd = CommandBufferPool.Get("LocalFog Opaque Depth Fallback");
            cmd.SetRenderTarget(depth);
            cmd.SetViewport(new Rect(0, 0, width, height));
            cmd.ClearRenderTarget(true, true, Color.white, SystemInfo.usesReversedZBuffer ? 0f : 1f);
            cmd.SetGlobalMatrix(vpId, vp);
            cmd.SetGlobalMatrix(viewId, cam.worldToCameraMatrix);
            cmd.SetGlobalFloat(farId, cam.farClipPlane);
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            // Fresh list includes objects spawned by gameplay without requiring registration.
            foreach (var renderer in Object.FindObjectsOfType<Renderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff
                    || (cam.cullingMask & (1 << renderer.gameObject.layer)) == 0
                    || (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                    || !GeometryUtility.TestPlanesAABB(planes, renderer.bounds)) continue;
                var materials = renderer.sharedMaterials;
                for (int submesh = 0; submesh < materials.Length; ++submesh)
                {
                    var source = materials[submesh];
                    if (source == null || source.renderQueue > 2500) continue;
                    cmd.DrawRenderer(renderer, material, submesh, 0);
                }
            }
            cmd.SetGlobalTexture("_LocalFogOpaqueDepth", depth);
            cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            return true;
        }
        static void ReleaseTexture()
        {
            if (depth == null) return;
            depth.Release();
            if (Application.isPlaying) Object.Destroy(depth); else Object.DestroyImmediate(depth);
            depth = null;
        }
        public static void Release()
        {
            ReleaseTexture();
            if (material != null)
            {
                if (Application.isPlaying) Object.Destroy(material); else Object.DestroyImmediate(material);
                material = null;
            }
        }
    }
}