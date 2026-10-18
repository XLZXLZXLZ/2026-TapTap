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
        readonly HashSet<Renderer> uniqueForeground = new HashSet<Renderer>();
        static readonly int maskVP = Shader.PropertyToID("_LocalFogMaskVP");

        // Lists are shared by roots, not by volumes: several fogs can protect the same world.
        // Cache references only. Bounds, activation, layers and material slots remain live.
        sealed class ForegroundRoot
        {
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public readonly List<ForegroundDraw> Visible = new List<ForegroundDraw>();
            public readonly List<Material> Materials = new List<Material>();
            public int Version = -1;
            public int Pass = -1;
            public int VisiblePass = -1;
        }
        struct ForegroundDraw { public Renderer Renderer; public int MaterialCount; }
        static readonly Dictionary<Transform, ForegroundRoot> foregroundRoots = new Dictionary<Transform, ForegroundRoot>();
        static readonly Plane[] cameraPlanes = new Plane[6];
        static readonly List<LocalFogVolume> cameraFogs = new List<LocalFogVolume>();
        static readonly List<LocalFogVolume> renderedMasks = new List<LocalFogVolume>();
        static int foregroundVersion, foregroundPass;

        // Call after a procedural edit if its renderers must be refreshed immediately in edit mode.
        public void RefreshForegroundCache() => InvalidateForegroundCaches();
        static void InvalidateForegroundCaches() { foregroundVersion++; foregroundRoots.Clear(); }
#if UNITY_EDITOR
        static void ObjectsChanged(ref UnityEditor.ObjectChangeEventStream changes)
        {
            for (int i = 0; i < changes.length; i++)
            {
                // Transform/material values are read at render time. Structural changes can add renderers.
                var kind = changes.GetEventType(i);
                if (kind == UnityEditor.ObjectChangeKind.ChangeGameObjectOrComponentProperties
                    || kind == UnityEditor.ObjectChangeKind.ChangeAssetObjectProperties) continue;
                InvalidateForegroundCaches();
                break;
            }
        }
