using System.Collections.Generic;
using UnityEngine;

namespace TapTap.Editor
{
    // Straight inner slopes, outer bevels and long lower diagonals, joined by two short Bezier arcs.
    // Mirroring one half keeps the raster symmetric even at odd template widths.
    public static class HeartMapTemplate
    {
        public static Vector2[] Outline(Vector2Int size, int padding)
        {
            var half = new List<Vector2> { new Vector2(0f, 0.78f), new Vector2(0.165f, 0.945f) };
            Curve(half, new Vector2(0.165f, 0.945f), new Vector2(0.215f, 0.995f),
                new Vector2(0.270f, 1f), new Vector2(0.320f, 0.950f));
            half.Add(new Vector2(0.455f, 0.815f));
            Curve(half, new Vector2(0.455f, 0.815f), new Vector2(0.500f, 0.770f),
                new Vector2(0.520f, 0.700f), new Vector2(0.470f, 0.630f));
            half.Add(Vector2.zero);
            float maxX = 0f, maxY = 0f;
            foreach (Vector2 point in half)
            {
                maxX = Mathf.Max(maxX, point.x);
                maxY = Mathf.Max(maxY, point.y);
            }
            var points = new List<Vector2>(half);
            for (int i = half.Count - 2; i > 0; i--) points.Add(new Vector2(-half[i].x, half[i].y));
            for (int i = 0; i < points.Count; i++)
                points[i] = new Vector2(padding + (0.5f + points[i].x / (maxX * 2f)) * size.x,
                    padding + points[i].y / maxY * size.y);
            return points.ToArray();
        }

        private static void Curve(List<Vector2> points, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            for (int i = 1; i <= 12; i++)
            {
                float t = i / 12f, u = 1f - t;
                points.Add(u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d);
            }
        }
    }

    public sealed class WorldMapMask
    {
        public readonly Vector2Int Size;
        public readonly Vector2[][] Outlines;
        public readonly bool[] Cells;
        private readonly int[] prefix;

        public WorldMapMask(WorldMapDefinition map) : this(map.TemplateSize, map.Padding,
            map.Template == WorldMapTemplateKind.SplitCharacter
                ? CharacterMapTemplate.Outlines(map.TemplateSize, map.Padding, map.CharacterGapRatio, map.CharacterCornerRatio)
                : new[] { HeartMapTemplate.Outline(map.TemplateSize, map.Padding) }) { }

        public WorldMapMask(Vector2Int heartSize, int padding) : this(heartSize, padding,
            new[] { HeartMapTemplate.Outline(heartSize, padding) }) { }

        private WorldMapMask(Vector2Int templateSize, int padding, Vector2[][] outlines)
        {
            Size = templateSize + Vector2Int.one * (padding * 2);
            Outlines = outlines;
            Cells = new bool[Size.x * Size.y];
            prefix = new int[(Size.x + 1) * (Size.y + 1)];
            var crossings = new List<float>();
            for (int y = 0; y < Size.y; y++)
            {
                crossings.Clear();
                float py = y + 0.5f;
                foreach (Vector2[] outline in Outlines)
                    for (int i = 0; i < outline.Length; i++)
                    {
                        Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
                        if ((a.y > py) == (b.y > py)) continue;
                        crossings.Add(a.x + (py - a.y) * (b.x - a.x) / (b.y - a.y));
                    }
                crossings.Sort();
                for (int i = 0; i + 1 < crossings.Count; i += 2)
                {
                    int from = Mathf.Clamp(Mathf.CeilToInt(crossings[i] - 0.5f), 0, Size.x);
                    int to = Mathf.Clamp(Mathf.CeilToInt(crossings[i + 1] - 0.5f), 0, Size.x);
                    for (int x = from; x < to; x++) Cells[y * Size.x + x] = true;
                }
                // Sum each row once; rectangle containment then costs four array lookups.
                int row = 0, stride = Size.x + 1;
                for (int x = 0; x < Size.x; x++)
                {
                    row += Cells[y * Size.x + x] ? 1 : 0;
                    prefix[(y + 1) * stride + x + 1] = prefix[y * stride + x + 1] + row;
                }
            }
        }

        public bool Contains(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < Size.x &&
            cell.y < Size.y && Cells[cell.y * Size.x + cell.x];

        public int CountInside(RectInt rectangle)
        {
            int x0 = Mathf.Clamp(rectangle.xMin, 0, Size.x), x1 = Mathf.Clamp(rectangle.xMax, 0, Size.x);
            int y0 = Mathf.Clamp(rectangle.yMin, 0, Size.y), y1 = Mathf.Clamp(rectangle.yMax, 0, Size.y);
            int stride = Size.x + 1;
            return prefix[y1 * stride + x1] - prefix[y0 * stride + x1] -
                prefix[y1 * stride + x0] + prefix[y0 * stride + x0];
        }

        public int CountOutside(RectInt rectangle) => rectangle.width * rectangle.height - CountInside(rectangle);
        public bool Contains(RectInt rectangle) => rectangle.width > 0 && rectangle.height > 0 && CountOutside(rectangle) == 0;
    }
}
