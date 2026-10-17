using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    public sealed class WorldMapAssemblyView : MonoBehaviour
    {
        private const int PreviewLayer = 31;
        private GameObject visuals;
        private Camera preview;
        private RenderTexture texture;
        private Material lineMaterial;
        private readonly List<LevelOutlineData> temporaryOutlines = new List<LevelOutlineData>();
        private bool visible, complete;
        private string caption;
        private GUIStyle captionStyle;
        private float lineWidth;
        private bool selection;
        private WorldMapDefinition selectionWorld;
        private readonly Dictionary<WorldMapModule, Transform> pieces = new Dictionary<WorldMapModule, Transform>();
        private NeonWorldMapScreen neonScreen;
        [Tooltip("场景中提前摆放的地图界面；运行时只切换显隐，不实例化或销毁。")]
        public NeonWorldMapScreen SceneScreen;
        public bool UsesNeonScreen => neonScreen != null;
        public NeonWorldMapScreen NeonScreen => neonScreen;
        public static List<WorldMapModule> SelectableModules(WorldMapDefinition world) => NeonWorldMapScreen.SelectableModules(world);
        public List<WorldMapModule> AvailableModules(WorldMapDefinition world) => SceneScreen!=null && SceneScreen.Source==world
            ? SceneScreen.AvailableModules(world) : SelectableModules(world);
        public void SetMenuSelection(WorldMapModule module, string notice = null) => neonScreen?.SetSelected(module, notice);
        private Rect RenderRect => selection ? new Rect(16f, 110f, Mathf.Max(1f, Screen.width - 32f),
            Mathf.Max(80f, Screen.height - 254f)) : new Rect(0f, 0f, Screen.width, Screen.height);

        public IEnumerator Play(WorldMapRoot world, IReadOnlyList<WorldMapModule> modules,
            HashSet<string> passed, WorldMapModule incoming, float duration, float hold, string message = null,
            WorldMapModule departing = null)
        {
            if (TryPrepareNeon(world.Source, false))
            {
                yield return neonScreen.Play(passed, incoming, duration, hold, departing);
                yield break;
            }
            PrepareBoard(world.Source, modules, message ?? "第 " + world.Source.SequenceNumber(departing ?? incoming) + " 关完成");
            foreach (WorldMapModule module in modules)
                if ((passed.Contains(module.Id) || module == departing) && module != incoming) CreatePiece(world.Source, module);
            if (incoming == null)
            {
                preview.Render();
                yield return ScreenFadeOverlay.Instance.FadeTo(0f, 0.2f);
                float wait = 0f;
                while (wait < hold) { wait += Time.unscaledDeltaTime; yield return null; }
                yield break;
            }
            CreatePiece(world.Source, incoming);
            preview.Render();
            yield return ScreenFadeOverlay.Instance.FadeTo(0f, 0.2f);
            float pause = 0f;
            while (pause < hold) { pause += Time.unscaledDeltaTime; yield return null; }
        }

        private void PrepareBoard(WorldMapDefinition world, IReadOnlyList<WorldMapModule> modules, string title, bool asSelection = false)
        {
            Hide();
            visible = true;
            selection = asSelection;
            caption = title;
            visuals = new GameObject("World Outline Preview");
            visuals.transform.position = new Vector3(100000f, 100000f, 0f);
            lineMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            Bounds bounds = default;
            bool first = true;
            foreach (WorldMapModule module in modules)
            {
                RectInt grid = module.Bounds;
                float unit = module.Level.Palette.UnitSize;
                var area = new Bounds((Vector2)grid.center * unit, (Vector2)grid.size * unit);
                if (first) { bounds = area; first = false; } else bounds.Encapsulate(area);
            }
            Rect areaRect = RenderRect;
            float aspect = Mathf.Max(0.1f, areaRect.width / Mathf.Max(1f, areaRect.height));
            float size = Mathf.Max(1f, bounds.size.y * (selection ? 0.55f : 0.65f), bounds.size.x / aspect * 0.6f);
            lineWidth = size * 2f / Mathf.Max(1f, areaRect.height) * 1.25f;
            var cameraObject = new GameObject("Outline Camera");
            cameraObject.transform.SetParent(visuals.transform, false);
            cameraObject.transform.localPosition = new Vector3(bounds.center.x, bounds.center.y, -10f);
            preview = cameraObject.AddComponent<Camera>();
            preview.enabled = false;
            preview.orthographic = true;
            preview.orthographicSize = size;
            preview.aspect = aspect;
            preview.nearClipPlane = 0.1f; preview.farClipPlane = 50f;
            preview.cullingMask = 1 << PreviewLayer;
            preview.clearFlags = CameraClearFlags.SolidColor;
            preview.backgroundColor = new Color(0.018f, 0.03f, 0.055f, 1f);
            int height = Mathf.Clamp(Mathf.RoundToInt(areaRect.height), 480, 1440);
            texture = new RenderTexture(Mathf.RoundToInt(height * aspect), height, 16);
            texture.Create();
            preview.targetTexture = texture;
        }

        public void ShowSelection(WorldMapDefinition world)
        {
            if (TryPrepareNeon(world, true)) return;
            var modules = world.OrderedModules();
            PrepareBoard(world, modules, "关 卡 拼 图", true);
            selectionWorld = world;
            foreach (WorldMapModule module in modules)
                pieces[module] = CreatePiece(world, module);
            preview.Render();
        }

        private bool TryPrepareNeon(WorldMapDefinition world, bool asSelection)
        {
            if (SceneScreen==null)
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                    foreach (NeonWorldMapScreen screen in root.GetComponentsInChildren<NeonWorldMapScreen>(true))
                        if (screen.Source==world) { SceneScreen=screen; break; }
            if (SceneScreen==null || SceneScreen.Source!=world) return false;
            Hide();
            neonScreen = SceneScreen;
            neonScreen.Initialize(asSelection);
            return true;
        }

        // Screen coordinates follow Input.mousePosition (origin at the bottom left).
        public bool TryPick(Vector2 screenPoint, out WorldMapModule picked)
        {
            if (neonScreen != null) return neonScreen.TryPick(screenPoint, out picked);
            picked = null;
            if (!selection || preview == null || Screen.width == 0 || Screen.height == 0) return false;
            Rect rect = RenderRect;
            if (!rect.Contains(new Vector2(screenPoint.x, Screen.height - screenPoint.y))) return false;
            Vector2 local = preview.ViewportToWorldPoint(new Vector3((screenPoint.x - rect.x) / rect.width,
                (screenPoint.y - (Screen.height - rect.yMax)) / rect.height, 10f)) - visuals.transform.position;
            float closest = float.MaxValue;
            foreach (WorldMapModule module in selectionWorld.OrderedModules())
            {
                float unit = module.Level.Palette.UnitSize;
                if (!module.Contains(Vector2Int.FloorToInt(local / unit))) continue;
                float distance = Vector2.SqrMagnitude(local - (Vector2)pieces[module].localPosition
                    - (Vector2)pieces[module].Find("Number").localPosition);
                if (distance >= closest) continue;
                closest = distance;
                picked = module;
            }
            return picked != null;
        }

        public Vector2 LabelScreenPosition(WorldMapModule module)
        {
            if (neonScreen != null) return neonScreen.LabelScreenPosition(module);
            Vector3 point = preview.WorldToViewportPoint(pieces[module].Find("Number").position);
            Rect rect = RenderRect;
            return new Vector2(rect.x + point.x * rect.width, Screen.height - rect.yMax + point.y * rect.height);
        }

        private static Color SequenceColor(int number)
        {
            if (number >= 1 && number <= 5) return Color.red;
            if (number >= 6 && number <= 12) return Color.yellow;
            if (number >= 13 && number <= 20) return new Color(0.25f, 0.55f, 1f);
            return Color.white;
        }

        private LevelOutlineData GetOutline(LevelDefinition level)
        {
            if (level.Outline != null) return level.Outline;
            var data = ScriptableObject.CreateInstance<LevelOutlineData>();
            data.Build(level.Footprint);
            temporaryOutlines.Add(data);
            return data;
        }

        private Transform CreatePiece(WorldMapDefinition world, WorldMapModule module)
        {
            var piece = new GameObject("Outline " + world.SequenceNumber(module));
            piece.transform.SetParent(visuals.transform, false);
            float unit = module.Level.Palette.UnitSize;
            piece.transform.localPosition = (Vector2)module.Position * unit;
            LevelOutlineData data = GetOutline(module.Level);
            Color tint = SequenceColor(world.SequenceNumber(module));
            foreach (LevelOutlineLoop loop in data.Loops)
            {
                Vector2[] centerline = Centerline(loop.Points);
                    var edge = new GameObject("Contour");
                    edge.layer = PreviewLayer;
                    edge.transform.SetParent(piece.transform, false);
                    var line = edge.AddComponent<LineRenderer>();
                    line.sharedMaterial = lineMaterial;
                    line.useWorldSpace = false; line.loop = true;
                    line.positionCount = centerline.Length;
                    line.widthMultiplier = lineWidth;
                    line.startColor = line.endColor = tint;
                    line.numCornerVertices = 0; line.numCapVertices = 0;
                    line.sortingOrder = 1;
                    for (int i = 0; i < centerline.Length; i++) line.SetPosition(i, centerline[i] * unit);
            }
            var label = new GameObject("Number");
            label.layer = PreviewLayer;
            label.transform.SetParent(piece.transform, false);
            label.transform.localPosition = (Vector2)data.LabelPosition * unit;
            var text = label.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.text = world.SequenceNumber(module).ToString();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 64;
            float labelHeight = Mathf.Min(Mathf.Min(data.Bounds.width, data.Bounds.height) * unit * 0.25f,
                data.Bounds.width * unit * 0.45f / Mathf.Max(1f, text.text.Length * 0.6f));
            if (selection) labelHeight = preview.orthographicSize * 2f / RenderRect.height * 18f;
            // TextMesh characterSize scales with fontSize; compensate for the high-resolution font atlas.
            text.characterSize = labelHeight * 10f / text.fontSize;
            text.color = tint;
            var renderer = label.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = text.font.material;
            renderer.sortingOrder = 3;
            return piece.transform;
        }

        private static Vector2[] Centerline(Vector2[] boundary)
        {
            var points = new Vector2[boundary.Length];
            for (int i = 0; i < boundary.Length; i++)
            {
                Vector2 before = (boundary[i] - boundary[(i + boundary.Length - 1) % boundary.Length]).normalized;
                Vector2 after = (boundary[(i + 1) % boundary.Length] - boundary[i]).normalized;
                Vector2 inwardBefore = new Vector2(-before.y, before.x);
                Vector2 inwardAfter = new Vector2(-after.y, after.x);
                float divisor = 1f + Vector2.Dot(before, after);
                // Occupied cells lie to the left, including around holes and concave corners.
                points[i] = boundary[i] + (divisor > 0.001f
                    ? (inwardBefore + inwardAfter) * (0.5f / divisor) : inwardBefore * 0.5f);
            }
            return points;
        }

        public void ShowCompletion()
        {
            if (neonScreen != null)
            {
                neonScreen.ShowCompletion();
                return;
            }
            complete = true;
            WorldMapProgression flow = WorldMapProgression.Current;
            caption = flow.CompletedCount == flow.LevelCount ? "大世界已完成" : "最终关已通过";
        }
        private void LateUpdate() { if (visible && preview != null) preview.Render(); }

        private void OnGUI()
        {
            if (!visible || texture == null) return;
            int depth = GUI.depth;
            GUI.depth = 10;
            GUI.DrawTexture(RenderRect, texture, ScaleMode.StretchToFill);
            if (captionStyle == null) captionStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 24, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.6f, 1f, 0.9f) } };
            GUI.Label(new Rect(0f, 24f, Screen.width, 45f), caption, captionStyle);
            if (complete && !WorldMapProgression.Current.IsTransitioning)
            {
                WorldMapProgression flow = WorldMapProgression.Current;
                GUI.Label(new Rect(0f, Screen.height - 100f, Screen.width, 30f),
                    "已通过 " + flow.CompletedCount + " / " + flow.LevelCount + " 关", captionStyle);
                if (GUI.Button(new Rect(Screen.width * 0.5f - 210f, Screen.height - 60f, 200f, 36f), "返回第一关"))
                    flow.GoToSequence(GetComponent<WorldMapRoot>().Source.SequenceNumber(flow.Modules[0]));
                if (GUI.Button(new Rect(Screen.width * 0.5f + 10f, Screen.height - 60f, 200f, 36f), "返回选关"))
                    flow.ReturnToSelection();
            }
            GUI.depth = depth;
        }

        public void Hide()
        {
            if (neonScreen != null)
            {
                neonScreen.HidePresentation();
                neonScreen = null;
            }
            visible = complete = selection = false;
            selectionWorld = null;
            pieces.Clear();
            if (preview != null) preview.targetTexture = null;
            if (texture != null) { texture.Release(); Destroy(texture); }
            if (visuals != null) { visuals.SetActive(false); Destroy(visuals); }
            if (lineMaterial != null) Destroy(lineMaterial);
            foreach (LevelOutlineData outline in temporaryOutlines) Destroy(outline);
            temporaryOutlines.Clear();
            preview = null; texture = null; visuals = null; lineMaterial = null;
        }

        private void OnDisable() => Hide();
    }
}
