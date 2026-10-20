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
        private readonly List<Material> transparencyMaterials = new List<Material>();
        private bool touched;
        public bool IsTransparent => touched;
        public event Action<MovableEntity> HeadPassedUpward;
        public event Action<MovableEntity> HeadLanded;

        private void LateUpdate()
        {
            if (touched || RewindManager.Rewinding) return;

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
                touched = true;
                ApplyTransparency(transform);
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
