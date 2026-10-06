using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    public sealed class LevelRegion : MonoBehaviour
    {
        public Vector2Int Size = new Vector2Int(32, 18);
        [Min(0.01f)] public float UnitSize = 1f;
        public LevelDefinition Source;
        public CheckpointFlag EntryCheckpoint;

        public Bounds WorldBounds
        {
            get
            {
                Vector3 size = new Vector3(Size.x * UnitSize, Size.y * UnitSize, 0f);
                var bounds = new Bounds(transform.TransformPoint(Vector3.zero), Vector3.zero);
                bounds.Encapsulate(transform.TransformPoint(new Vector3(size.x, 0f, 0f)));
                bounds.Encapsulate(transform.TransformPoint(new Vector3(0f, size.y, 0f)));
                bounds.Encapsulate(transform.TransformPoint(size));
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
            Gizmos.DrawWireCube(size * 0.5f, size);
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
