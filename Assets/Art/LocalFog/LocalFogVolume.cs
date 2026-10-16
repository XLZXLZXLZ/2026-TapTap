using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

namespace TapTap.Art.LocalFog
{
    [ExecuteAlways, DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class LocalFogVolume : MonoBehaviour
    {
        public enum FogShape { Box, Sphere }
        [Tooltip("Sphere becomes an ellipsoid when dimensions differ.")]
        public FogShape shape = FogShape.Box;
        [Tooltip("Full dimensions in local units. Keep parent scale positive.")]
        public Vector3 size = new Vector3(10, 5, 10);
        [ColorUsage(true, false)] public Color color = new Color(0.72f, 0.8f, 0.85f, 1);
        [Min(0)] public float density = 0.25f;
        [Tooltip("Fraction of radius/half extent used for the soft boundary.")]
        [Range(0, 1)] public float edgeFade = 0.3f;
        [Range(8, 96)] public int raySteps = 48;
        [Header("Foreground Protection (URP)")]
        [Tooltip("Drag foreground objects or their parents here. Their visible mesh silhouettes will never receive this fog. No source materials are changed.")]
        public Transform[] protectedForeground = new Transform[0];
        [SerializeField, HideInInspector] Shader foregroundMaskShader;
        [SerializeField, HideInInspector] Shader opaqueDepthShader;
        Material foregroundMaskMaterial;
        RenderTexture foregroundMask;
        readonly List<Renderer> foregroundRenderers = new List<Renderer>();
        readonly HashSet<Renderer> uniqueForeground = new HashSet<Renderer>();
        static readonly int maskVP = Shader.PropertyToID("_LocalFogMaskVP");

        void RenderForegroundMask(ScriptableRenderContext context, Camera cam)
        {
            if (properties == null || volumeRenderer == null) return;
            volumeRenderer.GetPropertyBlock(properties);
            properties.SetFloat("_LocalFogHasForegroundMask", 0);
            if (protectedForeground == null || protectedForeground.Length == 0)
            {
                volumeRenderer.SetPropertyBlock(properties);
                return;
            }
            if (foregroundMaskShader == null) foregroundMaskShader = Shader.Find("Hidden/TapTap/LocalFog/ForegroundMask");
            if (foregroundMaskShader == null || !foregroundMaskShader.isSupported) return;
            if (foregroundMaskMaterial == null)
                foregroundMaskMaterial = new Material(foregroundMaskShader) { hideFlags = HideFlags.HideAndDontSave };
            int width = Mathf.Max(1, cam.scaledPixelWidth), height = Mathf.Max(1, cam.scaledPixelHeight);
            if (foregroundMask == null || foregroundMask.width != width || foregroundMask.height != height)
            {
                ReleaseMaskTexture();
                foregroundMask = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                { name = "LocalFog Foreground Protection", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                foregroundMask.Create();
            }
            Matrix4x4 vp = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) * cam.worldToCameraMatrix;
            CommandBuffer cmd = CommandBufferPool.Get("LocalFog Foreground Protection");
            cmd.SetRenderTarget(foregroundMask);
            cmd.SetViewport(new Rect(0, 0, width, height));
            cmd.ClearRenderTarget(false, true, Color.clear);
            cmd.SetGlobalMatrix(maskVP, vp);
            uniqueForeground.Clear();
            foreach (Transform root in protectedForeground)
            {
                if (root == null) continue;
                foregroundRenderers.Clear();
                root.GetComponentsInChildren<Renderer>(false, foregroundRenderers);
                foreach (Renderer renderer in foregroundRenderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer == volumeRenderer
                        || renderer.GetComponent<LocalFogVolume>() != null || !uniqueForeground.Add(renderer)
                        || (cam.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                    // This silhouette pass is for opaque foreground geometry, not particles or alpha sprites.
                    if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                    var materials = renderer.sharedMaterials;
                    for (int submesh = 0; submesh < materials.Length; ++submesh)
                        cmd.DrawRenderer(renderer, foregroundMaskMaterial, submesh, 0);
                }
            }
            // Restore the destination; URP will establish its own attachments before drawing the camera.
            cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            properties.SetTexture("_LocalFogForegroundMask", foregroundMask);
            properties.SetMatrix(maskVP, vp);
            properties.SetFloat("_LocalFogHasForegroundMask", 1);
            volumeRenderer.SetPropertyBlock(properties);
        }
        void ReleaseMaskTexture()
        {
            if (foregroundMask == null) return;
            foregroundMask.Release();
            if (Application.isPlaying) Destroy(foregroundMask); else DestroyImmediate(foregroundMask);
            foregroundMask = null;
        }
        void ReleaseForegroundMask()
        {
            ReleaseMaskTexture();
            if (foregroundMaskMaterial != null)
            {
                if (Application.isPlaying) Destroy(foregroundMaskMaterial); else DestroyImmediate(foregroundMaskMaterial);
                foregroundMaskMaterial = null;
            }
        }
        // One coordinator for all volumes: overlapping volumes must not restore each other's state.
        static readonly HashSet<LocalFogVolume> active = new HashSet<LocalFogVolume>();
        static readonly Dictionary<UniversalAdditionalCameraData, CameraOverrideOption> savedDepth = new Dictionary<UniversalAdditionalCameraData, CameraOverrideOption>();
        static readonly HashSet<int> warnedCameras = new HashSet<int>();
        static readonly int depthAvailable = Shader.PropertyToID("_LocalFogDepthAvailable");
        static void BeginURP(ScriptableRenderContext context, Camera cam)
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) return;
            bool visible = false;
            foreach (var fog in active)
                if (fog != null && fog.isActiveAndEnabled && (cam.cullingMask & (1 << fog.gameObject.layer)) != 0)
                { visible = true; break; }
            Shader.SetGlobalFloat(depthAvailable, 0);
            if (!visible) return;
            foreach (var fog in active)
                if (fog != null && fog.isActiveAndEnabled && (cam.cullingMask & (1 << fog.gameObject.layer)) != 0)
                    fog.RenderForegroundMask(context, cam);
            if (cam.cameraType == CameraType.SceneView)
            {
                // URP 12 always requests Scene View depth.
                Shader.SetGlobalFloat(depthAvailable, 1);
                return;
            }
            UniversalAdditionalCameraData data;
            if (cam.TryGetComponent(out data))
            {
                if (!savedDepth.ContainsKey(data)) savedDepth.Add(data, data.requiresDepthOption);
                data.requiresDepthOption = CameraOverrideOption.On;
                Shader.SetGlobalFloat(depthAvailable, 1);
            }
            else if (((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).supportsCameraDepthTexture)
                Shader.SetGlobalFloat(depthAvailable, 1);
            else
            {
                Shader fallbackShader = null;
                foreach (var fog in active)
                    if (fog != null && fog.opaqueDepthShader != null) { fallbackShader = fog.opaqueDepthShader; break; }
                if (fallbackShader == null) fallbackShader = Shader.Find("Hidden/TapTap/LocalFog/OpaqueDepth");
                if (LocalFogOpaqueDepth.Render(context, cam, fallbackShader))
                    Shader.SetGlobalFloat(depthAvailable, 2);
                else if (warnedCameras.Add(cam.GetInstanceID()))
                    Debug.LogWarning("LocalFog: Opaque depth fallback could not be created for camera '" + cam.name + "'. Check LocalFogOpaqueDepth.shader import errors and supported render texture formats.", cam);
            }
        }
        static void EndURP(ScriptableRenderContext context, Camera cam)
        {
            UniversalAdditionalCameraData data;
            CameraOverrideOption previous;
            if (cam != null && cam.TryGetComponent(out data) && savedDepth.TryGetValue(data, out previous))
            {
                data.requiresDepthOption = previous;
                savedDepth.Remove(data);
            }
        }
        static void Register(LocalFogVolume fog)
        {
            if (active.Count == 0)
            {
                RenderPipelineManager.beginCameraRendering += BeginURP;
                RenderPipelineManager.endCameraRendering += EndURP;
            }
            active.Add(fog);
        }
        static void Unregister(LocalFogVolume fog)
        {
            active.Remove(fog);
            if (active.Count != 0) return;
            RenderPipelineManager.beginCameraRendering -= BeginURP;
            RenderPipelineManager.endCameraRendering -= EndURP;
            foreach (var pair in savedDepth)
                if (pair.Key != null) pair.Key.requiresDepthOption = pair.Value;
            savedDepth.Clear();
            LocalFogOpaqueDepth.Release();
            warnedCameras.Clear();
        }
        MaterialPropertyBlock properties;
        MeshRenderer volumeRenderer;

        void OnEnable()
        {
            volumeRenderer = GetComponent<MeshRenderer>();
            Camera.onPreCull += PrepareCamera;
            Register(this);
            Apply();
        }
        void OnDisable() { Camera.onPreCull -= PrepareCamera; Unregister(this); ReleaseForegroundMask(); }
        void OnValidate() { Apply(); }
        void Update() { Apply(); }
        void PrepareCamera(Camera cam)
        {
            if (GraphicsSettings.currentRenderPipeline != null) return;
            if (cam != null && (cam.cullingMask & (1 << gameObject.layer)) != 0)
                cam.depthTextureMode |= DepthTextureMode.Depth;
        }
        void Apply()
        {
            size = new Vector3(Mathf.Max(0.001f, size.x), Mathf.Max(0.001f, size.y), Mathf.Max(0.001f, size.z));
            transform.localScale = size;
            if (volumeRenderer == null) volumeRenderer = GetComponent<MeshRenderer>();
            if (volumeRenderer == null) return;
            // Support Built-in and URP; never change pipeline assets.
            volumeRenderer.enabled = GraphicsSettings.currentRenderPipeline == null || GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset;
            if (properties == null) properties = new MaterialPropertyBlock();
            volumeRenderer.GetPropertyBlock(properties);
            properties.SetFloat("_Shape", (float)shape);
            properties.SetColor("_FogColor", color);
            properties.SetFloat("_Density", Mathf.Max(0, density));
            properties.SetFloat("_EdgeFade", Mathf.Clamp01(edgeFade));
            properties.SetFloat("_Steps", Mathf.Clamp(raySteps, 8, 96));
            volumeRenderer.SetPropertyBlock(properties);
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(color.r, color.g, color.b, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            if (shape == FogShape.Box) Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            else Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
        }
    }
}