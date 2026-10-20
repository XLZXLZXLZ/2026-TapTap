using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    [InitializeOnLoad]
    public static class BlueprintMapValidation
    {
        private const string Pending = "TapTap.BlueprintValidation";
        private static readonly string Output = Path.GetFullPath("Logs/Blueprint");
        private static int stage, frame;
        private static double deadline, since;
        private static bool partialReveal, travelled, capturedOrb, sawConfirmScale;
        private static bool sawReleaseScale;
        private static Vector2 pointerPress;
        private static BlueprintWorldMapScreen screen;
        private static Coroutine animation;

        static BlueprintMapValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
                deadline = EditorApplication.timeSinceStartup + 120d;
                EditorApplication.update += Tick;
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated project.");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "results.txt"), "");
            try
            {
                var art = AssetDatabase.LoadAssetAtPath<BlueprintMapArt>(BlueprintMapAssets.ArtPath);
                Check(art != null && art.Pieces.Length == 20, "Expected 20 mapped art pairs");
                Check(art.Pieces.All(piece => piece.Normal != null && piece.Glow != null), "Missing normal/glow artwork");
                foreach (var piece in art.Pieces)
                {
                    Check(piece.Contains(piece.TravelPoint), "Travel anchor lies outside piece " + piece.Sequence);
                    Check(!piece.Contains(Vector2.zero), "Transparent canvas intercepts piece " + piece.Sequence);
                    Check(!piece.Normal.isReadable && !piece.Glow.isReadable, "Full canvas retained on CPU");
                }
                Check(!art.Pieces[8].Contains(new Vector2(1000f, 1360f)), "Concave room notch intercepts transparent clicks");
                FindCutout(art.Pieces[0]);
                var selection = AssetDatabase.LoadAssetAtPath<GameObject>(BlueprintMapAssets.SelectionPath).GetComponent<BlueprintWorldMapScreen>();
                var transition = AssetDatabase.LoadAssetAtPath<GameObject>(BlueprintMapAssets.TransitionPath).GetComponent<BlueprintWorldMapScreen>();
                Check(selection.Art == transition.Art && selection != transition, "Screens must share only artwork");
                Check(selection.Role != transition.Role && selection.Pieces.Length == 20 && transition.Pieces.Length == 20, "Independent roles or pieces missing");
                Check(selection.Viewport.anchorMin == Vector2.zero && selection.Viewport.anchorMax == Vector2.one,
                    "Inset selection clip creates a visible seam inside the screen");
                Check(selection.GetComponentsInChildren<NeonLevelOutline>(true).Length == 0
                    && transition.GetComponentsInChildren<NeonLevelOutline>(true).Length == 0, "Legacy outlines remain in new presentations");
                Pass("20 matched normal/glow textures; baked alpha hit regions and valid anchors; separate scene prefabs; no legacy line graphics.");
                ValidateBindings();
                PlayerSettings.companyName = "TapTapValidation";
                PlayerSettings.productName = "BlueprintValidation";
                var world = selection.Source;
                WorldMapProgressStore.Clear(world);
                EditorSceneManager.OpenScene(world.SelectionScenePath);
                SessionState.SetBool(Pending, true);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception) { Finish(exception); }
        }

        public static void ValidateSavedScenesBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated project.");
            Directory.CreateDirectory(Output);
            ValidateBindings();
        }

        private static void ValidateBindings()
        {
            var world = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
            foreach (string path in new[] { world.SelectionScenePath, world.PlayScenePath, "Assets/Scenes/ReturnJourneyPreview.unity" })
            {
                bool selection = path == world.SelectionScenePath;
                Scene scene = EditorSceneManager.OpenScene(path);
                Check(!scene.GetRootGameObjects().Any(root => root.GetComponentInChildren<NeonWorldMapScreen>(true) != null), "Legacy screen still present in scene");
                var presentation = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BlueprintWorldMapScreen>(true)).Single();
                var view = scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<WorldMapAssemblyView>(true)).Single();
                Check(view.SceneScreen == null && view.BlueprintScreen == presentation, "Saved scene UI binding is invalid");
                Check(presentation.Role == (selection ? BlueprintWorldMapScreen.ScreenRole.Selection : BlueprintWorldMapScreen.ScreenRole.Transition), "Wrong scene role");
            }
            Pass("Selection, gameplay and return-journey preview bind the appropriate independent blueprint prefab; legacy screen instances removed.");
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
            frame = Time.frameCount;
            try
            {
                Check(EditorApplication.timeSinceStartup < deadline, "Runtime validation timeout at " + stage);
                if (stage == 0)
                {
                    var menu = WorldLevelMenu.Current;
                    if (menu == null || menu.View == null || menu.View.ActiveBlueprint == null) return;
                    screen = menu.View.ActiveBlueprint;
                    Check(screen.Pieces.Count(piece => piece.gameObject.activeSelf) == 1, "Fresh save should expose only first level");
                    Check(screen.TryPick(screen.LabelScreenPosition(menu.Selected), out var picked) && picked == menu.Selected, "First tile cannot be picked");
                    Vector2 cutout = FindCutout(screen.Art.Pieces[0]);
                    Vector2 cutoutScreen = RectTransformUtility.WorldToScreenPoint(screen.UICamera, screen.Board.TransformPoint(cutout));
                    Check(screen.TryPick(cutoutScreen, out picked) && picked == menu.Selected, "Decorative cutout cannot be picked");
                    CheckHorizontalFit(screen);
                    Capture(screen, "selection-first.png");
                    Capture(screen, "selection-wide.png", 1920, 1080);
                    Capture(screen, "selection-portrait.png", 900, 1200);
                    foreach (var unlockedModule in menu.World.OrderedModules()) WorldMapProgressStore.Unlock(menu.World, unlockedModule); menu.SendMessage("Refresh");
                    Check(screen.Pieces.All(piece => piece.gameObject.activeSelf), "Unlock all did not restore all 20 pieces");
                    // Exercise hover without the real menu's physical pointer overwriting it.
                    // The production menu is restored before testing its actual scene load.
                    menu.View.Hide();
                    screen = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BlueprintMapAssets.SelectionPath))
                        .GetComponent<BlueprintWorldMapScreen>();
                    screen.Initialize(true);
                    screen.SetHovered(screen.Source.OrderedModules()[0]);
                    since = EditorApplication.timeSinceStartup;
                    stage = 1;
                }
                else if (stage == 1 && EditorApplication.timeSinceStartup - since > 0.3d)
                {
                    Check(screen.Pieces[0].Emphasis > 0.95f, "Hover did not fade to glow");
                    Check(screen.Pieces[0].Normal.color.a < 0.05f && screen.Pieces[0].Glow.color.a > 0.95f, "Normal and glow do not crossfade");
                    Capture(screen, "selection-glow.png");
                    screen.SetHovered(null);
                    screen.ScrollBy(float.MaxValue);
                    since = EditorApplication.timeSinceStartup;
                    stage = 2;
                }
                else if (stage == 2 && EditorApplication.timeSinceStartup - since > 1d)
                {
                    Check(Mathf.Abs(screen.ScrollOffset - screen.ScrollLimit) < 2f,
                        "Scroll did not reach bounded bottom: " + screen.ScrollOffset + " / " + screen.ScrollLimit);
                    Check(screen.Pieces[0].Emphasis < 0.05f, "Hover exit left glow stuck on");
                    Capture(screen, "selection-bottom.png");
                    var world = screen.Source;
                    var modules = world.OrderedModules();
                    // Jump across distant pieces to exercise the transition camera's pair framing.
                    screen.SetPressed(modules[19]);
                    Time.timeScale = 0f;
                    since = EditorApplication.timeSinceStartup;
                    stage = 7;
                }
                else if (stage == 7 && EditorApplication.timeSinceStartup - since > 0.15d)
                {
                    Check(screen.Pieces[19].transform.localScale.x < 0.97f, "Held button did not shrink during paused simulation");
                    animation = screen.StartCoroutine(screen.Confirm(screen.Source.OrderedModules()[19]));
                    since = EditorApplication.timeSinceStartup;
                    stage = 3;
                }
                else if (stage == 3)
                {
                    if (Mathf.Abs(screen.Pieces[19].transform.localScale.x - 1f) > 0.01f) sawConfirmScale = true;
                    if (EditorApplication.timeSinceStartup - since < 0.4d || screen.Pieces[19].IsConfirming) return;
                    Check(sawConfirmScale, "Confirmation never scaled the chosen tile");
                    Check(Mathf.Abs(screen.Pieces[19].transform.localScale.x - 1f) < 0.001f, "Confirmation did not finish during paused simulation");
                    Time.timeScale = 1f;
                    Pass("Fresh save and unlock-all; alpha hit testing; hover entry/exit; bounded scrolling; confirmation uses unscaled time.");
                    Object.Destroy(screen.gameObject);
                    WorldLevelMenu.Current.View.ShowSelection(WorldLevelMenu.Current.World);
                    screen = WorldLevelMenu.Current.View.ActiveBlueprint;
                    pointerPress = RectTransformUtility.WorldToScreenPoint(screen.UICamera, screen.Board.TransformPoint(FindCutout(screen.Art.Pieces[0])));
                    WorldLevelMenu.Current.PointerDown(pointerPress);
                    since = EditorApplication.timeSinceStartup;
                    stage = 8;
                }
                else if (stage == 8 && EditorApplication.timeSinceStartup - since > 0.15d)
                {
                    Check(screen.Pieces[0].transform.localScale.x < 0.97f && !WorldMapSceneRouter.IsLoading,
                        "Pointer down must shrink immediately without entering the level");
                    Check(screen.TryPick(pointerPress, out _), "Pressed scale moved the hit area away from the pointer");
                    Capture(screen, "selection-pressed.png");
                    WorldLevelMenu.Current.CancelPress();
                    since = EditorApplication.timeSinceStartup;
                    stage = 9;
                }
                else if (stage == 9 && EditorApplication.timeSinceStartup - since > 0.15d)
                {
                    Check(Mathf.Abs(screen.Pieces[0].transform.localScale.x - 1f) < 0.001f && !WorldMapSceneRouter.IsLoading,
                        "Cancelling a press must restore scale and remain in the menu");
                    WorldLevelMenu.Current.PointerDown(pointerPress);
                    since = EditorApplication.timeSinceStartup;
                    stage = 10;
                }
                else if (stage == 10 && EditorApplication.timeSinceStartup - since > 0.15d)
                {
                    Check(WorldLevelMenu.Current.PointerUp(pointerPress), "Release on a cutout did not confirm entry");
                    Check(!WorldMapSceneRouter.IsLoading, "Scene load started before release rebound");
                    stage = 11;
                }
                else if (stage == 11)
                {
                    if (!sawReleaseScale && screen != null && screen.Pieces[0].transform.localScale.x > 1.01f)
                    { sawReleaseScale = true; Capture(screen, "selection-released.png"); }
                    if (!WorldMapSceneRouter.IsLoading && WorldLevelMenu.Current != null) return;
                    Check(sawReleaseScale, "Released button never rebounded before scene entry");
                    Pass("Actual menu pointer routing: cutout press shrinks while held; cancel restores; release rebounds before loading; hit area remains stable.");
                    stage = 4;
                }
                else if (stage == 4)
                {
                    var flow = WorldMapProgression.Current;
                    if (flow == null || flow.CurrentModule == null || WorldMapSceneRouter.IsLoading) return;
                    screen = flow.GetComponent<WorldMapAssemblyView>().BlueprintScreen;
                    Check(screen != null && screen.Role == BlueprintWorldMapScreen.ScreenRole.Transition, "Gameplay uses wrong screen");
                    Check(!screen.Canvas.enabled, "Transition screen leaked into gameplay");
                    Check(flow.CompleteCurrent(flow.CurrentRegion.GetComponentInChildren<LevelEndpoint>()), "Cannot start real endpoint transition");
                    stage = 5;
                }
                else if (stage == 5)
                {
                    var flow = WorldMapProgression.Current;
                    var next = screen.Pieces[1];
                    if (screen.Phase == NeonWorldMapScreen.TransitionPhase.Drawing && next.Opacity > 0.1f && next.Opacity < 0.85f)
                    { partialReveal = true; Capture(screen, "transition-fade.png"); }
                    if (screen.ActiveTravel != null && screen.Phase == NeonWorldMapScreen.TransitionPhase.OrbTravelling)
                    {
                        travelled = true;
                        var travel = screen.ActiveTravel;
                        float progress = Vector2.Distance(travel.Orb.anchoredPosition, travel.Origin)
                            / Mathf.Max(1f, Vector2.Distance(travel.Origin, travel.Destination));
                        if (!capturedOrb && progress > 0.35f)
                        {
                            Capture(screen, "transition-orb.png");
                            Check(travel.Trail.SampleCount > 1 && !travel.Trail.canvasRenderer.cull, "Scroll mask culled the travel trail");
                            capturedOrb = true;
                        }
                    }
                    if (flow.IsTransitioning || flow.CurrentIndex != 1) return;
                    Check(partialReveal && travelled && capturedOrb, "Whole-tile fade or retained orb was not observed");
                    Check(screen.ActiveTravel == null && !screen.Canvas.enabled && Time.timeScale > 0f, "Transition failed to clean up");
                    Pass("Real menu scene load and endpoint transition: tile fades, orb travels, gameplay resumes and presentation is hidden.");
                    // Explicitly interrupt an independent transition during travel.
                    screen.Initialize(false);
                    var modules = screen.Source.OrderedModules();
                    animation = screen.StartCoroutine(screen.Play(new System.Collections.Generic.HashSet<string> { modules[0].Id }, modules[19], 0.1f, modules[0]));
                    stage = 6;
                }
                else if (stage == 6 && screen.ActiveTravel != null)
                {
                    CheckInside(screen, screen.Art.Pieces[0].GlowBounds);
                    CheckInside(screen, screen.Art.Pieces[19].GlowBounds);
                    Capture(screen, "transition-distant.png");
                    screen.StopCoroutine(animation);
                    screen.HidePresentation();
                    Check(screen.ActiveTravel == null && !screen.Canvas.enabled, "Interrupted orb leaked");
                    Pass("Distant origin/destination fit in the transition view; interruption cleans up orb and trail.");
                    Finish(null);
                }
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static void Capture(BlueprintWorldMapScreen view, string name, int width = 1600, int height = 1000)
        {
            bool topAligned = view.Role == BlueprintWorldMapScreen.ScreenRole.Selection
                && Mathf.Abs(view.ScrollOffset + view.ScrollLimit) < 2f;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var previous = view.UICamera.targetTexture;
            var active = RenderTexture.active;
            try
            {
                view.UICamera.targetTexture = rt;
                Canvas.ForceUpdateCanvases();
                view.SendMessage("LateUpdate");
                CheckHorizontalFit(view);
                if (topAligned) Check(Mathf.Abs(view.ScrollOffset + view.ScrollLimit) < 2f, "Resize lost top alignment");
                view.UICamera.Render();
                RenderTexture.active = rt;
                var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply();
                File.WriteAllBytes(Path.Combine(Output, name), texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }
            finally
            {
                view.UICamera.targetTexture = previous;
                Canvas.ForceUpdateCanvases();
                view.SendMessage("LateUpdate");
                if (topAligned) Check(Mathf.Abs(view.ScrollOffset + view.ScrollLimit) < 2f, "Restoring viewport lost top alignment");
                RenderTexture.active = active; rt.Release(); Object.DestroyImmediate(rt);
            }
        }
        private static void CheckHorizontalFit(BlueprintWorldMapScreen view)
        {
            Rect bounds = view.Art.ContentBounds;
            foreach (float x in new[] { bounds.xMin, bounds.xMax })
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(view.UICamera, view.Board.TransformPoint(new Vector3(x, bounds.center.y, 0f)));
                RectTransformUtility.ScreenPointToLocalPointInRectangle(view.Viewport, point, view.UICamera, out var local);
                Check(local.x >= view.Viewport.rect.xMin && local.x <= view.Viewport.rect.xMax, "Map clips horizontally");
            }
        }
        private static Vector2 FindCutout(BlueprintMapArt.Piece piece)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(piece.Normal)));
                Color32[] pixels = texture.GetPixels32();
                Rect bounds = piece.NormalBounds;
                for (int y = 12; y < bounds.height - 12; y += 4)
                    for (int x = 12; x < bounds.width - 12; x += 4)
                    {
                        Vector2 point = bounds.min + new Vector2(x, y);
                        if (pixels[(int)point.y * texture.width + (int)point.x].a == 0 && piece.Contains(point)) return point;
                    }
                throw new InvalidOperationException("No interactive transparent interior found in piece " + piece.Sequence);
            }
            finally { Object.DestroyImmediate(texture); }
        }
        private static void CheckInside(BlueprintWorldMapScreen view, Rect bounds)
        {
            foreach (var corner in new[] { bounds.min, bounds.max })
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(view.UICamera, view.Board.TransformPoint(corner));
                Check(RectTransformUtility.RectangleContainsScreenPoint(view.Viewport, point, view.UICamera), "Transition clips a framed room");
            }
        }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Pass(string message) => File.AppendAllText(Path.Combine(Output, "results.txt"), "PASS " + message + "\n");
        private static void Finish(Exception exception)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(Pending, false);
            Time.timeScale = 1f;
            if (exception != null) { File.AppendAllText(Path.Combine(Output, "results.txt"), "FAIL " + exception + "\n"); Debug.LogException(exception); }
            else Pass("All blueprint validation checks completed.");
            EditorApplication.Exit(exception == null ? 0 : 1);
        }
    }
}
