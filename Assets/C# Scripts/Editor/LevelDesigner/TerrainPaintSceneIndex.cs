using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TapTap.Editor
{
    internal sealed class TerrainPaintRegion
    {
        public LevelRegion Region;
        public TerrainPaintProfile Profile => Region.Source.TerrainPaint;
        public readonly Dictionary<Vector2Int, TerrainTileVisual> Tiles = new Dictionary<Vector2Int, TerrainTileVisual>();
        public readonly Dictionary<TerrainTileVisual, Vector2Int> Cells = new Dictionary<TerrainTileVisual, Vector2Int>();
    }

    [InitializeOnLoad]
    internal static class TerrainPaintSceneIndex
    {
        static TerrainPaintSceneIndex() => Undo.undoRedoPerformed += OnUndoRedo;

        private static void OnUndoRedo()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            RefreshLoadedPaint();
            foreach (TerrainPaintProfile profile in Resources.FindObjectsOfTypeAll<TerrainPaintProfile>())
                if (EditorUtility.IsDirty(profile)) AssetDatabase.SaveAssetIfDirty(profile);
        }

        public static List<TerrainPaintRegion> Build()
        {
            var regions = new Dictionary<LevelRegion, TerrainPaintRegion>();
            var placements = new Dictionary<LevelDefinition, Dictionary<string, LevelPlacement>>();
            foreach (TerrainTileVisual tile in Object.FindObjectsOfType<TerrainTileVisual>(true))
            {
                if (!tile.gameObject.scene.IsValid() || EditorSceneManager.IsPreviewScene(tile.gameObject.scene)
                    || StageUtility.GetStageHandle(tile.gameObject) != StageUtility.GetCurrentStageHandle()) continue;
                LevelRegion region = tile.GetComponentInParent<LevelRegion>(true);
                if (region == null || region.Source == null || region.Source.TerrainPaint == null) continue;
                if (!placements.TryGetValue(region.Source, out var byId))
                {
                    placements.Add(region.Source, byId = new Dictionary<string, LevelPlacement>());
                    foreach (LevelPlacement placement in region.Source.Placements)
                        if (placement != null && !string.IsNullOrEmpty(placement.Id)) byId[placement.Id] = placement;
                }
                if (string.IsNullOrEmpty(tile.PlacementId) || !byId.TryGetValue(tile.PlacementId, out LevelPlacement item)) continue;
                if (!regions.TryGetValue(region, out TerrainPaintRegion entry))
                    regions.Add(region, entry = new TerrainPaintRegion { Region = region });
                entry.Tiles[item.Cell] = tile;
                entry.Cells[tile] = item.Cell;
            }
            return new List<TerrainPaintRegion>(regions.Values);
        }

        public static bool Project(Ray ray, LevelRegion region, out Vector2Int cell)
        {
            cell = default;
            if (region == null || !new Plane(region.transform.forward, region.transform.position).Raycast(ray, out float distance)) return false;
            Vector3 local = region.transform.InverseTransformPoint(ray.GetPoint(distance)) / region.UnitSize;
            if (float.IsNaN(local.x) || float.IsNaN(local.y) || float.IsInfinity(local.x) || float.IsInfinity(local.y)
                || Mathf.Abs(local.x) > 10000000 || Mathf.Abs(local.y) > 10000000) return false;
            cell = Vector2Int.FloorToInt(new Vector2(local.x, local.y));
            return true;
        }

        public static bool ClipLine(ref Vector2Int a, ref Vector2Int b, RectInt bounds)
        {
            Vector2 origin = a, delta = (Vector2)b - origin;
            float start = 0, end = 1;
            bool Clip(float p, float q)
            {
                if (Mathf.Abs(p) < 0.000001f) return q >= 0;
                float ratio = q / p;
                if (p < 0) { if (ratio > end) return false; start = Mathf.Max(start, ratio); }
                else { if (ratio < start) return false; end = Mathf.Min(end, ratio); }
                return true;
            }
            if (!Clip(-delta.x, origin.x - bounds.xMin) || !Clip(delta.x, bounds.xMax - 1 - origin.x)
                || !Clip(-delta.y, origin.y - bounds.yMin) || !Clip(delta.y, bounds.yMax - 1 - origin.y)) return false;
            a = Vector2Int.RoundToInt(origin + delta * start); b = Vector2Int.RoundToInt(origin + delta * end);
            return true;
        }

        public static IEnumerable<Vector2Int> Line(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(b.x - a.x), dy = Mathf.Abs(b.y - a.y);
            int sx = a.x < b.x ? 1 : -1, sy = a.y < b.y ? 1 : -1, error = dx - dy;
            while (true)
            {
                yield return a;
                if (a == b) yield break;
                int doubled = error * 2;
                if (doubled > -dy) { error -= dy; a.x += sx; }
                if (doubled < dx) { error += dx; a.y += sy; }
            }
        }

        public static List<TerrainTileVisual> Fill(TerrainPaintRegion region, Vector2Int start)
        {
            var result = new List<TerrainTileVisual>();
            if (!region.Tiles.TryGetValue(start, out TerrainTileVisual first) || !first.CanPaint) return result;
            Material target = first.PaintMaterial;
            var queue = new Queue<Vector2Int>(); var seen = new HashSet<Vector2Int>();
            queue.Enqueue(start); seen.Add(start);
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                if (!region.Tiles.TryGetValue(cell, out TerrainTileVisual tile) || !tile.CanPaint || tile.PaintMaterial != target) continue;
                result.Add(tile);
                foreach (Vector2Int offset in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                    if (seen.Add(cell + offset) && region.Tiles.ContainsKey(cell + offset)) queue.Enqueue(cell + offset);
            }
            return result;
        }

        public static void RefreshLoadedPaint()
        {
            foreach (TerrainPaintProfile profile in Resources.FindObjectsOfTypeAll<TerrainPaintProfile>()) profile.InvalidateCache();
            foreach (TerrainTileVisual tile in Object.FindObjectsOfType<TerrainTileVisual>(true)) tile.RefreshPaint();
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }

    internal sealed class TerrainPaintStroke
    {
        private readonly HashSet<TerrainPaintProfile> profiles = new HashSet<TerrainPaintProfile>();
        private readonly HashSet<string> visited = new HashSet<string>();
        private int group = -1;
        public int Count { get; private set; }

        public bool Apply(TerrainTileVisual tile, Material material)
        {
            if (tile == null || !tile.CanPaint) return false;
            LevelRegion region = tile.GetComponentInParent<LevelRegion>(true);
            TerrainPaintProfile profile = region.Source.TerrainPaint;
            string key = profile.GetInstanceID() + ":" + profile.ModuleKey(region.WorldModuleId) + ":" + tile.PlacementId;
            if (!visited.Add(key) || profile.GetPaint(region.WorldModuleId, tile.PlacementId) == material
                || (material == profile.BaseMaterial && tile.PaintMaterial == null)) return false;
            if (group < 0) { Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("绘制地形涂装"); }
            if (profiles.Add(profile)) Undo.RegisterCompleteObjectUndo(profile, "绘制地形涂装");
            if (!profile.SetPaint(region.WorldModuleId, tile.PlacementId, material)) return false;
            EditorUtility.SetDirty(profile);
            tile.RefreshPaint(); Count++;
            return true;
        }

        public void Finish()
        {
            if (group < 0) return;
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            foreach (TerrainPaintProfile profile in profiles) if (profile != null) AssetDatabase.SaveAssetIfDirty(profile);
            foreach (TerrainTileVisual tile in Object.FindObjectsOfType<TerrainTileVisual>(true))
            {
                LevelRegion region = tile.GetComponentInParent<LevelRegion>(true);
                TerrainPaintProfile profile = region != null ? region.Source?.TerrainPaint : null;
                if (profile != null && profiles.Contains(profile) && visited.Contains(profile.GetInstanceID() + ":"
                    + profile.ModuleKey(region.WorldModuleId) + ":" + tile.PlacementId)) tile.RefreshPaint();
            }
            SceneView.RepaintAll();
            group = -1;
        }
    }
}
