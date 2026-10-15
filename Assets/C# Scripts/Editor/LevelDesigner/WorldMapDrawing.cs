using System;
using UnityEngine;

namespace TapTap.Editor
{
    public static class WorldMapDrawing
    {
        public static readonly Color32 Outside = new Color32(87, 91, 101, 255);
        public static readonly Color32 Inside = new Color32(30, 34, 43, 255);
        public static readonly Color32 PassageColor = new Color32(255, 216, 85, 255);
        private static readonly Color[] ModuleColors =
        {
            new Color(0.32f, 0.75f, 0.92f), new Color(0.78f, 0.57f, 0.91f),
            new Color(0.94f, 0.68f, 0.38f), new Color(0.40f, 0.80f, 0.64f),
            new Color(0.92f, 0.52f, 0.63f), new Color(0.62f, 0.70f, 0.94f)
        };

        public static Color ModuleColor(WorldMapModule module)
        {
            uint hash = 0;
            foreach (char c in module.Id ?? "") hash = hash * 31 + c;
            return ModuleColors[hash % ModuleColors.Length];
        }

        public static Texture2D MaskTexture(WorldMapMask mask)
        {
            var pixels = new Color32[mask.Cells.Length];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = mask.Cells[i] ? Inside : Outside;
            return Texture(mask.Size.x, mask.Size.y, pixels);
        }

        public static Texture2D LevelTexture(LevelDefinition level)
        {
            // Two pixels per cell also distinguish thin platforms from full terrain.
            int scale = Mathf.Max(1, Mathf.Min(2, 4096 / Mathf.Max(level.Size.x, level.Size.y)));
            int width = level.Size.x * scale, height = level.Size.y * scale;
            var pixels = new Color32[width * height];
            if (level.Placements != null)
                foreach (LevelPlacement placement in level.Placements)
                {
                    if (placement?.Brush == null) continue;
                    Color color = placement.Brush.Color;
                    if (placement.Settings is PhaseBlockPlacementSettings phase && phase.SolidWhenActive) color.a *= 0.3f;
                    if (placement.Settings is PistonPlacementSettings piston && piston.ExtendedWhenActive) color.a *= 0.3f;
                    Rect shape = LevelBrushHandlers.Get(placement.Brush.HandlerId)?.GetPreviewRect(placement) ?? placement.Brush.PreviewRect;
                    Fill(pixels, width, height, Mathf.FloorToInt((placement.Cell.x - level.CanvasOrigin.x + shape.xMin) * scale),
                        Mathf.FloorToInt((placement.Cell.y - level.CanvasOrigin.y + shape.yMin) * scale),
                        Mathf.Max(1, Mathf.CeilToInt(shape.width * scale)),
                        Mathf.Max(1, Mathf.CeilToInt(shape.height * scale)), color);
                }
            return Texture(width, height, pixels);
        }

        public static Texture2D Export(WorldMapDefinition map, WorldMapMask mask)
        {
            RectInt bounds = map.PreviewBounds;
            Vector2Int offset = bounds.min;
            int scale = Mathf.Max(1, Mathf.Min(3, 4096 / Mathf.Max(bounds.width, bounds.height)));
            int width = bounds.width * scale, height = bounds.height * scale;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = map.ShowTemplate && mask.Contains(offset + new Vector2Int(x / scale, y / scale)) ? Inside : Outside;
            foreach (WorldMapModule module in map.Modules)
            {
                if (module?.Level == null) continue;
                LevelFootprint footprint = module.Level.Footprint;
                Color color = ModuleColor(module), tint = color;
                tint.a = 0.22f;
                foreach (RectInt run in footprint.Runs)
                    Fill(pixels, width, height, (run.x + module.Position.x - offset.x) * scale, (run.y + module.Position.y - offset.y) * scale, run.width * scale, scale, tint);
                Texture2D level = LevelTexture(module.Level);
                try
                {
                    Color32[] source = level.GetPixels32();
                    for (int y = 0; y < module.Level.Size.y * scale; y++)
                        for (int x = 0; x < module.Level.Size.x * scale; x++)
                        {
                            int tx = (module.Position.x + module.Level.CanvasOrigin.x - offset.x) * scale + x,
                                ty = (module.Position.y + module.Level.CanvasOrigin.y - offset.y) * scale + y;
                            if (tx < 0 || ty < 0 || tx >= width || ty >= height) continue;
                            Color sample = source[(y * level.height / (module.Level.Size.y * scale)) * level.width +
                                x * level.width / (module.Level.Size.x * scale)];
                            pixels[ty * width + tx] = Over(sample, pixels[ty * width + tx]);
                        }
                }
                finally { UnityEngine.Object.DestroyImmediate(level); }
                foreach (LevelBoundaryEdge edge in footprint.Edges)
                {
                    Vector2Int a = (edge.Start + module.Position - offset) * scale, b = (edge.End + module.Position - offset) * scale;
                    // Draw on the inside pixel, so neighbouring rooms keep independent borders.
                    Vector2Int inset = edge.Outward.x > 0 ? Vector2Int.left : edge.Outward.y > 0 ? Vector2Int.down : Vector2Int.zero;
                    Vector2Int tangent = edge.Outward.x == 0 ? Vector2Int.right : Vector2Int.up;
                    Line(pixels, width, height, a + inset, b + inset - tangent, color);
                }
            }
            foreach (Vector2[] outline in map.ShowTemplate ? mask.Outlines : new Vector2[0][])
                for (int i = 0; i < outline.Length; i++)
                    Line(pixels, width, height, Vector2Int.RoundToInt((outline[i] - (Vector2)offset) * scale),
                        Vector2Int.RoundToInt((outline[(i + 1) % outline.Length] - (Vector2)offset) * scale), new Color32(255, 143, 168, 255));
            if (map.Passages != null)
                for (int i = 0; i < map.Passages.Count; i++)
                {
                    WorldMapPassage passage = map.Passages[i]; if (passage == null) continue;
                    Vector2 point = ((Vector2)(passage.Position - offset) + Vector2.one * 0.5f) * scale;
                    float halfLength = Mathf.Max(6f, scale * 4f);
                    Vector2 from = point - passage.ArrowDirection * halfLength, to = point + passage.ArrowDirection * halfLength;
                    ClippedLine(pixels, width, height, from, to, PassageColor);
                    ExportArrow(pixels, width, height, from, to, PassageColor);
                    if (passage.Bidirectional) ExportArrow(pixels, width, height, to, from, PassageColor);
                }
            return Texture(width, height, pixels);
        }

