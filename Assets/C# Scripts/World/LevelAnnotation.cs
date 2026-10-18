using UnityEngine;

namespace TapTap
{
    // Compatibility component for saved scenes. The annotation tool and feedback flow are retired.
    [AddComponentMenu("")]
    public sealed class LevelAnnotation : MonoBehaviour
    {
        private void OnEnable()
        {
            foreach (Renderer visual in GetComponentsInChildren<Renderer>(true)) visual.enabled = false;
            foreach (Collider2D sensor in GetComponentsInChildren<Collider2D>(true)) sensor.enabled = false;
            enabled = false;
        }
    }
}
