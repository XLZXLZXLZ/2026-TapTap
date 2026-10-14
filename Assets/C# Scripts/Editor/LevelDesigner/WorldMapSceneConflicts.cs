using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    // Read-only Scene-view diagnostics. No scene loads, prefab generation or asset writes.
    [InitializeOnLoad]
    public static class WorldMapSceneConflicts
    {
        static WorldMapSceneConflicts()
        {
            SceneView.duringSceneGui += Draw;
            EditorApplication.update += Poll;
        }

        private sealed class RegionCells
        {
            public LevelRegion Region;
            public Vector2Int Origin;
            public readonly Dictionary<Vector2Int, HashSet<string>> Tiles = new Dictionary<Vector2Int, HashSet<string>>();
        }
        private sealed class Conflict
        {
            public WorldMapRoot World;
            public Vector2Int Cell;
            public float Unit;
            public string Names;
        }
        private static readonly List<Conflict> conflicts = new List<Conflict>();
        private static double nextCheck;

        private static void Poll()
        {
            if (Application.isBatchMode || Application.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.timeSinceStartup < nextCheck || Resources.FindObjectsOfTypeAll<SceneView>().Length == 0) return;
            nextCheck = EditorApplication.timeSinceStartup + 0.25d;
            int previous = Fingerprint(); Refresh();
            if (previous != Fingerprint()) SceneView.RepaintAll();
        }

        private static int Fingerprint()
        {
            int hash = conflicts.Count;
            foreach (Conflict conflict in conflicts) hash = unchecked(hash * 31 + conflict.Cell.GetHashCode() + conflict.Names.GetHashCode());
            return hash;
        }

        private static void Refresh()
        {
            conflicts.Clear();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (GameObject root in SceneManager.GetSceneAt(i).GetRootGameObjects())
                {
                    WorldMapRoot world = root.GetComponent<WorldMapRoot>();
                    Transform generated = world != null ? world.transform.Find("Generated") : null;
                    if (generated == null) continue;
                    var regions = new List<RegionCells>();
                    foreach (LevelRegion region in generated.GetComponentsInChildren<LevelRegion>(true))
                    {
                        if (region.Source == null) continue;
                        var cells = new RegionCells { Region = region, Origin = Vector2Int.RoundToInt(
                            (Vector2)world.transform.InverseTransformPoint(region.transform.position) / region.UnitSize) };
                        Transform contents = region.transform.Find("Generated");
                        if (contents != null)
                            foreach (Transform layer in contents)
                                foreach (Transform tile in layer)
                                {
                                    Vector2 position = (Vector2)world.transform.InverseTransformPoint(tile.position) / region.UnitSize;
                                    Vector2Int cell = Vector2Int.FloorToInt(position);
                                    if (!cells.Tiles.TryGetValue(cell, out var types)) cells.Tiles[cell] = types = new HashSet<string>();
                                    string type = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(tile.gameObject);
                                    if (string.IsNullOrEmpty(type))
                                        foreach (LevelPlacement placement in region.Source.Placements)
                                            if (placement?.Brush != null && tile.name.EndsWith(placement.Id, System.StringComparison.Ordinal))
                                            { type = AssetDatabase.GetAssetPath(placement.Brush.Prefab); break; }
                                    if (string.IsNullOrEmpty(type)) type = tile.name;
                                    type += "|" + layer.name + "|" + JsonUtility.ToJson(tile.localRotation) + "|" + JsonUtility.ToJson(tile.localScale);
                                    foreach (Collider2D collider in tile.GetComponents<Collider2D>())
                                        type += "|" + collider.GetType().Name + ":" + collider.isTrigger + ":" + collider.enabled + ":" + JsonUtility.ToJson(collider.offset);
                                    var box = tile.GetComponent<BoxCollider2D>();
                                    if (box != null) type += "|" + JsonUtility.ToJson(box.size);
                                    var surface = tile.GetComponent<WorldSurface>();
                                    if (surface != null) type += "|" + surface.Kind + ":" + surface.ConveyorSpeed;
                                    var phase = tile.GetComponent<PhaseBlock>();
                                    if (phase != null) type += "|phase:" + phase.SolidWhenActive;
                                    var spring = tile.GetComponent<SpringPad>();
                                    if (spring != null) type += "|spring:" + spring.BounceHeight + ":" + spring.AnimationDuration;
                                    var checkpoint = tile.GetComponent<CheckpointFlag>();
                                    if (checkpoint != null) type += "|checkpoint:" + JsonUtility.ToJson(checkpoint.SpawnOffset);
                                    var endpoint = tile.GetComponent<LevelEndpoint>();
                                    if (endpoint != null) type += "|endpoint:" + endpoint.EndpointId + ":" + endpoint.Label;
                                    var light = tile.GetComponent<IndicatorLight>();
                                    if (light != null) type += "|light:" + light.InitiallyOn + ":" + light.ShortWidth + ":" + light.LongWidth
                                        + ":" + light.Length + ":" + light.GradientPower + ":" + light.GridUnit + ":" + JsonUtility.ToJson(light.Tint);
                                    types.Add(type);
                                }
                        regions.Add(cells);
                    }
                    for (int a = 0; a < regions.Count; a++)
                        for (int b = 0; b < a; b++)
                        {
                            RegionCells first = regions[a], second = regions[b];
                            foreach (Vector2Int local in first.Region.Source.Footprint.Cells)
                            {
                                Vector2Int cell = local + first.Origin;
                                if (!second.Region.Source.Footprint.Contains(cell - second.Origin)) continue;
                                first.Tiles.TryGetValue(cell, out var one); second.Tiles.TryGetValue(cell, out var two);
                                if (one == null ? two == null : two != null && one.SetEquals(two)) continue;
                                conflicts.Add(new Conflict { World = world, Cell = cell, Unit = first.Region.UnitSize,
                                    Names = first.Region.Source.name + " ↔ " + second.Region.Source.name });
                            }
                        }
                }
        }

        private static void Draw(SceneView view)
        {
            if (Application.isPlaying) return;
            if (EditorApplication.timeSinceStartup >= nextCheck)
            { nextCheck = EditorApplication.timeSinceStartup + 0.25d; Refresh(); }
            Color previous = Handles.color;
            Handles.color = new Color(1f, 0.42f, 0.08f, 0.9f);
            foreach (Conflict conflict in conflicts)
            {
                if (conflict.World == null) continue;
                Vector3 center = conflict.World.transform.TransformPoint(((Vector2)conflict.Cell + Vector2.one * 0.5f) * conflict.Unit);
                Handles.DrawWireCube(center, new Vector3(conflict.Unit, conflict.Unit, 0.01f));
            }
            Handles.color = previous;
            if (conflicts.Count == 0) return;
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(12, 12, 420, 64), EditorStyles.helpBox);
            GUILayout.Label("大世界重叠图块不一致：" + conflicts.Count + " 处", EditorStyles.boldLabel);
            GUILayout.Label(conflicts[0].Names + " · 格子 " + conflicts[0].Cell, EditorStyles.wordWrappedMiniLabel);
            GUILayout.EndArea();
            Handles.EndGUI();
        }
    }
}
