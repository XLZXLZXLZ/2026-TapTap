using UnityEngine;

namespace TapTap
{
    public sealed class MagneticConnectionView : MonoBehaviour
    {
        [SerializeField] private LineRenderer line;
        [SerializeField] private SpriteRenderer[] dots;
        [SerializeField, Min(0f)] private float amplitude = 0.075f;
        [SerializeField, Min(0f)] private float speed = 2.1f;
        [SerializeField, Range(3, 64)] private int segments = 18;
        private Vector3[] points;
        private Vector3[] dotScales;
        private Color[] dotColors;
        private GradientColorKey[] colors;
        private GradientAlphaKey[] opacity;
        private Gradient faded;
        private float width;
        private float alpha;

        public void Configure(LineRenderer renderer, SpriteRenderer[] motes, float waveAmplitude, float waveSpeed, int pointSegments)
        { line = renderer; dots = motes; amplitude = waveAmplitude; speed = waveSpeed; segments = pointSegments; }

        private void Initialize()
        {
            if (points != null || line == null) return;
            points = new Vector3[Mathf.Clamp(segments, 3, 64) + 1];
            line.positionCount = points.Length;
            width = line.widthMultiplier;
            colors = line.colorGradient.colorKeys;
            opacity = line.colorGradient.alphaKeys;
            faded = new Gradient();
            faded.mode = line.colorGradient.mode;
            if (dots == null) dots = new SpriteRenderer[0];
            dotScales = new Vector3[dots.Length]; dotColors = new Color[dots.Length];
            for (int i = 0; i < dots.Length; i++)
                if (dots[i] != null) { dotScales[i] = dots[i].transform.localScale; dotColors[i] = dots[i].color; }
        }

        public void Render(MovableEntity body, MovableEntity head, bool connected, bool pulling, float unit, bool immediate = false)
        {
            Initialize();
            if (line == null || points == null) return;
            alpha = immediate ? (connected ? 1f : 0f) : Mathf.MoveTowards(alpha, connected ? 1f : 0f, Time.deltaTime * 8f);
            Transform lowerVisual = body.Visual != null ? body.Visual : body.transform;
            Transform upperVisual = head.Visual != null ? head.Visual : head.transform;
            Vector3 from = lowerVisual.position + lowerVisual.up * body.Motor.Size.y * 0.42f;
            Vector3 to = upperVisual.position - upperVisual.up * head.Motor.Size.y * 0.42f;
            Vector3 direction = to - from;
            Vector3 sideways = new Vector3(-direction.y, direction.x, 0f).normalized;
            float waveSize = amplitude * unit * Mathf.Clamp01(direction.magnitude / Mathf.Max(0.01f, unit));
            float time = Time.time * speed;
            for (int i = 0; i < points.Length; i++)
            {
                float t = (float)i / (points.Length - 1);
                float wave = Mathf.Sin(t * Mathf.PI * 5f - time * 3f) * 0.65f
                    + Mathf.Sin(t * Mathf.PI * 11f + time * 4.7f) * 0.25f + (i % 2 == 0 ? 0.15f : -0.15f);
                points[i] = Vector3.Lerp(from, to, t) + sideways * (wave * waveSize * Mathf.Sin(t * Mathf.PI));
            }
            GradientAlphaKey[] keys = faded.alphaKeys;
            if (keys.Length != opacity.Length) keys = new GradientAlphaKey[opacity.Length];
            for (int i = 0; i < keys.Length; i++) keys[i] = new GradientAlphaKey(opacity[i].alpha * alpha * (0.9f + Mathf.Sin(time * 2f) * 0.1f), opacity[i].time);
            faded.SetKeys(colors, keys);
            line.colorGradient = faded;
            line.enabled = alpha > 0.001f;
            line.widthMultiplier = width * unit;
            line.SetPositions(points);
            for (int i = 0; i < dots.Length; i++)
            {
                if (dots[i] == null) continue;
                float t = Mathf.Repeat((pulling ? -1f : 1f) * time * 0.27f + (float)i / dots.Length, 1f);
                float index = t * (points.Length - 1);
                int lower = Mathf.Min(Mathf.FloorToInt(index), points.Length - 2);
                dots[i].transform.position = Vector3.Lerp(points[lower], points[lower + 1], index - lower);
                dots[i].transform.localScale = dotScales[i] * (0.75f + Mathf.Sin(t * Mathf.PI) * 0.25f);
                Color color = dotColors[i]; color.a *= alpha * Mathf.Sin(t * Mathf.PI);
                dots[i].color = color;
                dots[i].enabled = alpha > 0.001f;
            }
        }

        private void OnDisable()
        {
            alpha = 0f;
            if (line != null) line.enabled = false;
            if (dots != null) foreach (SpriteRenderer dot in dots) if (dot != null) dot.enabled = false;
        }
    }
}
