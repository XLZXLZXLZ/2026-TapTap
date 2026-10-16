using UnityEngine;

namespace TapTap
{
    // One simulation supplies both the normal scene and the isolated fog background.
    [DisallowMultipleComponent, RequireComponent(typeof(ParticleSystem))]
    [DefaultExecutionOrder(-90)]
    public sealed class WorldEnvironmentParticles : MonoBehaviour
    {
        public const string LayerName = "EnvironmentBackground";
        [Tooltip("在整个 World 边缘额外覆盖的距离，避免镜头边缘没有粒子。")]
        [Min(0f)] public float WorldPadding = 16f;

        private ParticleSystem particles;
        private ParticleSystemRenderer particleRenderer;
        private Vector3 originalScale, originalPosition;
        private ParticleSystem.MinMaxCurve originalRate;
        private int originalMaximum;
        private bool captured;

        public int LayerMask => 1 << gameObject.layer;
        public bool CanRender => isActiveAndEnabled && particleRenderer != null && particleRenderer.enabled
            && !particleRenderer.forceRenderingOff;
        public float AreaRatio { get; private set; } = 1f;

        private void Awake() => CaptureSettings();

        private void CaptureSettings()
        {
            if (captured) return;
            particles = GetComponent<ParticleSystem>();
            particleRenderer = GetComponent<ParticleSystemRenderer>();
            originalScale = particles.shape.scale;
            originalPosition = particles.shape.position;
            originalRate = particles.emission.rateOverTime;
            originalMaximum = particles.main.maxParticles;
            int layer = UnityEngine.LayerMask.NameToLayer(LayerName);
            if (layer >= 0) gameObject.layer = layer;
            // Scene occlusion must never remove particles from the isolated backdrop.
            particleRenderer.allowOcclusionWhenDynamic = false;
            captured = true;
        }

        public void FitToWorld(WorldMapRoot world)
        {
            CaptureSettings();
            if (world == null || world.Source == null || world.Source.OrderedModules().Count == 0) return;
            Bounds bounds = world.WorldBounds;
            // Measure in emitter/shape coordinates without scaling the actual particles.
            var shape = particles.shape;
            Matrix4x4 toShape = Matrix4x4.Rotate(Quaternion.Inverse(Quaternion.Euler(shape.rotation)))
                * transform.worldToLocalMatrix;
            Bounds local = default;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                point = toShape.MultiplyPoint3x4(point);
                if (corner == 0) local = new Bounds(point, Vector3.zero); else local.Encapsulate(point);
            }
            Vector3 xAxis = transform.TransformVector(Quaternion.Euler(shape.rotation) * Vector3.right);
            Vector3 yAxis = transform.TransformVector(Quaternion.Euler(shape.rotation) * Vector3.up);
            Vector3 scale = originalScale;
            scale.x = Mathf.Max(originalScale.x, local.size.x + 2f * WorldPadding / Mathf.Max(0.0001f, xAxis.magnitude));
            scale.y = Mathf.Max(originalScale.y, local.size.y + 2f * WorldPadding / Mathf.Max(0.0001f, yAxis.magnitude));
            Vector3 center = Quaternion.Euler(shape.rotation) * new Vector3(local.center.x, local.center.y, 0f);
            center.z = originalPosition.z; // Keep the authored background depth.
            AreaRatio = scale.x * scale.y / Mathf.Max(0.0001f, originalScale.x * originalScale.y);
            if ((shape.scale - scale).sqrMagnitude < 0.000001f && (shape.position - center).sqrMagnitude < 0.000001f) return;

            bool restart = Application.isPlaying && particles.isPlaying;
            if (Application.isPlaying) particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            shape.scale = scale;
            shape.position = center;
            var emission = particles.emission;
            var rate = originalRate;
            if (rate.mode == ParticleSystemCurveMode.Constant) rate.constant *= AreaRatio;
            else if (rate.mode == ParticleSystemCurveMode.TwoConstants)
            { rate.constantMin *= AreaRatio; rate.constantMax *= AreaRatio; }
            else rate.curveMultiplier *= AreaRatio;
            emission.rateOverTime = rate;
            var main = particles.main;
            main.maxParticles = Mathf.CeilToInt(originalMaximum * AreaRatio);
            if (Application.isPlaying && (restart || main.playOnAwake))
            {
                // Refill the expanded field immediately, using the existing prewarm duration.
                if (main.prewarm) particles.Simulate(main.duration, false, true, true);
                particles.Play(false);
            }
        }
    }
}
