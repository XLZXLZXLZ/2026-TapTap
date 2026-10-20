using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TapTap
{
    [ExecuteAlways]
    public sealed class BlueprintWorldMapScreen : MonoBehaviour
    {
        public enum ScreenRole { Selection, Transition }
        public ScreenRole Role;
        public WorldMapDefinition Source;
        public BlueprintMapArt Art;
        public Camera UICamera;
        public Canvas Canvas;
        public Behaviour BloomVolume;
        public RectTransform Viewport, Board, Background;
        public RawImage BackgroundImage;
        public BlueprintLevelPiece[] Pieces;
        public bool ShowOnSceneStart = true, PreviewInEditor = true;
        [Range(0.6f, 1f)] public float WidthFill = 0.88f;
        [Min(0f)] public float VerticalPadding = 48f;
        [Min(1f)] public float ScrollSpeed = 150f;
        [Min(1f)] public float ScrollSmoothing = 12f;
        [Range(0f, 50f)] public float BackgroundParallax = 24f;
        [Range(0f, 20f)] public float PointerParallax = 7f;
        [Min(0f)] public float RevealDelay = 0.2f;
        [Min(0.1f)] public float RevealDuration = 0.55f;
        [Min(0.1f)] public float OrbTravelDuration = 0.5f;
        [Min(0f)] public float OrbPauseDuration = 0.6f;
        [Min(1f)] public float OrbSize = 38f;
        public WorldMapTravelEffect ActiveTravel { get; private set; }
        public NeonWorldMapScreen.TransitionPhase Phase { get; private set; }
        public bool DraggedThisGesture { get; private set; }
        public float ScrollOffset => scroll;
        public float ScrollLimit => Mathf.Max(0f, (Art.ContentBounds.height * scale + VerticalPadding * 2f - Viewport.rect.height) * 0.5f);
        private readonly Dictionary<string, BlueprintLevelPiece> byId = new Dictionary<string, BlueprintLevelPiece>();
        private readonly HashSet<string> unlocked = new HashSet<string>();
        private bool selection, visible, initialized, preview;
        private float scroll, targetScroll, scale;
        private Vector2 lastSize, pressPoint, dragPoint;
        private Vector2 pointerOffset;
        private List<WorldMapModule> selectable;
        private bool framePair;
        private Rect transitionBounds;

        private void Awake()
        {
            if (Application.IsPlaying(gameObject)) SetVisible(ShowOnSceneStart);
        }

        private void OnEnable()
        {
            if (Application.IsPlaying(gameObject)) return;
            SetVisible(PreviewInEditor);
            preview = PreviewInEditor;
        }

        public List<WorldMapModule> AvailableModules(WorldMapDefinition world)
        {
            var ids = new HashSet<string>();
            if (Art != null && Art.Pieces != null)
                foreach (var piece in Art.Pieces) ids.Add(piece.ModuleId);
            return world.OrderedModules().FindAll(module => ids.Contains(module.Id));
        }

        public void Initialize(bool asSelection)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            ClearTravel();
            selection = asSelection;
            framePair = false;
            initialized = true;
            Phase = NeonWorldMapScreen.TransitionPhase.None;
            byId.Clear(); unlocked.Clear();
            selectable = AvailableModules(Source);
            foreach (var module in selectable)
                if (WorldMapProgressStore.IsUnlocked(Source, module)) unlocked.Add(module.Id);
            foreach (var piece in Pieces)
            {
                piece.Initialize(Art);
                byId.Add(piece.ModuleId, piece);
                piece.gameObject.SetActive(!selection || unlocked.Contains(piece.ModuleId));
            }
            pointerOffset = Vector2.zero;
            DraggedThisGesture = false;
            SetVisible(true);
            UnityEngine.Canvas.ForceUpdateCanvases();
            FitBoard();
            targetScroll = scroll = -ScrollLimit;
            ApplyLayout();
        }

        private void SetVisible(bool value)
        {
            visible = value;
            if (UICamera != null) UICamera.enabled = value;
            if (Canvas != null) Canvas.enabled = value;
            if (BloomVolume != null) BloomVolume.enabled = value;
        }

        public void HidePresentation()
        {
            ClearTravel();
            foreach (var piece in Pieces) { piece.Hover(false); piece.SetPressed(false); }
            SetVisible(false);
            Phase = NeonWorldMapScreen.TransitionPhase.None;
        }

        private void FitBoard()
        {
            // Viewport.rect already contains the new dimensions on a resize. Use
            // the previous height to preserve the old scroll position correctly.
            float previousLimit = Mathf.Max(0f, (Art.ContentBounds.height * scale + VerticalPadding * 2f - lastSize.y) * 0.5f);
            float normalized = previousLimit > 0f ? targetScroll / previousLimit : -1f;
            scale = Viewport.rect.width * WidthFill / Mathf.Max(1f, Art.ContentBounds.width);
            if (!selection && framePair)
                scale = Mathf.Min(scale, Viewport.rect.height * 0.84f / Mathf.Max(1f, transitionBounds.height));
            Board.localScale = Vector3.one * scale;
            targetScroll = scroll = Mathf.Clamp(normalized, -1f, 1f) * ScrollLimit;
            if (!selection && framePair)
                targetScroll = scroll = Mathf.Clamp((Art.ContentBounds.center.y - transitionBounds.center.y) * scale, -ScrollLimit, ScrollLimit);
            lastSize = Viewport.rect.size;
            // Cover the wide screen without stretching the portrait blueprint texture.
            Vector2 size = Background.rect.size;
            float textureAspect = (float)Art.Background.width / Art.Background.height;
            float viewAspect = size.x / Mathf.Max(1f, size.y);
            Vector2 uv = viewAspect > textureAspect ? new Vector2(1f, textureAspect / viewAspect)
                : new Vector2(viewAspect / textureAspect, 1f);
            BackgroundImage.uvRect = new Rect((Vector2.one - uv) * 0.5f, uv);
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            Board.anchoredPosition = -Art.ContentBounds.center * scale + Vector2.up * scroll;
            float fraction = ScrollLimit > 0f ? scroll / ScrollLimit : 0f;
            Background.anchoredPosition = Vector2.up * (fraction * BackgroundParallax) + pointerOffset;
        }

        public void SetSelected(WorldMapModule module, bool focus = false)
        {
            if (focus && module != null && byId.TryGetValue(module.Id, out var piece)) Focus(piece.Art.TravelPoint);
        }

        private void Focus(Vector2 point)
        {
            targetScroll = Mathf.Clamp((Art.ContentBounds.center.y - point.y) * scale, -ScrollLimit, ScrollLimit);
        }

        public bool CanSelect(WorldMapModule module) => selection && module != null
            && byId.ContainsKey(module.Id) && unlocked.Contains(module.Id);

        public bool TryPick(Vector2 point, out WorldMapModule module)
        {
            module = null;
            if (!selection || !visible || !RectTransformUtility.RectangleContainsScreenPoint(Viewport, point, UICamera)) return false;
            if (selectable == null) return false;
            foreach (var candidate in selectable)
                if (unlocked.Contains(candidate.Id) && byId.TryGetValue(candidate.Id, out var piece) && piece.Contains(point, UICamera))
                { module = candidate; return true; }
            return false;
        }

        public void SetHovered(WorldMapModule module)
        {
            foreach (var piece in Pieces) piece.Hover(module != null && piece.ModuleId == module.Id);
        }

        public void SetPressed(WorldMapModule module)
        {
            foreach (var piece in Pieces) piece.SetPressed(module != null && piece.ModuleId == module.Id);
        }

        public Vector2 LabelScreenPosition(WorldMapModule module) => byId.TryGetValue(module.Id, out var piece)
            ? RectTransformUtility.WorldToScreenPoint(UICamera, piece.TravelAnchor.position) : Vector2.zero;

        public IEnumerator Confirm(WorldMapModule module)
        {
            if (module != null && byId.TryGetValue(module.Id, out var piece)) yield return piece.Confirm();
        }

        // Called before hit testing by the menu. Left-drag and click share a gesture,
        // so releasing a drag can never enter a level accidentally.
        public void HandleNavigation()
        {
            if (!selection || !visible) return;
            Vector2 pointer = Input.mousePosition;
            bool inside = RectTransformUtility.RectangleContainsScreenPoint(Viewport, pointer, UICamera);
            if (inside) ScrollBy(-Input.mouseScrollDelta.y * ScrollSpeed);
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(2))
            {
                pressPoint = dragPoint = pointer;
                DraggedThisGesture = false;
                dragging = inside;
            }
            if (dragging && (Input.GetMouseButton(0) || Input.GetMouseButton(2)))
            {
                if ((pointer - pressPoint).sqrMagnitude > 64f) DraggedThisGesture = true;
                if (DraggedThisGesture && RectTransformUtility.ScreenPointToLocalPointInRectangle(Viewport, pointer, UICamera, out var now)
                    && RectTransformUtility.ScreenPointToLocalPointInRectangle(Viewport, dragPoint, UICamera, out var before))
                    ScrollBy(now.y - before.y);
                dragPoint = pointer;
            }
            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(2)) dragging = false;
            float vertical = (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S) ? 1f : 0f)
                - (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W) ? 1f : 0f);
            ScrollBy(vertical * ScrollSpeed * 4f * Time.unscaledDeltaTime);
        }
        private bool dragging;

        public void ScrollBy(float delta) => targetScroll = Mathf.Clamp(targetScroll + delta, -ScrollLimit, ScrollLimit);

        public IEnumerator Play(HashSet<string> passed, WorldMapModule incoming, float hold, WorldMapModule departing)
        {
            ClearTravel();
            Phase = NeonWorldMapScreen.TransitionPhase.Preparing;
            BlueprintLevelPiece arriving = null, origin = null;
            bool revealIncoming = incoming != null && !unlocked.Contains(incoming.Id);
            foreach (var piece in Pieces)
            {
                bool current = incoming != null && piece.ModuleId == incoming.Id;
                bool finished = departing != null && piece.ModuleId == departing.Id;
                piece.gameObject.SetActive(current || finished || unlocked.Contains(piece.ModuleId) || passed.Contains(piece.ModuleId));
                piece.Hover(false);
                piece.SetOpacity(current && revealIncoming ? 0f : 1f);
                piece.SetEmphasis(0f);
                if (current) arriving = piece;
                if (finished) origin = piece;
            }
            // The transition frames the two pieces independently of the menu's scroll.
            if (arriving != null || origin != null)
            {
                Vector2 center = arriving != null ? arriving.Art.TravelPoint : origin.Art.TravelPoint;
                if (arriving != null && origin != null)
                {
                    Rect bounds = Union(arriving.Art.GlowBounds, origin.Art.GlowBounds);
                    transitionBounds = bounds;
                    framePair = true;
                    center = bounds.center;
                    float pairScale = Viewport.rect.height * 0.84f / Mathf.Max(1f, bounds.height);
                    scale = Mathf.Min(scale, pairScale);
                    Board.localScale = Vector3.one * scale;
                }
                Focus(center);
                scroll = targetScroll;
                ApplyLayout();
            }
            UnityEngine.Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;
            Phase = NeonWorldMapScreen.TransitionPhase.CoverReveal;
            yield return ScreenFadeOverlay.Instance.FadeTo(0f, 0.35f);
            Phase = NeonWorldMapScreen.TransitionPhase.Delay;
            if (arriving != null) yield return Wait(RevealDelay);
            Coroutine travel = null;
            try
            {
                if (origin != null && arriving != null && origin != arriving)
                {
                    ActiveTravel = WorldMapTravelEffect.Create(Board, origin.TravelAnchor, arriving.TravelAnchor,
                        OrbSize, OrbTravelDuration, OrbPauseDuration);
                    bool flightStarted = false;
                    travel = StartCoroutine(ActiveTravel.Play(phase =>
                    {
                        Phase = phase;
                        if (phase == NeonWorldMapScreen.TransitionPhase.OrbTravelling) flightStarted = true;
                    }));
                    while (!flightStarted) yield return null;
                    if (revealIncoming) yield return RevealIncoming(arriving);
                    yield return travel;
                }
                else if (arriving != null && revealIncoming)
                {
                    Phase = NeonWorldMapScreen.TransitionPhase.Drawing;
                    yield return RevealIncoming(arriving);
                }
                Phase = NeonWorldMapScreen.TransitionPhase.Holding;
                yield return Wait(hold);
            }
            finally
            {
                if (travel != null) StopCoroutine(travel);
                ClearTravel();
            }
        }

        private IEnumerator RevealIncoming(BlueprintLevelPiece arriving)
        {
            float elapsed = 0f;
            while (elapsed < RevealDuration)
            {
                elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
                arriving.SetOpacity(Mathf.SmoothStep(0f, 1f, elapsed / RevealDuration));
                yield return null;
            }
            arriving.SetOpacity(1f);
        }

        private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
            Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        private static IEnumerator Wait(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration) { elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f); yield return null; }
        }

        public void ShowCompletion() => Phase = NeonWorldMapScreen.TransitionPhase.Complete;
        private void ClearTravel()
        {
            if (ActiveTravel == null) return;
            ActiveTravel.Cancel();
            ActiveTravel = null;
        }
        private void OnDisable() { dragging = false; ClearTravel(); }

        private void Update()
        {
            if (Application.IsPlaying(gameObject) && visible && Phase == NeonWorldMapScreen.TransitionPhase.Complete
                && Input.GetKeyDown(KeyCode.Return)) WorldMapProgression.Current?.GoToSequence(1);
        }

        private void LateUpdate()
        {
            if (Art == null || Board == null || Viewport == null || Background == null) return;
            if (!Application.IsPlaying(gameObject))
            {
                if (preview != PreviewInEditor) { SetVisible(PreviewInEditor); preview = PreviewInEditor; }
                if (lastSize != Viewport.rect.size) { FitBoard(); scroll = targetScroll = -ScrollLimit; ApplyLayout(); }
                return;
            }
            if (!initialized || !visible) return;
            if (lastSize != Viewport.rect.size) FitBoard();
            if (!selection || PauseMenu.BlocksInput || WorldMapSceneRouter.IsLoading) return;
            float t = 1f - Mathf.Exp(-ScrollSmoothing * Time.unscaledDeltaTime);
            scroll = Mathf.Lerp(scroll, targetScroll, t);
            Vector2 pointer = Input.mousePosition;
            Vector2 target = Application.isFocused ? new Vector2(pointer.x / Mathf.Max(1f, Screen.width) - 0.5f,
                pointer.y / Mathf.Max(1f, Screen.height) - 0.5f) * PointerParallax * 2f : Vector2.zero;
            pointerOffset = Vector2.Lerp(pointerOffset, target, t);
            ApplyLayout();
        }
    }
}
