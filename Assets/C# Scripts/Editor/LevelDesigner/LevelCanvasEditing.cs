using System;
using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public static class LevelCanvasEditing
    {
        public static RectInt ExpandedBounds(RectInt bounds, Vector2Int direction, int count)
            => ResizedBounds(bounds, direction, Mathf.Max(0, count));

        // Positive counts move this edge outward; negative counts move it inward.
        public static RectInt ResizedBounds(RectInt bounds, Vector2Int direction, int count)
        {
            if (Mathf.Abs(direction.x) + Mathf.Abs(direction.y) != 1)
                throw new ArgumentException("请选择左、右、上、下中的一个方向。", nameof(direction));
            int dimension = direction.x != 0 ? bounds.width : bounds.height;
            count = Mathf.Clamp(count, 1 - dimension, Mathf.Max(0, 2048 - dimension));
            Vector2Int added = new Vector2Int(Mathf.Abs(direction.x), Mathf.Abs(direction.y)) * count;
            Vector2Int origin = bounds.min + new Vector2Int(Mathf.Min(direction.x, 0), Mathf.Min(direction.y, 0)) * count;
            return new RectInt(origin, bounds.size + added);
        }

        public static bool Expand(LevelDefinition level, Vector2Int direction, int count, Action beforeChange = null)
            => Resize(level, direction, Mathf.Max(0, count), beforeChange);

        public static bool Resize(LevelDefinition level, Vector2Int direction, int count, Action beforeChange = null)
        {
            if (level == null || EditorApplication.isPlayingOrWillChangePlaymode) return false;
            RectInt next = ResizedBounds(level.CanvasBounds, direction, count);
            if (next.Equals(level.CanvasBounds)) return false;
            if (beforeChange != null) beforeChange();
            else Undo.RegisterCompleteObjectUndo(level, count < 0 ? "回缩关卡画布" : "扩展关卡画布");
            // Keep placement coordinates and module origins fixed, including Custom prefab contents.
            level.CanvasOrigin = next.min;
            level.Size = next.size;
            EditorUtility.SetDirty(level);
            LevelLayoutChanges.Notify(level);
            return true;
        }
    }
}
