using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    public sealed class LevelRegion : MonoBehaviour
    {
        public Vector2Int Size = new Vector2Int(32, 18);
        [Min(0.01f)] public float UnitSize = 1f;
        public LevelDefinition Source;
        [HideInInspector] public string WorldModuleId;
        [HideInInspector] public int SequenceNumber;
        public CheckpointFlag EntryCheckpoint;
        [Tooltip("没有来源布局时使用的镜头设置；有来源时在布局资源中配置。")]
        [SerializeField] private LevelCameraSettings cameraSettings = new LevelCameraSettings();

        public LevelCameraSettings CameraSettings => Source != null && Source.CameraSettings != null
            ? Source.CameraSettings : cameraSettings ?? (cameraSettings = new LevelCameraSettings());

        public Bounds WorldBounds
        {
            get
            {
                RectInt area = Source != null ? Source.Footprint.Bounds : new RectInt(Vector2Int.zero, Size);
                var min = new Vector3(area.xMin * UnitSize, area.yMin * UnitSize, 0f);
                var max = new Vector3(area.xMax * UnitSize, area.yMax * UnitSize, 0f);
                var bounds = new Bounds(transform.TransformPoint(min), Vector3.zero);
                bounds.Encapsulate(transform.TransformPoint(new Vector3(max.x, min.y, 0f)));
                bounds.Encapsulate(transform.TransformPoint(new Vector3(min.x, max.y, 0f)));
                bounds.Encapsulate(transform.TransformPoint(max));
                return bounds;
            }
        }

        public Vector2 EntrySpawnPosition => EntryCheckpoint != null
            ? EntryCheckpoint.SpawnPosition
            : (Vector2)transform.position;

        private void OnDrawGizmosSelected()
        {
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Vector3 size = new Vector3(Size.x * UnitSize, Size.y * UnitSize, 0f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.25f, 0.8f, 1f, 0.85f);
            if (Source != null)
                foreach (LevelBoundaryEdge edge in Source.Footprint.Edges)
                    Gizmos.DrawLine((Vector2)edge.Start * UnitSize, (Vector2)edge.End * UnitSize);
            else Gizmos.DrawWireCube(size * 0.5f, size);
            Gizmos.matrix = Matrix4x4.identity;
            if (EntryCheckpoint != null)
            {
                Gizmos.color = new Color(0.35f, 1f, 0.4f, 1f);
                Gizmos.DrawWireSphere(EntrySpawnPosition, Mathf.Max(0.01f, UnitSize) * 0.2f);
            }
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
