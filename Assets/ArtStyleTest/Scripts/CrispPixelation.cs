using UnityEngine;

namespace ArtStyleTest
{
    /// <summary>Camera settings consumed by the URP screen-effects renderer feature.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    [AddComponentMenu("Art Style Test/Crisp Pixelation")]
    public sealed class CrispPixelation : MonoBehaviour
    {
        [SerializeField] Shader pixelationShader;
        [SerializeField, Range(1, 64), Tooltip("Size of each square in output pixels. 1 keeps the original resolution.")]
        int pixelSize = 4;

        Material material;
        Camera attachedCamera;
        bool previousAllowMSAA;

        public int PixelSize
        {
            get => pixelSize;
            set => pixelSize = Mathf.Clamp(value, 1, 64);
        }

        void OnEnable()
        {
            attachedCamera = GetComponent<Camera>();
            previousAllowMSAA = attachedCamera.allowMSAA;
            attachedCamera.allowMSAA = false;
        }

        void OnValidate() => PixelSize = pixelSize;

        void OnDisable()
        {
            if (attachedCamera) attachedCamera.allowMSAA = previousAllowMSAA;
            if (!material) return;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
            material = null;
        }

        public bool TryGetMaterial(out Material result)
        {
            result = null;
            if (!pixelationShader || !pixelationShader.isSupported)
                return false;

            if (!material)
                material = new Material(pixelationShader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_PixelSize", PixelSize);
            result = material;
            return true;
        }
    }
}
