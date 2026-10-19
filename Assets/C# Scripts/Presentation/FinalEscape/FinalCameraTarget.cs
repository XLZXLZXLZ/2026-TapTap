using UnityEngine;

namespace TapTap
{
    public sealed class FinalCameraTarget : MonoBehaviour
    {
        public bool OverrideFieldOfView;
        [Range(1f, 150f)] public float FieldOfView = 38f;

        public LevelCameraShot Evaluate(LevelCameraShot initial, Vector3 focus)
        {
            initial.Position = transform.position;
            // The escape pullback keeps the camera's rotation captured on entry.
            initial.Focus = focus;
            if (OverrideFieldOfView) { initial.Physical = false; initial.FieldOfView = FieldOfView; }
            initial.FarClip = Mathf.Max(initial.FarClip, Vector3.Distance(transform.position, focus) + 200f);
            return initial;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, 0.8f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position, "最终镜头位置（保持入场旋转）");
#endif
        }
    }
}
