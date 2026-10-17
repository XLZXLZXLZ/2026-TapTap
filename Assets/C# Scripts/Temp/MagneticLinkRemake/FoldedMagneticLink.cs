using UnityEngine;
using UnityEngine.Rendering;

namespace TapTap.MagneticLinkRemake
{
    // Presentation only: endpoints, tension and a caller-owned clock are the complete input.
    public sealed class FoldedMagneticLink : MonoBehaviour
    {
        [SerializeField, Min(0.001f)] private float coreWidth = 0.022f;
        [SerializeField, Min(0f)] private float bendWidth = 0.17f;
        [SerializeField, Range(2, 10)] private int waveCount = 5;
        [SerializeField, Min(0f)] private float flowSpeed = 1.6f;
        [SerializeField, Range(0f, 1f)] private float irregularity = 0.7f;
        [SerializeField, ColorUsage(true, true)] private Color coreColor = new Color(4.7f, 3.8f, 0.15f, 1f);
        private readonly Vector3[] points = new Vector3[41];
        private readonly float[] lengths = new float[41];
        private readonly Vector3[] pair = new Vector3[2];
        private LineRenderer core, sheath, halo;
        private readonly LineRenderer[] terminals = new LineRenderer[2];
        private readonly LineRenderer[] pulses = new LineRenderer[2];
        private Material material;
        private MaterialPropertyBlock properties;
        private float totalLength;
        private float flowPhase;
        private float widthVisibility = 1f;
        public bool Ready => material != null;

