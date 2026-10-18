using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace ReusableTextEffects
{
    /// <summary>
    /// Splits a TMP text visually at mesh level and animates every visible glyph.
    /// Supports TextMeshProUGUI and world-space TextMeshPro without cloning objects.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("TextMeshPro/Effects/TMP Text Shatter")]
    public sealed class TMPTextShatter : MonoBehaviour
    {
        public enum CharacterOrder
        {
            LeftToRight,
            RightToLeft,
            Random,
            Simultaneous
        }

        [Header("Source")]
        [SerializeField] private TMP_Text source;
        [SerializeField] private bool playOnEnable;
        [SerializeField] private bool useUnscaledTime = true;
        [SerializeField] private bool hideTextOnComplete = true;

        [Header("Timing")]
        [SerializeField] private Vector2 durationRange = new Vector2(0.42f, 0.62f);
        [SerializeField, Min(0f)] private float characterDelay = 0.018f;
        [SerializeField] private CharacterOrder characterOrder = CharacterOrder.LeftToRight;

        [Header("Movement (TMP local units)")]
        [SerializeField] private Vector2 launchDirection = Vector2.down;
        [SerializeField] private Vector2 launchSpeedRange = new Vector2(850f, 1150f);
        [SerializeField, Min(0f)] private float perpendicularScatter = 180f;
        [SerializeField] private Vector2 gravity = new Vector2(0f, -900f);
        [SerializeField] private Vector2 rotationRange = new Vector2(-100f, 100f);

        [Header("Appearance")]
        [SerializeField, Min(0f)] private float endScale = 0.75f;
        [SerializeField, Range(0f, 1f)] private float fadeStart = 0.65f;
        [SerializeField] private AnimationCurve scaleCurve =
            new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));

        [Header("Randomness")]
        [Tooltip("0 uses a different seed each play. Any other value is deterministic.")]
        [SerializeField] private int randomSeed;

        [Header("Events")]
        [SerializeField] private UnityEvent onShatterStarted;
        [SerializeField] private UnityEvent onShatterCompleted;

        private readonly List<GlyphShard> shards = new List<GlyphShard>();
        private TMP_MeshInfo[] originalMeshInfo;
        private bool prepared;
        private bool playing;
        private float elapsed;
        private float totalDuration;
        private string preparedText;
        private float? finishBelowY;

        public TMP_Text Source => source;
        public bool IsPlaying => playing;
        public float TotalDuration => totalDuration;

        private sealed class GlyphShard
        {
            public int materialIndex;
            public int vertexIndex;
            public Vector3 center;
            public Vector2 velocity;
            public float rotation;
            public float delay;
            public float duration;
        }

        private void Awake()
        {
            ResolveSource();
        }

        private void OnEnable()
        {
            if (playOnEnable && Application.isPlaying) Shatter();
        }

        private void OnDisable()
        {
            playing = false;
        }

        private void Update()
        {
            if (!playing) return;

            elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            RenderFrame(elapsed);
            if (elapsed < totalDuration) return;

            playing = false;
            if (hideTextOnComplete && source != null) source.enabled = false;
            onShatterCompleted?.Invoke();
        }

        /// <summary>Changes the target TMP component and clears cached geometry.</summary>
        public void Configure(TMP_Text target)
        {
            if (source == target) return;
            StopAndRestore();
            source = target;
            prepared = false;
        }

        /// <summary>Caches the current text geometry. Safe to call before an animation event.</summary>
        public void Prepare()
        {
            ResolveSource();
            if (source == null) return;
            if (prepared && preparedText == source.text && !source.havePropertiesChanged) return;

            playing = false;
            source.enabled = true;
            source.ForceMeshUpdate(true, true);
            originalMeshInfo = source.textInfo.CopyMeshInfoVertexData();
            shards.Clear();

            TMP_TextInfo textInfo = source.textInfo;
            for (int i = 0; i < textInfo.characterCount; i++)
            {
                TMP_CharacterInfo character = textInfo.characterInfo[i];
                if (!character.isVisible) continue;

                shards.Add(new GlyphShard
                {
                    materialIndex = character.materialReferenceIndex,
                    vertexIndex = character.vertexIndex,
                    center = (character.bottomLeft + character.topRight) * 0.5f
                });
            }

            preparedText = source.text;
            prepared = true;
            BuildAnimationData();
        }

        /// <summary>Starts the shatter. Can be called by a Button, UnityEvent, or Animation Event.</summary>
        public void Shatter()
        {
            Play(null);
        }

        /// <summary>
        /// Keeps glyphs opaque until they have fallen completely below a local-space boundary.
        /// Requires downward gravity; the ordinary Shatter API keeps its existing timing and fade.
        /// </summary>
        public void ShatterBelow(float localBottomY)
        {
            Play(gravity.y < -0.0001f ? (float?)localBottomY : null);
        }

        private void Play(float? bottom)
        {
            ResolveSource();
            if (source == null || playing) return;

            finishBelowY = bottom;
            if (!prepared || preparedText != source.text || source.havePropertiesChanged) Prepare();
            else BuildAnimationData();
            if (shards.Count == 0) return;

            source.enabled = true;
            elapsed = 0f;
            playing = true;
            RestoreOriginalMesh();
            onShatterStarted?.Invoke();
        }

        /// <summary>Stops the effect and restores the intact text for reuse.</summary>
        public void StopAndRestore()
        {
            playing = false;
            elapsed = 0f;
            finishBelowY = null;
            if (source == null) return;

            source.enabled = true;
            if (prepared) RestoreOriginalMesh();
            else source.ForceMeshUpdate(true, true);
        }

        /// <summary>Invalidates cached geometry after changing text or layout.</summary>
        public void Rebuild()
        {
            prepared = false;
            Prepare();
        }

        private void ResolveSource()
        {
            if (source == null) source = GetComponent<TMP_Text>();
        }

        private void BuildAnimationData()
        {
            int count = shards.Count;
            if (count == 0)
            {
                totalDuration = 0f;
                return;
            }

            int seed = randomSeed == 0 ? Environment.TickCount : randomSeed;
            System.Random random = new System.Random(seed);
            int[] ranks = BuildDelayRanks(count, random);
            Vector2 direction = launchDirection.sqrMagnitude > 0.0001f
                ? launchDirection.normalized
                : Vector2.down;
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            float minDuration = Mathf.Max(0.01f, Mathf.Min(durationRange.x, durationRange.y));
            float maxDuration = Mathf.Max(minDuration, Mathf.Max(durationRange.x, durationRange.y));
            float minSpeed = Mathf.Min(launchSpeedRange.x, launchSpeedRange.y);
            float maxSpeed = Mathf.Max(launchSpeedRange.x, launchSpeedRange.y);
            float longest = 0f;

            for (int i = 0; i < count; i++)
            {
                GlyphShard shard = shards[i];
                float speed = Range(random, minSpeed, maxSpeed);
                float scatter = Range(random, -perpendicularScatter, perpendicularScatter);
                shard.velocity = direction * speed + perpendicular * scatter;
                shard.rotation = Range(random,
                    Mathf.Min(rotationRange.x, rotationRange.y),
                    Mathf.Max(rotationRange.x, rotationRange.y));
                shard.duration = Range(random, minDuration, maxDuration);
                if (finishBelowY.HasValue)
                {
                    Vector3[] original = originalMeshInfo[shard.materialIndex].vertices;
                    float radius = 0f;
                    for (int corner = 0; corner < 4; corner++)
                        radius = Mathf.Max(radius, (original[shard.vertexIndex + corner] - shard.center).magnitude);
                    radius *= Mathf.Max(1f, endScale);
                    float distance = Mathf.Max(0f, shard.center.y + radius - finishBelowY.Value);
                    float acceleration = -gravity.y;
                    float fallTime = (shard.velocity.y + Mathf.Sqrt(shard.velocity.y * shard.velocity.y
                        + 2f * acceleration * distance)) / acceleration;
                    shard.duration = Mathf.Max(shard.duration, fallTime);
                }
                shard.delay = characterOrder == CharacterOrder.Simultaneous
                    ? 0f
                    : ranks[i] * characterDelay;
                longest = Mathf.Max(longest, shard.delay + shard.duration);
            }

            totalDuration = longest;
        }

        private int[] BuildDelayRanks(int count, System.Random random)
        {
            int[] ranks = new int[count];
            switch (characterOrder)
            {
                case CharacterOrder.RightToLeft:
                    for (int i = 0; i < count; i++) ranks[i] = count - 1 - i;
                    break;
                case CharacterOrder.Random:
                    int[] order = new int[count];
                    for (int i = 0; i < count; i++) order[i] = i;
                    for (int i = count - 1; i > 0; i--)
                    {
                        int other = random.Next(i + 1);
                        int temp = order[i];
                        order[i] = order[other];
                        order[other] = temp;
                    }
                    for (int rank = 0; rank < count; rank++) ranks[order[rank]] = rank;
                    break;
                default:
                    for (int i = 0; i < count; i++) ranks[i] = i;
                    break;
            }
            return ranks;
        }

        private void RenderFrame(float time)
        {
            if (source == null || originalMeshInfo == null) return;

            RestoreOriginalMesh(false);
            TMP_TextInfo textInfo = source.textInfo;

            foreach (GlyphShard shard in shards)
            {
                float localTime = time - shard.delay;
                if (localTime <= 0f) continue;

                float normalized = Mathf.Clamp01(localTime / shard.duration);
                float scaleT = scaleCurve == null ? normalized : scaleCurve.Evaluate(normalized);
                float scale = Mathf.LerpUnclamped(1f, endScale, scaleT);
                float radians = shard.rotation * normalized * Mathf.Deg2Rad;
                float sin = Mathf.Sin(radians);
                float cos = Mathf.Cos(radians);
                Vector2 offset = shard.velocity * localTime + 0.5f * gravity * localTime * localTime;

                Vector3[] vertices = textInfo.meshInfo[shard.materialIndex].vertices;
                Color32[] colors = textInfo.meshInfo[shard.materialIndex].colors32;
                Vector3[] originals = originalMeshInfo[shard.materialIndex].vertices;
                Color32[] originalColors = originalMeshInfo[shard.materialIndex].colors32;

                float alpha = finishBelowY.HasValue || normalized <= fadeStart
                    ? 1f
                    : 1f - Mathf.InverseLerp(fadeStart, 1f, normalized);

                for (int corner = 0; corner < 4; corner++)
                {
                    int vertex = shard.vertexIndex + corner;
                    Vector3 relative = (originals[vertex] - shard.center) * scale;
                    Vector3 rotated = new Vector3(
                        relative.x * cos - relative.y * sin,
                        relative.x * sin + relative.y * cos,
                        relative.z);
                    vertices[vertex] = shard.center + rotated + (Vector3)offset;

                    Color32 color = originalColors[vertex];
                    color.a = (byte)Mathf.RoundToInt(color.a * alpha);
                    colors[vertex] = color;
                }
            }

            source.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
        }

        private void RestoreOriginalMesh(bool upload = true)
        {
            if (source == null || originalMeshInfo == null) return;

            TMP_TextInfo textInfo = source.textInfo;
            int meshCount = Mathf.Min(textInfo.meshInfo.Length, originalMeshInfo.Length);
            for (int i = 0; i < meshCount; i++)
            {
                Array.Copy(originalMeshInfo[i].vertices, textInfo.meshInfo[i].vertices,
                    Mathf.Min(originalMeshInfo[i].vertices.Length, textInfo.meshInfo[i].vertices.Length));
                Array.Copy(originalMeshInfo[i].colors32, textInfo.meshInfo[i].colors32,
                    Mathf.Min(originalMeshInfo[i].colors32.Length, textInfo.meshInfo[i].colors32.Length));
            }

            if (upload)
                source.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
        }

        private static float Range(System.Random random, float min, float max)
        {
            return Mathf.Lerp(min, max, (float)random.NextDouble());
        }

        private void OnValidate()
        {
            durationRange.x = Mathf.Max(0.01f, durationRange.x);
            durationRange.y = Mathf.Max(0.01f, durationRange.y);
            characterDelay = Mathf.Max(0f, characterDelay);
            perpendicularScatter = Mathf.Max(0f, perpendicularScatter);
            endScale = Mathf.Max(0f, endScale);
        }
    }
}
