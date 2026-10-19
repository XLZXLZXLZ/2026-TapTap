using UnityEngine;

namespace ScreenFilters
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class MainMenuBlurCamera : MonoBehaviour
    {
        [Range(0f, 1f)] public float Amount = 1f;
        [Tooltip("Blur radius in pixels at 1080p.")]
        [Range(0f, 20f)] public float Radius = 6f;
        [Header("Diagonal boundary in normalized screen coordinates")]
        [Range(0f, 1f)] public float BottomBoundary = 0.333333f;
        [Range(0f, 1f)] public float TopBoundary = 0.5f;
        [Tooltip("Full width of the soft transition, as a fraction of the screen width.")]
        [Range(0.01f, 0.6f)] public float Feather = 0.2f;
    }
}
