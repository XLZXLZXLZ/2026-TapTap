using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class RewindScreenEffect : MonoBehaviour
    {
        private Material material;

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            EffectManager effects = EffectManager.Existing;
            if (effects == null || !effects.isActiveAndEnabled || effects.RewindVisualStrength <= 0f)
            {
                Graphics.Blit(source, destination);
                return;
            }
            Shader shader = effects.RewindShader;
            if (shader == null || !shader.isSupported)
            {
                Graphics.Blit(source, destination);
                return;
            }
            if (material == null || material.shader != shader)
            {
                ReleaseMaterial();
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            effects.ApplyRewindMaterial(material);
            Graphics.Blit(source, destination, material);
        }

        private void ReleaseMaterial()
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
            material = null;
        }

        private void OnDisable() => ReleaseMaterial();
        private void OnDestroy() => ReleaseMaterial();
    }
}