        private static void ExportArrow(Color32[] pixels, int width, int height, Vector2 from, Vector2 to, Color32 color)
        {
            Vector2 direction = (to - from).normalized; if (direction.sqrMagnitude < 0.01f) return;
            Vector2 side = new Vector2(-direction.y, direction.x);
            ClippedLine(pixels, width, height, to - direction * 7f + side * 4f, to, color);
            ClippedLine(pixels, width, height, to - direction * 7f - side * 4f, to, color);
        }

        private static void ClippedLine(Color32[] pixels, int width, int height, Vector2 a, Vector2 b, Color32 color)
        {
            Vector2 delta = b - a; float first = 0f, last = 1f;
            if (!Clip(-delta.x, a.x, ref first, ref last) || !Clip(delta.x, width - 1 - a.x, ref first, ref last) ||
                !Clip(-delta.y, a.y, ref first, ref last) || !Clip(delta.y, height - 1 - a.y, ref first, ref last)) return;
            Line(pixels, width, height, Vector2Int.RoundToInt(a + delta * first), Vector2Int.RoundToInt(a + delta * last), color);
        }

        private static bool Clip(float slope, float distance, ref float first, ref float last)
        {
            if (Mathf.Abs(slope) < 0.0001f) return distance >= 0f;
            float t = distance / slope;
            if (slope < 0f) { if (t > last) return false; first = Mathf.Max(first, t); }
            else { if (t < first) return false; last = Mathf.Min(last, t); }
            return true;
        }

        public static int CountOutside(WorldMapMask mask, LevelFootprint footprint, Vector2Int position)
        {
            int count = 0;
            foreach (RectInt run in footprint.Runs) count += mask.CountOutside(new RectInt(run.position + position, run.size));
            return count;
        }

        private static void Line(Color32[] pixels, int width, int height, Vector2Int a, Vector2Int b, Color32 color)
        {
            int dx = Math.Abs(b.x - a.x), sx = a.x < b.x ? 1 : -1;
            int dy = -Math.Abs(b.y - a.y), sy = a.y < b.y ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                if (a.x >= 0 && a.y >= 0 && a.x < width && a.y < height) pixels[a.y * width + a.x] = color;
                if (a == b) break;
                int twice = error * 2;
                if (twice >= dy) { error += dy; a.x += sx; }
                if (twice <= dx) { error += dx; a.y += sy; }
            }
        }

        private static Texture2D Texture(int width, int height, Color32[] pixels)
        {
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "World map preview", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            result.SetPixels32(pixels);
            result.Apply();
            return result;
        }

        private static void Fill(Color32[] pixels, int width, int height, int x, int y, int w, int h, Color color)
        {
            for (int py = Mathf.Max(0, y); py < Mathf.Min(height, y + h); py++)
                for (int px = Mathf.Max(0, x); px < Mathf.Min(width, x + w); px++)
                    pixels[py * width + px] = Over(color, pixels[py * width + px]);
        }

        private static Color Over(Color front, Color back)
        {
            float alpha = front.a + back.a * (1f - front.a);
            if (alpha <= 0f) return Color.clear;
            return new Color((front.r * front.a + back.r * back.a * (1f - front.a)) / alpha,
                (front.g * front.a + back.g * back.a * (1f - front.a)) / alpha,
                (front.b * front.a + back.b * back.a * (1f - front.a)) / alpha, alpha);
        }
    }
}
