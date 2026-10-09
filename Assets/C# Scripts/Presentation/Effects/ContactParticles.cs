using UnityEngine;

namespace TapTap
{
    public sealed class ContactParticles : MonoBehaviour
    {
        [SerializeField] private ParticleSystem landing;
        [SerializeField] private ParticleSystem assembly;
        public void Configure(ParticleSystem landingEffect, ParticleSystem assemblyEffect)
        { landing = landingEffect; assembly = assemblyEffect; }

        public void Clear()
        {
            if (landing != null) landing.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (assembly != null) assembly.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void Burst(Vector2 point, Vector2 normal, bool assembled, float unit)
        {
            ParticleSystem effect = assembled ? assembly : landing;
            if (effect == null) return;
            effect.transform.position = point + normal * unit * 0.015f;
            // The prefab cone emits along local +Z, rotated into the contact's outward normal.
            effect.transform.rotation = Quaternion.LookRotation(new Vector3(normal.x, normal.y, 0f), Vector3.forward);
            effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            effect.Play(true);
        }
    }
}