#endif
        static List<ForegroundDraw> ForegroundRenderers(Transform root, Camera cam)
        {
            if (!foregroundRoots.TryGetValue(root, out ForegroundRoot cached))
                foregroundRoots.Add(root, cached = new ForegroundRoot());
            // Runtime spawning needs no registration. Reuse a traversal across fogs in this camera
            // pass, but refresh on the next pass so newly spawned/reparented renderers are included.
            if (cached.Version != foregroundVersion || (Application.isPlaying && cached.Pass != foregroundPass))
            {
                root.GetComponentsInChildren<Renderer>(true, cached.Renderers);
                cached.Renderers.RemoveAll(NotMeshRenderer);
                cached.Version = foregroundVersion;
                cached.Pass = foregroundPass;
                cached.VisiblePass = -1;
            }
            // Different protection sets often share Generated. Cull and inspect that root only
            // once for this camera, even when it contributes to several different masks.
            if (cached.VisiblePass != foregroundPass)
            {
                cached.Visible.Clear();
                foreach (Renderer renderer in cached.Renderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                        || renderer.forceRenderingOff || (cam.cullingMask & (1 << renderer.gameObject.layer)) == 0
                        || !GeometryUtility.TestPlanesAABB(cameraPlanes, renderer.bounds)
                        || renderer.GetComponent<LocalFogVolume>() != null) continue;
                    renderer.GetSharedMaterials(cached.Materials);
                    cached.Visible.Add(new ForegroundDraw { Renderer = renderer, MaterialCount = cached.Materials.Count });
                }
                cached.VisiblePass = foregroundPass;
            }
            return cached.Visible;
        }
        static bool NotMeshRenderer(Renderer renderer) => !(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer);
        bool VisibleTo(Camera cam)
        {
            return isActiveAndEnabled && volumeRenderer != null && volumeRenderer.enabled
                && !volumeRenderer.forceRenderingOff && density > 0 && color.a > 0
                && (cam.cullingMask & (1 << gameObject.layer)) != 0
                && GeometryUtility.TestPlanesAABB(cameraPlanes, volumeRenderer.bounds);
        }
        static bool SameForeground(LocalFogVolume first, LocalFogVolume second)
        {
            if (first.foregroundMaskShader != second.foregroundMaskShader) return false;
            // Root order and duplicates do not change a silhouette union. Never merge different sets.
            return ContainsRoots(first.protectedForeground, second.protectedForeground)
                && ContainsRoots(second.protectedForeground, first.protectedForeground);
        }
        static bool ContainsRoots(Transform[] source, Transform[] destination)
        {
            if (source == null) return true;
            foreach (Transform root in source)
            {
                if (root == null) continue;
                bool found = false;
                if (destination != null)
                    foreach (Transform other in destination)
                        if (root == other) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }
        void SetForegroundMask(RenderTexture texture, Matrix4x4 vp)
        {
            if (properties == null || volumeRenderer == null) return;
            volumeRenderer.GetPropertyBlock(properties);
            properties.SetFloat("_LocalFogHasForegroundMask", texture != null ? 1 : 0);
            if (texture != null) properties.SetTexture("_LocalFogForegroundMask", texture);
            properties.SetMatrix(maskVP, vp);
            volumeRenderer.SetPropertyBlock(properties);
        }
        bool RenderForegroundMask(ScriptableRenderContext context, Camera cam, Matrix4x4 vp)
        {
            if (protectedForeground == null || protectedForeground.Length == 0)
            {
                SetForegroundMask(null, vp);
                return false;
            }
            if (foregroundMaskShader == null) foregroundMaskShader = Shader.Find("Hidden/TapTap/LocalFog/ForegroundMask");
            if (foregroundMaskShader == null || !foregroundMaskShader.isSupported)
            {
                SetForegroundMask(null, vp);
                return false;
            }
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
            CommandBuffer cmd = CommandBufferPool.Get("LocalFog Foreground Protection");
            try
            {
                cmd.SetRenderTarget(foregroundMask);
                cmd.SetViewport(new Rect(0, 0, width, height));
                cmd.ClearRenderTarget(false, true, Color.clear);
                cmd.SetGlobalMatrix(maskVP, vp);
                uniqueForeground.Clear();
                foreach (Transform root in protectedForeground)
                {
                    if (root == null) continue;
                    foreach (ForegroundDraw draw in ForegroundRenderers(root, cam))
                    {
                        Renderer renderer = draw.Renderer;
                        if (!uniqueForeground.Add(renderer)) continue;
                        for (int submesh = 0; submesh < draw.MaterialCount; ++submesh)
                            cmd.DrawRenderer(renderer, foregroundMaskMaterial, submesh, 0);
                    }
                }
                // Restore the destination; URP will establish its own attachments before drawing the camera.
                cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                context.ExecuteCommandBuffer(cmd);
            }
            finally { CommandBufferPool.Release(cmd); }
            SetForegroundMask(foregroundMask, vp);
            return true;
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
        // The renderer feature uses the same visibility rule as the fog coordinator.
        public static bool HasVisibleVolumes(Camera camera)
        {
            if (camera == null || active.Count == 0) return false;
            GeometryUtility.CalculateFrustumPlanes(camera, cameraPlanes);
            foreach (var fog in active)
                if (fog != null && fog.VisibleTo(camera)) return true;
            return false;
        }

        static void BeginURP(ScriptableRenderContext context, Camera cam)
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) return;
            GeometryUtility.CalculateFrustumPlanes(cam, cameraPlanes);
            cameraFogs.Clear();
            renderedMasks.Clear();
            foregroundPass++;
            foreach (var fog in active)
                if (fog != null && fog.VisibleTo(cam)) cameraFogs.Add(fog);
            Shader.SetGlobalFloat(depthAvailable, 0);
            if (cameraFogs.Count == 0) return;
            Matrix4x4 vp = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) * cam.worldToCameraMatrix;
            foreach (var fog in cameraFogs)
            {
                LocalFogVolume shared = null;
                foreach (var owner in renderedMasks)
                    if (SameForeground(owner, fog)) { shared = owner; break; }
                if (shared != null)
                    fog.SetForegroundMask(shared.foregroundMask, vp);
                else if (fog.RenderForegroundMask(context, cam, vp))
                    renderedMasks.Add(fog);
            }
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
#if UNITY_EDITOR
                UnityEditor.EditorApplication.hierarchyChanged += InvalidateForegroundCaches;
                UnityEditor.EditorApplication.projectChanged += InvalidateForegroundCaches;
                UnityEditor.Undo.undoRedoPerformed += InvalidateForegroundCaches;
                UnityEditor.ObjectChangeEvents.changesPublished += ObjectsChanged;
#endif
            }
            active.Add(fog);
        }
        static void Unregister(LocalFogVolume fog)
        {
            active.Remove(fog);
            if (active.Count != 0) return;
            RenderPipelineManager.beginCameraRendering -= BeginURP;
            RenderPipelineManager.endCameraRendering -= EndURP;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.hierarchyChanged -= InvalidateForegroundCaches;
            UnityEditor.EditorApplication.projectChanged -= InvalidateForegroundCaches;
            UnityEditor.Undo.undoRedoPerformed -= InvalidateForegroundCaches;
            UnityEditor.ObjectChangeEvents.changesPublished -= ObjectsChanged;
#endif
            InvalidateForegroundCaches();
            cameraFogs.Clear();
            renderedMasks.Clear();
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
        void OnValidate() { InvalidateForegroundCaches(); Apply(); }
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
