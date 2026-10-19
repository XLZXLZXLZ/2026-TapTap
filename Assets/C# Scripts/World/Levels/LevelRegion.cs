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

        // Prefab rebuilds can invalidate scene overrides. Recover the authored entry by
        // placement ID, never by hierarchy order (a level may have several checkpoints).
        public CheckpointFlag ResolveEntryCheckpoint()
        {
            if (EntryCheckpoint != null && EntryCheckpoint.GetComponentInParent<LevelRegion>() == this)
                return EntryCheckpoint;
            EntryCheckpoint = null;
            LevelPlacement entry = Source != null ? Source.EntryPlacement : null;
            if (entry?.Brush == null || entry.Brush.HandlerId != "checkpoint") return null;
            foreach (CheckpointFlag checkpoint in GetComponentsInChildren<CheckpointFlag>(true))
            {
                if (checkpoint.GetComponentInParent<LevelRegion>() != this) continue;
                for (Transform owner = checkpoint.transform; owner != transform; owner = owner.parent)
                    if (owner.name.EndsWith(" " + entry.Id, System.StringComparison.Ordinal))
                        return EntryCheckpoint = checkpoint;
            }
            return null;
        }

        public Vector2 EntrySpawnPosition
        {
            get
            {
                CheckpointFlag entry = ResolveEntryCheckpoint();
                if (entry != null) return entry.SpawnPosition;
                LevelPlacement placement = Source != null ? Source.EntryPlacement : null;
                if (placement?.Brush != null && placement.Brush.HandlerId == "checkpoint"
                    && placement.Settings is CheckpointPlacementSettings settings)
                    return (Vector2)transform.TransformPoint(((Vector2)placement.Cell + placement.Brush.Anchor) * UnitSize)
                        + settings.SpawnOffset * UnitSize;
                return transform.position;
            }
        }

        // The buffer is measured in authored grid units, including around concave boundaries.
        public bool ContainsWorldPoint(Vector2 point, float buffer = 0f)
        {
            Vector2 local = (Vector2)transform.InverseTransformPoint(point) / Mathf.Max(0.0001f, UnitSize);
            if (Source == null)
                return local.x >= -buffer && local.x < Size.x + buffer
                    && local.y >= -buffer && local.y < Size.y + buffer;
            LevelFootprint footprint = Source.Footprint;
            Vector2Int cell = Vector2Int.FloorToInt(local);
            if (footprint.Contains(cell)) return true;
            if (buffer <= 0f) return false;
            int reach = Mathf.CeilToInt(buffer);
            for (int y = cell.y - reach; y <= cell.y + reach; y++)
                for (int x = cell.x - reach; x <= cell.x + reach; x++)
                {
                    if (!footprint.Contains(new Vector2Int(x, y))) continue;
                    Vector2 closest = new Vector2(Mathf.Clamp(local.x, x, x + 1), Mathf.Clamp(local.y, y, y + 1));
                    if ((closest - local).sqrMagnitude <= buffer * buffer) return true;
                }
            return false;
        }

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
