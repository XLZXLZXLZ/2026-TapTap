using UnityEngine;

namespace TapTap
{
    public sealed class FinalCameraTarget : MonoBehaviour
    {
        [Tooltip("未指定旋转参考时，自动朝向大世界中心；关闭后使用目标对象的旋转。")]
        public bool AimAtWorldCenter = true;
        public bool OverrideFieldOfView;
        [Range(1f, 150f)] public float FieldOfView = 38f;

        public LevelCameraShot Evaluate(LevelCameraShot initial, Vector3 focus)
        {
            initial.Position = transform.position;
            initial.Rotation = AimAtWorldCenter && (focus - transform.position).sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(focus - transform.position, Vector3.up) : transform.rotation;
            initial.Focus = focus;
            if (OverrideFieldOfView) { initial.Physical = false; initial.FieldOfView = FieldOfView; }
            initial.FarClip = Mathf.Max(initial.FarClip, Vector3.Distance(transform.position, focus) + 200f);
            return initial;
        }

        private void OnDrawGizmos()
        {
            WorldMapRoot world = GetComponentInParent<WorldMapRoot>();
            Quaternion rotation = transform.rotation;
            if (AimAtWorldCenter && world != null && world.Source != null
                && (world.WorldBounds.center - transform.position).sqrMagnitude > 0.001f)
                rotation = Quaternion.LookRotation(world.WorldBounds.center - transform.position, Vector3.up);
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.color = new Color(1f, 0.75f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, 0.8f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, rotation, Vector3.one);
            Gizmos.DrawFrustum(Vector3.zero, FieldOfView, 12f, 0.3f, 1.6f);
            Gizmos.matrix = old;
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position, "最终镜头目标（世界坐标）");
#endif
        }
    }
}