        private void Awake()
        {
            Shader shader = Resources.Load<Shader>("MagneticLinkRemake/Filament");
            if (shader == null)
            {
                Debug.LogError("Missing MagneticLinkRemake/Filament shader.", this);
                enabled = false;
                return;
            }
            material = new Material(shader) { name = "Folded magnetic filament", hideFlags = HideFlags.HideAndDontSave };
            properties = new MaterialPropertyBlock();
            halo = MakeLine("Soft field", points.Length);
            sheath = MakeLine("Yellow sheath", points.Length);
            core = MakeLine("Bright core", points.Length);
            core.widthCurve = new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.12f, 1f),
                new Keyframe(0.88f, 1f), new Keyframe(1f, 0.5f));
            for (int i = 0; i < 2; i++)
            {
                terminals[i] = MakeLine("Pole glow " + i, 2);
                pulses[i] = MakeLine("Flux streak " + i, 2);
            }
        }

        private LineRenderer MakeLine(string label, int count)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            child.layer = gameObject.layer;
            var line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = count;
            line.numCornerVertices = 5;
            line.numCapVertices = 6;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.sortingOrder = 10;
            line.enabled = false;
            return line;
        }

        public void Render(Vector3 from, Vector3 to, float visibility, float tension, double clock, float unit)
        {
            if (!Ready) return;
            float distance = Vector3.Distance(from, to);
            bool visible = visibility > 0.001f && distance > 0.025f * unit;
            core.enabled = sheath.enabled = halo.enabled = visible;
            for (int i = 0; i < 2; i++) terminals[i].enabled = pulses[i].enabled = visible;
            if (!visible) return;
            widthVisibility = Mathf.Pow(Mathf.Clamp01(visibility), 0.45f);

            // Repeat a double clock before conversion, retaining smooth motion in long sessions.
            float time = (float)(clock % 4096d);
            Vector3 axis = (to - from) / distance;
            Vector3 side = Vector3.Cross(axis, Vector3.forward).normalized;
            if (side.sqrMagnitude < 0.1f) side = Vector3.right;
            float bend = bendWidth * unit * Mathf.Clamp01(distance / (unit * 1.5f))
                * Mathf.Lerp(1f, 0.55f, tension);
            flowPhase = (float)((clock * flowSpeed) % 1d);
            totalLength = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                float t = (float)i / (points.Length - 1);
                // Carry nonuniform folds along the link. Noise is continuous and clock-based,
                // so pause/rewind remain stable without generating random positions each frame.
                double travelling = t * waveCount - clock * flowSpeed;
                float warp = Noise(travelling * 0.73d, 17u) * irregularity * 0.4f;
                warp += Noise(t * waveCount * 0.85d + clock * 0.31d, 53u) * irregularity * 0.14f;
                float phase = (float)(travelling - System.Math.Floor(travelling));
                float triangle = 1f - 4f * Mathf.Abs(Mathf.Repeat(phase + warp, 1f) - 0.5f);
                float envelope = Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f - t) / 0.09f);
                float surge = 0.9f + Mathf.Sin(t * Mathf.PI * 2f - time * 3.2f) * 0.1f;
                float amplitude = 1f + Noise(travelling * 1.37d + 4.6d, 97u) * irregularity * 0.6f;
                float bias = Noise(travelling * 0.43d - 2.8d, 131u) * irregularity * 0.3f;
                points[i] = Vector3.Lerp(from, to, t) + side * ((triangle * amplitude + bias) * bend * envelope * surge);
                if (i > 0) totalLength += Vector3.Distance(points[i - 1], points[i]);
                lengths[i] = totalLength;
            }
            float breath = (0.94f + Mathf.Sin(time * 2.2f) * 0.06f) * visibility;
            float power = Mathf.Lerp(1f, 1.45f, tension);
            SetFilament(halo, coreWidth * unit * 7f, new Color(1.1f, 0.8f, 0.02f, breath * 0.16f), 2.5f);
            SetFilament(sheath, coreWidth * unit * 2.4f, new Color(1.6f, 1.25f, 0.04f, breath * 0.48f), 1.5f);
            Color light = coreColor * power;
            light.a = breath;
            SetFilament(core, coreWidth * unit, light, 0.7f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 pole = i == 0 ? from : to;
                pair[0] = pole - side * (0.008f * unit);
                pair[1] = pole + side * (0.008f * unit);
                terminals[i].SetPositions(pair);
                Style(terminals[i], unit * Mathf.Lerp(0.075f, 0.095f, tension),
                    new Color(3f, 2.4f, 0.08f, visibility * 0.65f), 2f);

                float phase = Mathf.Repeat((float)((clock * (1.1d + tension) * unit / totalLength) % 1d)
                    * (tension > 0.5f ? -1f : 1f) + i * 0.5f, 1f);
                float trail = Mathf.Min(0.09f, unit * 0.13f / totalLength);
                float end = Mathf.Clamp01(phase + (tension > 0.5f ? trail : -trail));
                pair[0] = Sample(phase);
                pair[1] = Sample(end);
                pulses[i].SetPositions(pair);
                Style(pulses[i], coreWidth * unit * 1.8f,
                    new Color(5.2f, 4.4f, 0.2f, visibility * Mathf.Sin(phase * Mathf.PI) * 0.8f), 1f);
            }
        }

        private static float Noise(double coordinate, uint seed)
        {
            long cell = (long)System.Math.Floor(coordinate);
            float t = (float)(coordinate - cell);
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(Hash(unchecked((uint)cell) ^ seed),
                Hash(unchecked((uint)(cell + 1)) ^ seed), t) * 2f - 1f;
        }

        private static float Hash(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                value ^= value >> 16;
            }
            return (value & 0x00ffffffu) / 16777215f;
        }

        private Vector3 Sample(float fraction)
        {
            float target = fraction * totalLength;
            for (int i = 1; i < lengths.Length; i++)
                if (target <= lengths[i])
                    return Vector3.Lerp(points[i - 1], points[i], Mathf.InverseLerp(lengths[i - 1], lengths[i], target));
            return points[points.Length - 1];
        }

        private void SetFilament(LineRenderer line, float width, Color color, float softness)
        {
            line.SetPositions(points);
            Style(line, width, color, softness);
        }

        private void Style(LineRenderer line, float width, Color color, float softness)
        {
            line.widthMultiplier = width * widthVisibility;
            properties.Clear();
            properties.SetColor("_Tint", color);
            properties.SetFloat("_Softness", softness);
            properties.SetFloat("_FlowPhase", flowPhase);
            properties.SetFloat("_BandCount", waveCount);
            properties.SetFloat("_FlowStrength", line == core ? 0.4f : line == sheath ? 0.25f : 0f);
            line.SetPropertyBlock(properties);
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
