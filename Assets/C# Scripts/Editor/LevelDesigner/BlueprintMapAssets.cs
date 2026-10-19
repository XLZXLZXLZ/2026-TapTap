using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    public static class BlueprintMapAssets
    {
        public const string Folder = "Assets/Resources/WorldMapSelection/";
        public const string SelectionPath = Folder + "BlueprintSelection.prefab";
        public const string TransitionPath = Folder + "BlueprintTransition.prefab";
        public const string ArtPath = Folder + "BlueprintArt.asset";
        private const string SourceFolder = "Assets/Art/Sprite/UI/ChooseLevel";
        // Number centers in the supplied full canvas (top-left coordinates).
        // Keep the orb on the printed numbers, including the concave L-shaped rooms.
        private static readonly Vector2[] NumberCenters =
        {
            new Vector2(629, 315), new Vector2(950, 525), new Vector2(630, 590), new Vector2(1170, 255),
            new Vector2(1280, 440), new Vector2(1405, 690), new Vector2(960, 790), new Vector2(1360, 960),
            new Vector2(870, 990), new Vector2(1305, 1288), new Vector2(515, 1395), new Vector2(737, 1375),
            new Vector2(975, 1468), new Vector2(517, 1680), new Vector2(1068, 1660), new Vector2(518, 1938),
            new Vector2(1395, 1645), new Vector2(1285, 1925), new Vector2(750, 1770), new Vector2(845, 1998)
        };

        [MenuItem("TapTap/World Map/Rebuild Blueprint Artwork")]
        public static void Build()
        {
            var files = Directory.GetFiles(SourceFolder, "*.png").OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (files.Length != 45) throw new InvalidOperationException("Expected background, shade, 21 normal tiles, title and 21 glow tiles; found " + files.Length);
            var world = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
            var modules = world.OrderedModules().Where(module => world.SequenceNumber(module) >= 1 && world.SequenceNumber(module) <= 20).ToList();
            if (modules.Count != 20) throw new InvalidOperationException("Review blueprint numbering before changing the 20-level mapping.");
            foreach (var file in files)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(file.Replace('\\', '/'));
                importer.textureType = TextureImporterType.Default;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 4096;
                importer.isReadable = false;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            var art = AssetDatabase.LoadAssetAtPath<BlueprintMapArt>(ArtPath);
            if (art == null) { art = ScriptableObject.CreateInstance<BlueprintMapArt>(); AssetDatabase.CreateAsset(art, ArtPath); }
            art.Source = world;
            art.Background = Load(files[0]); art.Shade = Load(files[1]); art.Title = Load(files[23]);
            art.CanvasSize = new Vector2(1920, 2160);
            art.Pieces = new BlueprintMapArt.Piece[20];
            bool first = true;
            for (int i = 0; i < art.Pieces.Length; i++)
            {
                Texture2D normal = Read(files[i + 2]), glow = Read(files[i + 24]);
                Rect normalBounds = Bounds(normal), glowBounds = Bounds(glow);
                var piece = new BlueprintMapArt.Piece
                {
                    Sequence = i + 1, ModuleId = modules[i].Id, Normal = Load(files[i + 2]), Glow = Load(files[i + 24]),
                    NormalBounds = normalBounds, GlowBounds = glowBounds,
                    MaskWidth = Mathf.CeilToInt(normalBounds.width / 4f), MaskHeight = Mathf.CeilToInt(normalBounds.height / 4f)
                };
                piece.HitMask = new byte[(piece.MaskWidth * piece.MaskHeight + 7) / 8];
                Color32[] pixels = normal.GetPixels32();
                bool[] silhouette = FilledSilhouette(normal, normalBounds, pixels);
                float nearest = float.MaxValue;
                Vector2 label = new Vector2(NumberCenters[i].x, art.CanvasSize.y - NumberCenters[i].y);
                // Fill enclosed decorative cutouts before sampling the interaction region.
                // Exterior transparent space, including concave notches, remains unclickable.
                for (int y = 0; y < piece.MaskHeight; y++)
                    for (int x = 0; x < piece.MaskWidth; x++)
                    {
                        Vector2 point = normalBounds.min + new Vector2((x + 0.5f) / piece.MaskWidth * normalBounds.width,
                            (y + 0.5f) / piece.MaskHeight * normalBounds.height);
                        int column = (int)point.x - (int)normalBounds.x;
                        int row = (int)point.y - (int)normalBounds.y;
                        if (!silhouette[row * (int)normalBounds.width + column]) continue;
                        int bit = y * piece.MaskWidth + x;
                        piece.HitMask[bit / 8] |= (byte)(1 << (bit % 8));
                        float distance = (point - label).sqrMagnitude;
                        if (distance < nearest) { nearest = distance; piece.TravelPoint = point; }
                    }
                if (nearest == float.MaxValue) throw new InvalidOperationException("Empty alpha mask: " + files[i + 2]);
                art.Pieces[i] = piece;
                art.ContentBounds = first ? glowBounds : Union(art.ContentBounds, glowBounds);
                first = false;
                Object.DestroyImmediate(normal); Object.DestroyImmediate(glow);
            }
            // The source title is also positioned in the same 1920 x 2160 canvas.
            Texture2D title = Read(files[23]);
            Rect titleBounds = Bounds(title);
            Object.DestroyImmediate(title);
            art.ContentBounds = Union(art.ContentBounds, titleBounds);
            EditorUtility.SetDirty(art);
            CreateScreen(world, art, titleBounds, true, SelectionPath);
            CreateScreen(world, art, titleBounds, false, TransitionPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Blueprint assets built: 20 normal/glow pairs; independent selection and transition prefabs. Tile 21 intentionally unbound.");
        }

        private static Texture2D Load(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path.Replace('\\', '/'));
        private static bool[] FilledSilhouette(Texture2D texture, Rect bounds, Color32[] pixels)
        {
            int width = (int)bounds.width, height = (int)bounds.height;
            int stride = width + 2, rows = height + 2;
            var solid = new bool[stride * rows];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    solid[(y + 1) * stride + x + 1] = pixels[((int)bounds.y + y) * texture.width + (int)bounds.x + x].a >= 8;
            var exterior = new bool[solid.Length];
            var queue = new Queue<int>();
            queue.Enqueue(0); exterior[0] = true;
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue(), x = cell % stride, y = cell / stride;
                if (x > 0) Visit(cell - 1);
                if (x + 1 < stride) Visit(cell + 1);
                if (y > 0) Visit(cell - stride);
                if (y + 1 < rows) Visit(cell + stride);
            }
            var result = new bool[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    result[y * width + x] = !exterior[(y + 1) * stride + x + 1];
            return result;

            void Visit(int cell)
            {
                if (solid[cell] || exterior[cell]) return;
                exterior[cell] = true; queue.Enqueue(cell);
            }
        }
        private static Texture2D Read(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException("Cannot read " + path);
            if (texture.width != 1920 || texture.height != 2160) throw new InvalidOperationException("Unexpected artwork canvas: " + path);
            return texture;
        }
        private static Rect Bounds(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            int xmin = texture.width, ymin = texture.height, xmax = 0, ymax = 0;
            for (int y = 0; y < texture.height; y++)
                for (int x = 0; x < texture.width; x++)
                    if (pixels[y * texture.width + x].a > 0)
                    { xmin = Mathf.Min(xmin, x); ymin = Mathf.Min(ymin, y); xmax = Mathf.Max(xmax, x + 1); ymax = Mathf.Max(ymax, y + 1); }
            return Rect.MinMaxRect(xmin, ymin, xmax, ymax);
        }
        private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
            Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        private static void CreateScreen(WorldMapDefinition world, BlueprintMapArt art, Rect titleBounds, bool selection, string path)
        {
            // Retain the authored camera, URP Bloom for the travel orb, and canvas setup.
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "WorldCharacterSelection.prefab");
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(original);
            try
            {
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var legacy = root.GetComponent<NeonWorldMapScreen>();
                var canvas = legacy.Canvas;
                var camera = legacy.UICamera;
                var bloom = legacy.BloomVolume;
                Object.DestroyImmediate(legacy.MapHost.gameObject);
                Object.DestroyImmediate(legacy);
                root.name = selection ? "Blueprint Selection" : "Blueprint Transition";
                var screen = root.AddComponent<BlueprintWorldMapScreen>();
                screen.Source = world; screen.Art = art; screen.Canvas = canvas; screen.UICamera = camera;
                screen.BloomVolume = bloom;
                screen.Role = selection ? BlueprintWorldMapScreen.ScreenRole.Selection : BlueprintWorldMapScreen.ScreenRole.Transition;
                screen.ShowOnSceneStart = selection;
                screen.PreviewInEditor = selection;
                screen.WidthFill = selection ? 0.75f : 0.88f;
                RectTransform background = CreateRect("Blueprint Background", canvas.transform);
                Stretch(background, -64f);
                var backgroundImage = background.gameObject.AddComponent<RawImage>();
                backgroundImage.texture = art.Background; backgroundImage.raycastTarget = false;
                screen.Background = background; screen.BackgroundImage = backgroundImage;
                RectTransform shade = CreateRect("Screen Shade", canvas.transform);
                Stretch(shade, 0f);
                var shadeImage = shade.gameObject.AddComponent<RawImage>();
                shadeImage.texture = art.Shade; shadeImage.raycastTarget = false;
                // This export contains a 1920 x 1080 vignette in the upper half
                // of its transparent 1920 x 2160 canvas. Use that region as a screen overlay.
                shadeImage.uvRect = new Rect(0f, 0.5f, 1f, 0.5f);
                RectTransform viewport = CreateRect(selection ? "Selection Viewport" : "Transition Viewport", canvas.transform);
                // Clip only at the physical screen edges; inset masks visibly slice the map.
                viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
                viewport.offsetMin = viewport.offsetMax = Vector2.zero;
                viewport.gameObject.AddComponent<RectMask2D>();
                screen.Viewport = viewport;
                RectTransform board = CreateRect(selection ? "Scrollable Map" : "Transition Map", viewport);
                board.pivot = Vector2.zero; board.sizeDelta = art.CanvasSize;
                screen.Board = board;
                Image("Select Title", board, art.Title, titleBounds, Vector2.zero, art.CanvasSize).gameObject.SetActive(selection);
                var pieces = new List<BlueprintLevelPiece>();
                for (int i = 0; i < art.Pieces.Length; i++)
                {
                    var data = art.Pieces[i];
                    RectTransform rect = CreateRect("Level " + data.Sequence.ToString("00"), board);
                    rect.anchorMin = rect.anchorMax = Vector2.zero;
                    rect.anchoredPosition = data.Center;
                    rect.sizeDelta = data.NormalBounds.size;
                    var piece = rect.gameObject.AddComponent<BlueprintLevelPiece>();
                    piece.ArtIndex = i;
                    piece.Normal = Image("Normal", rect, data.Normal, data.NormalBounds, data.Center, art.CanvasSize);
                    piece.Glow = Image("Glow", rect, data.Glow, data.GlowBounds, data.Center, art.CanvasSize);
                    piece.Glow.color = new Color(1f, 1f, 1f, 0f);
                    piece.TravelAnchor = CreateRect("Travel Anchor", rect);
                    piece.TravelAnchor.anchoredPosition = data.TravelPoint - data.Center;
                    piece.TravelAnchor.sizeDelta = Vector2.one;
                    pieces.Add(piece);
                }
                screen.Pieces = pieces.ToArray();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.gameObject.layer = parent.gameObject.layer;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }
        private static void Stretch(RectTransform rect, float margin)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * margin; rect.offsetMax = Vector2.one * -margin;
        }
        private static RawImage Image(string name, RectTransform parent, Texture2D texture, Rect bounds, Vector2 origin, Vector2 canvasSize)
        {
            RectTransform rect = CreateRect(name, parent);
            if (parent.pivot == Vector2.zero) rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.anchoredPosition = bounds.center - origin;
            rect.sizeDelta = bounds.size;
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture; image.raycastTarget = false;
            image.uvRect = new Rect(bounds.x / canvasSize.x, bounds.y / canvasSize.y, bounds.width / canvasSize.x, bounds.height / canvasSize.y);
            return image;
        }
    }
}
