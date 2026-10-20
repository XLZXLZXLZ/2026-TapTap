using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    /// <summary>Cubic Bezier segments through authored XZ anchors, with an approximate arc-length lookup.</summary>
    public sealed class EndingBezierPath
    {
        public readonly struct Segment
        {
            public readonly Vector3 Start, Control1, Control2, End;

            public Segment(Vector3 start, Vector3 control1, Vector3 control2, Vector3 end)
            { Start = start; Control1 = control1; Control2 = control2; End = end; }

            public Vector3 Point(float t)
            {
                t = Mathf.Clamp01(t);
                float u = 1f - t;
                return u * u * u * Start + 3f * u * u * t * Control1
                    + 3f * u * t * t * Control2 + t * t * t * End;
            }

            public Vector3 Derivative(float t)
            {
                t = Mathf.Clamp01(t);
                float u = 1f - t;
                return 3f * u * u * (Control1 - Start) + 6f * u * t * (Control2 - Control1)
                    + 3f * t * t * (End - Control2);
            }

            public float ArcLength(float from, float to)
            {
                float middle = (from + to) * 0.5f;
                return (to - from) * (Derivative(from).magnitude
                    + 4f * Derivative(middle).magnitude + Derivative(to).magnitude) / 6f;
            }
        }

        private readonly struct Sample
        {
            public readonly float Distance, T;
            public readonly int Segment;
            public Sample(float distance, int segment, float t) { Distance = distance; Segment = segment; T = t; }
        }

        private readonly List<Vector3> anchors = new List<Vector3>();
        private readonly List<Segment> segments = new List<Segment>();
        private readonly List<Sample> samples = new List<Sample>();
        private readonly List<float> anchorDistances = new List<float>();
        public int AnchorCount => anchors.Count;
        public int SegmentCount => segments.Count;
        public float Length { get; private set; }
        public Vector3 GetAnchor(int index) => anchors[index];
        public float GetAnchorDistance(int index) => anchorDistances[index];
        public Segment GetSegment(int index) => segments[index];

        public EndingBezierPath(IEnumerable<Vector3> points, float smoothing = 0.8f, int samplesPerSegment = 32)
        {
            if (points != null)
                foreach (Vector3 source in points)
                {
                    Vector3 point = new Vector3(source.x, 0f, source.z);
                    if (anchors.Count == 0 || (point - anchors[anchors.Count - 1]).sqrMagnitude > 0.000001f)
                        anchors.Add(point);
                }
            if (anchors.Count == 0) return;
            smoothing = Mathf.Clamp01(smoothing);
            int steps = Mathf.Clamp(samplesPerSegment, 8, 128);
            samples.Add(new Sample(0f, 0, 0f));
            anchorDistances.Add(0f);
            for (int index = 0; index + 1 < anchors.Count; index++)
            {
                var segment = new Segment(anchors[index], anchors[index] + Tangent(index, smoothing) / 3f,
                    anchors[index + 1] - Tangent(index + 1, smoothing) / 3f, anchors[index + 1]);
                segments.Add(segment);
                Vector3 previous = segment.Start;
                for (int step = 1; step <= steps; step++)
                {
                    float t = (float)step / steps;
                    Vector3 point = segment.Point(t);
                    Length += Vector3.Distance(previous, point);
                    samples.Add(new Sample(Length, index, t));
                    previous = point;
                }
                anchorDistances.Add(Length);
            }
        }

        private Vector3 Tangent(int index, float smoothing)
        {
            if (anchors.Count < 2) return Vector3.zero;
            if (index == 0) return (anchors[1] - anchors[0]) * smoothing;
            if (index == anchors.Count - 1) return (anchors[index] - anchors[index - 1]) * smoothing;
            Vector3 before = anchors[index] - anchors[index - 1];
            Vector3 after = anchors[index + 1] - anchors[index];
            // Limit shared handles by the shorter adjoining segment, avoiding loops around close waypoints.
            return (before + after).normalized * (Mathf.Min(before.magnitude, after.magnitude) * smoothing);
        }

        public void Evaluate(float distance, out Vector3 point, out Vector3 direction)
        {
            if (segments.Count == 0)
            {
                point = anchors.Count > 0 ? anchors[0] : Vector3.zero;
                direction = Vector3.zero;
                return;
            }
            distance = Mathf.Clamp(distance, 0f, Length);
            int low = 1, high = samples.Count - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (samples[middle].Distance < distance) low = middle + 1;
                else high = middle;
            }
            Sample from = samples[low - 1], to = samples[low];
            float span = to.Distance - from.Distance;
            float blend = span > 0.000001f ? (distance - from.Distance) / span : 1f;
            Segment segment = segments[to.Segment];
            float startT = from.Segment == to.Segment ? from.T : 0f;
            float t = Mathf.Lerp(startT, to.T, blend);
            if (blend > 0f && blend < 1f)
            {
                // A linear interpolation of t still changes speed inside a sample interval.
                // Refine against local arc length, keeping a bracket for stationary Bezier endpoints.
                float fullLength = segment.ArcLength(startT, to.T);
                float wanted = fullLength * blend;
                float lowT = startT, highT = to.T;
                for (int iteration = 0; iteration < 4; iteration++)
                {
                    float error = wanted - segment.ArcLength(startT, t);
                    if (Mathf.Abs(error) < fullLength * 0.00001f) break;
                    if (error > 0f) lowT = t; else highT = t;
                    float speed = segment.Derivative(t).magnitude;
                    float next = speed > 0.000001f ? t + error / speed : (lowT + highT) * 0.5f;
                    t = next > lowT && next < highT ? next : (lowT + highT) * 0.5f;
                }
            }
            point = segment.Point(t);
            direction = segment.Derivative(t).normalized;
            if (direction.sqrMagnitude < 0.01f) direction = (segment.End - segment.Start).normalized;
        }
    }
}
