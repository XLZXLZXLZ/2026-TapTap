using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TapTap
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WorldMapTravelGraphic : MaskableGraphic
    {
        public bool IsTrail;
        public float TrailWidth = 0.8f;
        public float Lifetime = 0.24f;
        public int SampleCount => samples.Count;
        private struct Sample { public Vector2 Position; public float Time; }
        private readonly List<Sample> samples = new List<Sample>();

        public void Record(Vector2 point)
        {
            if (samples.Count > 0 && (samples[samples.Count - 1].Position - point).sqrMagnitude < 0.000001f) return;
            samples.Add(new Sample { Position = point, Time = Time.unscaledTime });
            SetVerticesDirty();
        }

        private void Update()
        {
            if (!IsTrail || samples.Count == 0) return;
            float expiry = Time.unscaledTime - Lifetime;
            while (samples.Count > 0 && samples[0].Time < expiry) samples.RemoveAt(0);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (!IsTrail)
            {
                Rect rect = rectTransform.rect;
                Quad(mesh, new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin, rect.yMax),
                    new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMax, rect.yMin), Color.white, Color.white, false);
                return;
            }
            for (int i = 1; i < samples.Count; i++)
            {
                Sample a = samples[i - 1], b = samples[i];
                Vector2 direction = (b.Position - a.Position).normalized;
                Vector2 normal = new Vector2(-direction.y, direction.x);
                if (normal.y < 0f) normal = -normal;
                float fadeA = Mathf.Clamp01(1f - (Time.unscaledTime - a.Time) / Lifetime);
                float fadeB = Mathf.Clamp01(1f - (Time.unscaledTime - b.Time) / Lifetime);
                Vector2 widthA = normal * (TrailWidth * 0.5f * fadeA);
                Vector2 widthB = normal * (TrailWidth * 0.5f * fadeB);
                Color start = Color.white, end = Color.white;
                start.a = fadeA * fadeA;
                end.a = fadeB * fadeB;
                Quad(mesh, a.Position - widthA, a.Position + widthA, b.Position + widthB,
                    b.Position - widthB, start, end, true);
            }
        }

        private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
            Color start, Color end, bool trail)
        {
            int index = mesh.currentVertCount;
            float u = trail ? 2f : 0f;
            mesh.AddVert(a, start, new Vector2(u, 0f));
            mesh.AddVert(b, start, new Vector2(u, 1f));
            mesh.AddVert(c, end, new Vector2(u + 1f, 1f));
            mesh.AddVert(d, end, new Vector2(u + 1f, 0f));
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
