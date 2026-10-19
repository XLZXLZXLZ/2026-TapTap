using UnityEngine;

namespace TapTap
{
    // All submeshes use one door-local dissolve field, including recess and rivets.
    [DisallowMultipleComponent]
    public sealed class GoldenDoorVisual : MonoBehaviour
    {
        public Renderer[] Renderers;
        public ParticleSystem DissolveSparks;
        public Vector3 LockCentre = new Vector3(0f, 1f, 0f);
        public float Progress { get; private set; }
        private MaterialPropertyBlock properties;
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        private static readonly int SpaceId = Shader.PropertyToID("_WorldToDoor");
        private static readonly int OriginId = Shader.PropertyToID("_DissolveOrigin");

        public void SetProgress(float value)
        {
            Progress = Mathf.Clamp01(value);
            if (properties == null) properties = new MaterialPropertyBlock();
            if (Renderers == null || Renderers.Length == 0) Renderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer view in Renderers)
            {
                if (view == null || view is ParticleSystemRenderer) continue;
                view.GetPropertyBlock(properties);
                properties.SetFloat(DissolveId, Progress);
                properties.SetMatrix(SpaceId, transform.worldToLocalMatrix);
                properties.SetVector(OriginId, LockCentre);
                view.SetPropertyBlock(properties);
                view.enabled = Progress < 1f;
            }
        }

        public void BeginDissolve()
        {
            SetProgress(0f);
            if (DissolveSparks != null) DissolveSparks.Play(true);
        }

        public void ResetClosed()
        {
            StopSparks();
            SetProgress(0f);
        }

        public void SetOpen()
        {
            StopSparks();
            SetProgress(1f);
        }

        private void StopSparks()
        {
            if (DissolveSparks != null)
                DissolveSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
