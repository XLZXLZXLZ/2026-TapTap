using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap
{
    [DisallowMultipleComponent]
    [AddComponentMenu("TapTap/World/Level Endpoint")]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class LevelEndpoint : MonoBehaviour
    {
        [Tooltip("触及终点后完成当前关，按大世界序号推进。")]
        public string EndpointId = "finish";
        public string Label = "终点";
        [Header("入门演出（锚点按脚底基准定位）")]
        [SerializeField] private Transform frontAnchor;
        [SerializeField] private Transform insideAnchor;
        [Min(0.1f), Tooltip("自动行走速度，单位为格/秒。")]
        public float EntryWalkSpeed = 2.5f;
        [Min(0.02f)] public float EntryRetractDuration = 0.25f;
        [Min(0.02f)] public float EntryTurnDuration = 0.16f;
        [Min(0f)] public float EntryHoldDuration = 0.18f;
        private BoxCollider2D trigger;
        private MaterialPropertyBlock returnColors;
        private sealed class ExternalLightCache
        {
            public int RefreshFrame = -1;
            public readonly List<Light> Lights = new List<Light>();
            public readonly List<CheckpointFlag> Checkpoints = new List<CheckpointFlag>();
        }
        private static readonly Dictionary<int, ExternalLightCache> externalLightCaches = new Dictionary<int, ExternalLightCache>();
        private static readonly int[] ColorProperties =
        {
            Shader.PropertyToID("_BaseColor"), Shader.PropertyToID("_Color"),
            Shader.PropertyToID("_EmissionColor"), Shader.PropertyToID("_TintColor")
        };

        public Vector3 FrontFootPosition => frontAnchor != null ? frontAnchor.position : transform.position;
        public Vector3 InsideFootPosition(float unit) => insideAnchor != null ? insideAnchor.position
            : FrontFootPosition + transform.forward.normalized * unit;
        public void ConfigureEntryAnchors(Transform front, Transform inside) { frontAnchor = front; insideAnchor = inside; }

        private void OnDrawGizmosSelected()
        {
            float unit = GetComponentInParent<LevelRegion>() != null ? GetComponentInParent<LevelRegion>().UnitSize : 1f;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(FrontFootPosition, 0.08f * unit);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(InsideFootPosition(unit), 0.08f * unit);
            Gizmos.DrawLine(FrontFootPosition, InsideFootPosition(unit));
        }

        private void Awake() => EnsureTrigger();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLightCaches()
        {
            externalLightCaches.Clear();
            SceneManager.sceneUnloaded -= ClearLightCache;
            SceneManager.sceneUnloaded += ClearLightCache;
        }
        private static void ClearLightCache(Scene scene) => externalLightCaches.Remove(scene.handle);
        private void Start()
        {
            // Populate once after the scene/region hierarchy has been assembled, before return begins.
            GetExternalLights(gameObject.scene, true);
            ApplyReturnColors();
        }
        private void OnEnable()
        {
            EventManager.Subscribe<GameStageChanged>(StageChanged);
            ApplyReturnColors();
        }
        private void OnDisable() => EventManager.Unsubscribe<GameStageChanged>(StageChanged);
        private void StageChanged(GameStageChanged message)
        {
            if (message.Scene == gameObject.scene && message.Stage >= GameStage.Returning) ApplyReturnColors();
        }
        private void ApplyReturnColors()
        {
            if (!GameStageProgression.HasProgressedIn(gameObject.scene)) return;
            if (returnColors == null) returnColors = new MaterialPropertyBlock();
            foreach (Renderer view in GetComponentsInChildren<Renderer>(true))
            {
                if (view is SpriteRenderer sprite && TryUnavailableRed(sprite.color, out Color tint)) sprite.color = tint;
                Material[] materials = view.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    if (material == null) continue;
                    view.GetPropertyBlock(returnColors, slot);
                    bool changed = false;
                    foreach (int property in ColorProperties)
                    {
                        if (!material.HasProperty(property) || !TryUnavailableRed(material.GetColor(property), out Color red)) continue;
                        returnColors.SetColor(property, red);
                        changed = true;
                    }
                    // Per-instance overrides preserve shared materials and unrelated shader properties.
                    if (changed) view.SetPropertyBlock(returnColors, slot);
                }
            }
            foreach (Light light in GetComponentsInChildren<Light>(true))
                if (TryUnavailableRed(light.color, out Color red)) light.color = red;
            ApplyExternalReturnLights();
        }
        private void ApplyExternalReturnLights()
        {
            // The world artwork stores the portal's point lights under Arts, outside this prefab.
            // Match in gameplay XY: artwork and gameplay markers deliberately use different Z depths.
            float unit = GetComponentInParent<LevelRegion>()?.UnitSize ?? 1f;
            ExternalLightCache cache = GetExternalLights(gameObject.scene);
            foreach (Light light in cache.Lights)
            {
                if (light == null || light.transform.IsChildOf(transform) || !TryUnavailableRed(light.color, out Color red)) continue;
                Vector2 position = light.transform.position;
                float distance = Vector2.Distance(position, FrontFootPosition);
                if (distance > unit * 2f) continue;
                bool belongsToEntry = false;
                foreach (CheckpointFlag checkpoint in cache.Checkpoints)
                    if (checkpoint != null && Vector2.Distance(position, checkpoint.transform.position) < distance)
                    { belongsToEntry = true; break; }
                if (!belongsToEntry) light.color = red;
            }
        }
        private static ExternalLightCache GetExternalLights(Scene scene, bool refresh = false)
        {
            if (!externalLightCaches.TryGetValue(scene.handle, out ExternalLightCache cache))
                externalLightCaches.Add(scene.handle, cache = new ExternalLightCache());
            if (cache.RefreshFrame >= 0 && (!refresh || cache.RefreshFrame == Time.frameCount)) return cache;
            cache.Lights.Clear();
            cache.Checkpoints.Clear();
            foreach (GameObject root in scene.GetRootGameObjects())
                cache.Lights.AddRange(root.GetComponentsInChildren<Light>(true));
            foreach (CheckpointFlag checkpoint in FindObjectsOfType<CheckpointFlag>(true))
                if (checkpoint.gameObject.scene == scene) cache.Checkpoints.Add(checkpoint);
            cache.RefreshFrame = Time.frameCount;
            return cache;
        }
        private static bool TryUnavailableRed(Color source, out Color red)
        {
            red = source;
            if (source.g <= Mathf.Max(source.r, source.b) + 0.01f) return false;
            Color.RGBToHSV(source, out float hue, out float saturation, out float brightness);
            // Warm greens become warm reds; cooler greens retain a slight crimson tint.
            red = Color.HSVToRGB(Mathf.Repeat((1f / 3f - hue) * 0.2f, 1f), saturation, brightness, true);
            red.a = source.a;
            return true;
        }
        private void Reset()
        {
            EnsureTrigger();
            trigger.size = new Vector2(0.8f, 1f);
            trigger.offset = new Vector2(0f, 0.5f);
        }

        public void EnsureTrigger()
        {
            trigger = GetComponent<BoxCollider2D>();
            if (trigger == null)
            {
                trigger = gameObject.AddComponent<BoxCollider2D>();
                trigger.size = new Vector2(0.8f, 1f);
                trigger.offset = new Vector2(0f, 0.5f);
            }
            trigger.isTrigger = true;
        }

        private void FixedUpdate()
        {
            WorldMapProgression flow = WorldMapProgression.Current;
            if (flow == null || !flow.ExitsEnabled || WorldMapProgression.InputBlocked || RewindManager.Rewinding) return;
            // The player uses a custom kinematic motor, so do not rely on Rigidbody trigger callbacks.
            foreach (MovableEntity entity in MovableEntity.ActiveEntities)
            {
                if (entity == null || entity.Part != EntityPart.Body || entity.IsReturning
                    || entity.GetComponentInParent<PlayerController>() == null) continue;
                if (trigger.IsTouching(entity.GetComponent<Collider2D>())
                    || TouchesBody(entity))
                {
                    flow.CompleteCurrent(this);
                    break;
                }
            }
        }

        private bool TouchesBody(MovableEntity entity)
        {
            Bounds actor = entity.Motor.CollisionBounds;
            if (trigger.bounds.Intersects(actor)) return true;
            Bounds expanded = trigger.bounds;
            expanded.Expand(actor.size);
            foreach (MotionSegment segment in entity.Motor.MotionSegments)
            {
                Vector2 delta = segment.To - segment.From;
                if (delta.sqrMagnitude < 0.000001f) continue;
                if (expanded.IntersectRay(new Ray(segment.From + entity.Motor.CenterOffset, delta.normalized), out float distance)
                    && distance <= delta.magnitude) return true;
            }
            return false;
        }
    }
}
