using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace TapTap
{
    // Full-square geometry uses exactly the same one-way rules as a thin platform.
    // Animation consumers can subscribe here without introducing another surface kind.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D), typeof(WorldSurface))]
    public sealed class SquarePlatform : MonoBehaviour
    {
        [Serializable] public sealed class HeadEvent : UnityEvent<MovableEntity> { }
        [SerializeField] private HeadEvent onHeadPassedUpward = new HeadEvent();
        [SerializeField] private HeadEvent onHeadLanded = new HeadEvent();
        [SerializeField] private Vector2 transparencyTriggerSize = new Vector2(1f, 0.5f);
        [SerializeField] private Vector2 transparencyTriggerOffset = Vector2.zero;
        [SerializeField, Range(0f, 1f)] private float touchedOpacity = 0.5f;
        [Tooltip("相邻方形平台的中心距离（世界单位）。1.05 可连接单位网格的上下左右，不连接斜角。")]
        [SerializeField, Min(0.01f)] private float neighborDistance = 1.05f;
        private const float BucketSize = 1f;
        private static readonly Dictionary<Vector2Int, List<SquarePlatform>> buckets =
            new Dictionary<Vector2Int, List<SquarePlatform>>();
        private static int adjacencyVersion;
        private readonly List<SquarePlatform> neighbors = new List<SquarePlatform>();
        private readonly List<SquarePlatform> pendingTransparency = new List<SquarePlatform>();
        private Vector2Int registeredCell;
        private int cachedAdjacencyVersion = -1;
        private readonly List<Material> transparencyMaterials = new List<Material>();
        private bool touched;
        public bool IsTransparent => touched;
        public event Action<MovableEntity> HeadPassedUpward;
        public event Action<MovableEntity> HeadLanded;

        private static Vector2Int Cell(Vector2 position) => Vector2Int.FloorToInt(position / BucketSize);

        private void OnEnable()
        {
            registeredCell = Cell(transform.position);
            if (!buckets.TryGetValue(registeredCell, out List<SquarePlatform> bucket))
                buckets.Add(registeredCell, bucket = new List<SquarePlatform>());
            bucket.Add(this);
            adjacencyVersion++;
        }

        private void OnDisable()
        {
            if (buckets.TryGetValue(registeredCell, out List<SquarePlatform> bucket))
            {
                bucket.Remove(this);
                if (bucket.Count == 0) buckets.Remove(registeredCell);
            }
            neighbors.Clear();
            adjacencyVersion++;
        }

        // All initially enabled tiles are registered before Start. Build adjacency
        // here, so contact only traverses cached links in the normal static level.
        private void Start() => CacheNeighbors();

        private void CacheNeighbors()
        {
            if (cachedAdjacencyVersion == adjacencyVersion) return;
            neighbors.Clear();
            cachedAdjacencyVersion = adjacencyVersion;
            Vector2 position = transform.position;
            float radius = Mathf.Max(0.01f, neighborDistance);
            Vector2Int min = Cell(position - Vector2.one * radius);
            Vector2Int max = Cell(position + Vector2.one * radius);
            for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    if (!buckets.TryGetValue(new Vector2Int(x, y), out List<SquarePlatform> bucket)) continue;
                    foreach (SquarePlatform other in bucket)
                        if (other != this && other != null && other.gameObject.scene == gameObject.scene
                            && ((Vector2)other.transform.position - position).sqrMagnitude <= radius * radius)
                            neighbors.Add(other);
                }
        }

        /// <summary>Call after moving a static tile or changing its neighbor distance at runtime.</summary>
        public void RefreshAdjacency()
        {
            if (!isActiveAndEnabled) return;
            OnDisable();
            OnEnable();
            CacheNeighbors();
        }

        public void TriggerTransparency()
        {
            if (touched || !isActiveAndEnabled) return;
            pendingTransparency.Clear();
            touched = true;
            pendingTransparency.Add(this);
            // Mark when queued: cycles and multiple paths cannot enqueue a tile
            // twice. An iterative traversal also avoids recursive stack overflow.
            try
            {
                for (int i = 0; i < pendingTransparency.Count; i++)
                {
                    SquarePlatform tile = pendingTransparency[i];
                    tile.CacheNeighbors();
                    tile.ApplyTransparency(tile.transform);
                    foreach (SquarePlatform neighbor in tile.neighbors)
                    {
                        if (neighbor == null || !neighbor.isActiveAndEnabled || neighbor.touched) continue;
                        neighbor.touched = true;
                        pendingTransparency.Add(neighbor);
                    }
                }
            }
            finally { pendingTransparency.Clear(); }
        }

        private void LateUpdate()
        {
            if (touched || RewindManager.Rewinding) return;
            CacheNeighbors();

            // Use the motor's authoritative position: Unity's kinematic rigidbody may
            // still be waiting for MovePosition to be applied by the physics step.
            Vector2 center = transform.TransformPoint(transparencyTriggerOffset);
            Vector3 scale = transform.lossyScale;
            Vector2 size = new Vector2(transparencyTriggerSize.x * Mathf.Abs(scale.x),
                transparencyTriggerSize.y * Mathf.Abs(scale.y));
            Rect region = new Rect(center - size * 0.5f, size);
            foreach (MovableEntity entity in MovableEntity.ActiveEntities)
            {
                if (entity == null || entity.IsReturning ||
                    (entity.Part != EntityPart.Head && entity.Part != EntityPart.Body)) continue;
                KinematicMotor2D motor = entity.Motor;
                if (!motor.CollisionsEnabled) continue;
                Bounds bounds = motor.PartBounds;
                if (!region.Overlaps(new Rect((Vector2)bounds.min, (Vector2)bounds.size))) continue;
                TriggerTransparency();
                break;
            }
        }

        internal void ApplyTransparency(Transform visualRoot)
        {
            if (!touched) return;
            foreach (Renderer renderer in visualRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is SpriteRenderer sprite)
                {
                    Color color = sprite.color;
                    color.a *= touchedOpacity;
                    sprite.color = color;
                    continue;
                }

                // The level styles also replace the sprite tiles with opaque 3D
                // models. Give this tile its own transparent materials.
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    Material material = new Material(materials[i]);
                    transparencyMaterials.Add(material);
                    materials[i] = material;
                    if (material.HasProperty("_BaseColor"))
                    {
                        Color color = material.GetColor("_BaseColor");
                        color.a *= touchedOpacity;
                        material.SetColor("_BaseColor", color);
                    }
                    else if (material.HasProperty("_Color"))
                    {
                        Color color = material.GetColor("_Color");
                        color.a *= touchedOpacity;
                        material.SetColor("_Color", color);
                    }
                    if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
                    if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 2f);
                    if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
                    if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
                    if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.DisableKeyword("_ALPHAMODULATE_ON");
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.renderQueue = (int)RenderQueue.Transparent;
                    material.SetShaderPassEnabled("ShadowCaster", false);
                }
                renderer.sharedMaterials = materials;
            }
        }

        private void OnDestroy()
        {
            foreach (Material material in transparencyMaterials)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.7f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(transparencyTriggerOffset, transparencyTriggerSize);
        }

        public void NotifyHeadPassedUpward(MovableEntity head)
        {
            if (!CanNotify(head)) return;
            HeadPassedUpward?.Invoke(head);
            onHeadPassedUpward.Invoke(head);
        }

        public void NotifyHeadLanded(MovableEntity head)
        {
            if (!CanNotify(head)) return;
            HeadLanded?.Invoke(head);
            onHeadLanded.Invoke(head);
        }

        private static bool CanNotify(MovableEntity head) => head != null && head.Part == EntityPart.Head
            && !head.IsReturning && !RewindManager.Rewinding;

        private void Reset()
        {
            GetComponent<WorldSurface>().Configure(SurfaceKind.OneWay);
            BoxCollider2D box = GetComponent<BoxCollider2D>();
            box.size = Vector2.one;
            box.offset = Vector2.zero;
        }
    }
}
