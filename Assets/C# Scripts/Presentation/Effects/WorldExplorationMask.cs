using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TapTap
{
    // The renderer feature projects this union into a separate texture. Background
    // depth never participates in the decision to reveal a screen pixel.
    [DisallowMultipleComponent]
    public sealed class WorldExplorationMask : MonoBehaviour
    {
        [Tooltip("本次运行尚未探索的区域及区域外画面的遮挡颜色。")]
        public Color MaskColor = Color.black;

        private sealed class RegionShape
        {
            public LevelRegion Region;
            public RegionRevealView Reveal;
            public Mesh Interior;
        }

        private readonly List<RegionShape> regions = new List<RegionShape>();
        private readonly Plane[] frustum = new Plane[6];
        public WorldEnvironmentParticles Background { get; private set; }

        public void RefreshRegions(WorldMapRoot world)
        {
            ReleaseShapes();
            if (world == null || world.Source == null) return;
            Background = null;
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                Background = root.GetComponentInChildren<WorldEnvironmentParticles>();
                if (Background != null) { Background.FitToWorld(world); break; }
            }
            foreach (WorldMapModule module in world.Source.OrderedModules())
            {
                LevelRegion region = world.FindRegion(module);
                if (region == null) continue;
                regions.Add(new RegionShape
                {
                    Region = region,
                    Reveal = region.GetComponent<RegionRevealView>(),
                    Interior = BuildInterior(region)
                });
            }
        }

        public void RecordVisibleRegions(CommandBuffer commands, Material fill, Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            foreach (RegionShape shape in regions)
            {
                if (shape.Region == null || !shape.Region.gameObject.activeInHierarchy
                    || shape.Reveal == null || !shape.Reveal.Discovered) continue;

                // Fill the playable air between the outer models as well as the
                // models themselves. Runs preserve concavities and disconnected rooms.
                commands.DrawMesh(shape.Interior, shape.Region.transform.localToWorldMatrix, fill, 0, 0);
                foreach (Renderer renderer in shape.Reveal.SceneRenderers)
                {
                    if (renderer == null || !renderer.enabled || renderer.forceRenderingOff
                        || !renderer.gameObject.activeInHierarchy
                        || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0
                        || !GeometryUtility.TestPlanesAABB(frustum, renderer.bounds)) continue;
                    // Particles, lines and labels must not punch holes in the fog.
                    if (renderer is MeshRenderer)
                    {
                        if (renderer.GetComponent<TextMesh>() != null) continue;
                        MeshFilter filter = renderer.GetComponent<MeshFilter>();
                        Mesh mesh = filter != null ? filter.sharedMesh : null;
                        if (mesh == null) continue;
                        for (int part = 0; part < mesh.subMeshCount; part++)
                            commands.DrawMesh(mesh, renderer.localToWorldMatrix, fill, part, 0);
                    }
                    else if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                    {
                        for (int part = 0; part < skinned.sharedMesh.subMeshCount; part++)
                            commands.DrawRenderer(renderer, fill, part, 0);
                    }
                    else if (renderer is SpriteRenderer)
                        commands.DrawRenderer(renderer, fill, 0, 0);
                }
            }
        }

        private static Mesh BuildInterior(LevelRegion region)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            void Add(RectInt run)
            {
                int first = vertices.Count;
                float unit = region.UnitSize;
                vertices.Add(new Vector3(run.xMin * unit, run.yMin * unit, 0f));
                vertices.Add(new Vector3(run.xMax * unit, run.yMin * unit, 0f));
                vertices.Add(new Vector3(run.xMax * unit, run.yMax * unit, 0f));
                vertices.Add(new Vector3(run.xMin * unit, run.yMax * unit, 0f));
                triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
            }
            if (region.Source != null)
                foreach (RectInt run in region.Source.Footprint.Runs) Add(run);
            else Add(new RectInt(Vector2Int.zero, region.Size));
            var mesh = new Mesh { name = "Explored region interior", hideFlags = HideFlags.HideAndDontSave };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.UploadMeshData(true);
            return mesh;
        }

        private void OnDestroy() => ReleaseShapes();

        private void ReleaseShapes()
        {
            foreach (RegionShape shape in regions)
                if (shape.Interior != null)
                {
                    if (Application.isPlaying) Destroy(shape.Interior);
                    else DestroyImmediate(shape.Interior);
                }
            regions.Clear();
        }
    }
}
