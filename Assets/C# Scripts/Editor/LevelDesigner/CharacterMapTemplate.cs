using System.Collections.Generic;
using UnityEngine;

namespace TapTap.Editor
{
    public static class CharacterMapTemplate
    {
        public static int GapCells(int height, float ratio)
        {
            int gap = Mathf.Max(1, Mathf.RoundToInt(height * Mathf.Clamp(ratio, 0.005f, 0.08f)));
            // Integer half-heights keep both sides equal and leave complete forbidden grid rows.
            if ((height - gap) % 2 != 0) gap++;
            return Mathf.Min(gap, height - 2);
        }

        public static Vector2[][] Outlines(Vector2Int size, int padding, float gapRatio, float cornerRatio)
        {
            float halfHeight = (size.y - GapCells(size.y, gapRatio)) * 0.5f;
            float left = padding + size.x * 0.04f, right = padding + size.x * 0.96f;
            float bottom = padding, top = padding + halfHeight;
            float radius = Mathf.Min((right - left) * Mathf.Clamp(cornerRatio, 0.01f, 0.12f), halfHeight * 0.35f);
            float lipHeight = Mathf.Min(size.y * 0.04f, halfHeight * 0.2f);
            float lipRadius = Mathf.Min(radius * 0.45f, lipHeight * 0.4f);
            float joinRadius = Mathf.Min(size.x * 0.04f * 0.3f, radius * 0.3f);
            float lipBottom = top - lipHeight, lipLeft = padding, lipRight = padding + size.x;
            var lower = new List<Vector2> { new Vector2(left + radius, bottom), new Vector2(right - radius, bottom) };
            Arc(lower, new Vector2(right - radius, bottom + radius), radius, -90f, 0f);
            lower.Add(new Vector2(right, lipBottom - joinRadius));
            Arc(lower, new Vector2(right + joinRadius, lipBottom - joinRadius), joinRadius, 180f, 90f);
            lower.Add(new Vector2(lipRight - lipRadius, lipBottom));
            Arc(lower, new Vector2(lipRight - lipRadius, lipBottom + lipRadius), lipRadius, -90f, 0f);
            lower.Add(new Vector2(lipRight, top - lipRadius));
            Arc(lower, new Vector2(lipRight - lipRadius, top - lipRadius), lipRadius, 0f, 90f);
            lower.Add(new Vector2(lipLeft + lipRadius, top));
            Arc(lower, new Vector2(lipLeft + lipRadius, top - lipRadius), lipRadius, 90f, 180f);
            lower.Add(new Vector2(lipLeft, lipBottom + lipRadius));
            Arc(lower, new Vector2(lipLeft + lipRadius, lipBottom + lipRadius), lipRadius, 180f, 270f);
            lower.Add(new Vector2(left - joinRadius, lipBottom));
            Arc(lower, new Vector2(left - joinRadius, lipBottom - joinRadius), joinRadius, 90f, 0f);
            lower.Add(new Vector2(left, bottom + radius));
            Arc(lower, new Vector2(left + radius, bottom + radius), radius, 180f, 270f);
            var upper = new Vector2[lower.Count];
            for (int i = 0; i < lower.Count; i++) upper[i] = new Vector2(lower[i].x, padding * 2f + size.y - lower[i].y);
            return new[] { lower.ToArray(), upper };
        }

        private static void Arc(List<Vector2> points, Vector2 center, float radius, float from, float to)
        {
            for (int i = 0; i <= 8; i++)
            {
                float angle = Mathf.Lerp(from, to, i / 8f) * Mathf.Deg2Rad;
                points.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        }
    }
}
