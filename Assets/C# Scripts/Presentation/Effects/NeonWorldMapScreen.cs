using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TapTap
{
    // Shared prefab presentation for the selection screen and the assembly transition.
    [ExecuteAlways]
    public sealed class NeonWorldMapScreen : MonoBehaviour
    {
        public enum TransitionPhase { None, Preparing, CoverReveal, Delay, Drawing, Holding, Complete,
            OrbAppearing, OrbTravelling, OrbDisappearing, TrailFading, OrbHolding, OrbArrived }
        public const string ResourcePath = "WorldMapSelection/WorldCharacterSelection";
        public WorldMapDefinition Source;
        public Camera UICamera;
        public Canvas Canvas;
        public CanvasGroup FadeGroup;
        public Behaviour BloomVolume;
        public bool ShowOnSceneStart = true;
        public bool PreviewInEditor = true;
        public RectTransform MapHost, Board;
        public Vector2 MapSize = new Vector2(86f, 127f);
        public NeonLevelPiece[] Pieces;
        [Min(0f)] public float AnimationDelay = 0.35f;
        [Min(0.1f)] public float MinimumDrawDuration = 1.4f;
        [Min(0.1f)] public float OrbTravelDuration = 0.5f;
        [Min(0f)] public float OrbPauseDuration = 0.6f;
        [Min(0.1f)] public float OrbSize = 3f;
        public WorldMapTravelEffect ActiveTravel { get; private set; }
        public TransitionPhase Phase { get; private set; }
        public float DrawingElapsed { get; private set; }
        private WorldMapModule selected;
        private List<WorldMapModule> selectable;
        private bool selection;
        private readonly HashSet<string> passed = new HashSet<string>();
        private readonly HashSet<string> unlocked = new HashSet<string>();
        private readonly Dictionary<string, NeonLevelPiece> byId = new Dictionary<string, NeonLevelPiece>();
        private Vector2 lastHostSize;
        private bool lastPreview;
        private static GameObject cachedPrefab;

        private void Awake()
        {
            if (Application.IsPlaying(gameObject)) SetPresentationVisible(ShowOnSceneStart);
        }

        private void OnEnable()
        {
            if (!Application.IsPlaying(gameObject)) RefreshAuthoringPreview();
        }

        private void SetPresentationVisible(bool value)
        {
            if (UICamera!=null) UICamera.enabled=value;
            if (Canvas!=null) Canvas.enabled=value;
            if (BloomVolume!=null) BloomVolume.enabled=value;
        }

        private void RefreshAuthoringPreview()
        {
            SetPresentationVisible(PreviewInEditor);
            lastPreview=PreviewInEditor;
        }

        public List<WorldMapModule> AvailableModules(WorldMapDefinition world)
        {
            var ids=new HashSet<string>();
            foreach (NeonLevelPiece piece in Pieces) ids.Add(piece.ModuleId);
            return world.OrderedModules().FindAll(module=>ids.Contains(module.Id));
        }

        public void HidePresentation()
        {
            ClearTravel();
            SetPresentationVisible(false);
            Phase=TransitionPhase.None;
        }

        public static GameObject PrefabFor(WorldMapDefinition world)
        {
            if (cachedPrefab == null) cachedPrefab = Resources.Load<GameObject>(ResourcePath);
            return cachedPrefab != null && cachedPrefab.GetComponent<NeonWorldMapScreen>().Source == world ? cachedPrefab : null;
        }

        public static List<WorldMapModule> SelectableModules(WorldMapDefinition world)
        {
            List<WorldMapModule> modules = world.OrderedModules();
            GameObject prefab = PrefabFor(world);
            if (prefab == null) return modules;
            var ids = new HashSet<string>();
            foreach (NeonLevelPiece piece in prefab.GetComponent<NeonWorldMapScreen>().Pieces) ids.Add(piece.ModuleId);
            return modules.FindAll(module => ids.Contains(module.Id));
        }

        public void Initialize(bool asSelection)
        {
            // An authored screen may be disabled to keep the gameplay scene unobstructed.
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            ClearTravel();
            selection = asSelection;
            Phase=TransitionPhase.None; DrawingElapsed=0f;
            byId.Clear(); passed.Clear(); unlocked.Clear();
            passed.UnionWith(WorldMapProgressStore.Completed(Source));
            foreach (NeonLevelPiece piece in Pieces) byId.Add(piece.ModuleId, piece);
            selectable = AvailableModules(Source);
            foreach (WorldMapModule module in selectable)
                if (WorldMapProgressStore.IsUnlocked(Source, module)) unlocked.Add(module.Id);
            FadeGroup.alpha=1f;
            foreach (NeonLevelPiece piece in Pieces)
            {
                piece.SetReveal(1f);
                piece.SetEmphasis(0f);
                piece.SetIntensity(0.50f);
                piece.gameObject.SetActive(!selection || unlocked.Contains(piece.ModuleId));
            }
            SetPresentationVisible(true);
            UnityEngine.Canvas.ForceUpdateCanvases();
            FitBoard();
        }

        private void FitBoard()
        {
            Vector2 size = MapHost.rect.size;
            float scale = Mathf.Min(size.x / MapSize.x, size.y / MapSize.y) * 0.96f;
            Board.localScale = Vector3.one * scale;
            Board.anchoredPosition = -MapSize * (scale * 0.5f);
            lastHostSize = size;
        }

        public void SetSelected(WorldMapModule module, string notice = null)
        {
            selected = module;
        }

        public bool CanSelect(WorldMapModule module) => selection && module!=null
            && byId.ContainsKey(module.Id) && unlocked.Contains(module.Id);

        public bool TryPick(Vector2 point, out WorldMapModule module)
        {
            module = null;
            if (!selection) return false;
            foreach (WorldMapModule item in selectable)
                if (byId.TryGetValue(item.Id, out NeonLevelPiece piece) && piece.Contains(point, UICamera))
                { module = item; return true; }
            return false;
        }

        public Vector2 LabelScreenPosition(WorldMapModule module) => byId[module.Id].NumberScreenPosition(UICamera);

        public IEnumerator Play(HashSet<string> completed, WorldMapModule incoming, float duration, float hold,
            WorldMapModule departing = null)
        {
            ClearTravel();
            passed.Clear(); passed.UnionWith(completed);
            if (departing != null) passed.Add(departing.Id);
            Phase=TransitionPhase.Preparing; DrawingElapsed=0f;
            NeonLevelPiece arriving = null;
            bool revealIncoming = incoming != null && !unlocked.Contains(incoming.Id);
            foreach (NeonLevelPiece piece in Pieces)
            {
                bool current = incoming != null && piece.ModuleId == incoming.Id;
                piece.gameObject.SetActive(current || unlocked.Contains(piece.ModuleId) || passed.Contains(piece.ModuleId));
                piece.SetEmphasis(current?1f:0f);
                piece.SetIntensity(current?0.94f:0.40f); piece.SetReveal(1f);
                if (current) arriving = piece;
            }
            // Render fonts, HDR buffers and Bloom behind the opaque persistent cover.
            UnityEngine.Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;
            if (arriving != null && revealIncoming) { arriving.SetIntensity(0f); arriving.SetReveal(0f); }
            Phase=TransitionPhase.CoverReveal;
            yield return ScreenFadeOverlay.Instance.FadeTo(0f, 0.35f);
            yield return null;
            yield return null;
            Phase=TransitionPhase.Delay;
            float elapsed = 0f;
            while (arriving != null && elapsed<AnimationDelay)
            {
                elapsed+=Mathf.Min(Time.unscaledDeltaTime,1f/30f);
                yield return null;
            }
            if (arriving != null && departing != null && departing != incoming
                && byId.TryGetValue(departing.Id, out NeonLevelPiece origin))
            {
                ActiveTravel = WorldMapTravelEffect.Create(Board, origin.Number.transform, arriving.Number.transform,
                    OrbSize, OrbTravelDuration, OrbPauseDuration);
                Coroutine travel = null;
                try
                {
                    bool flightStarted = false;
                    travel = StartCoroutine(ActiveTravel.Play(stage =>
                    {
                        Phase = stage;
                        if (stage == TransitionPhase.OrbTravelling) flightStarted = true;
                    }));
                    // Newly unlocked icons appear as the orb starts flying; existing icons stay visible.
                    while (!flightStarted) yield return null;
                    if (revealIncoming) yield return DrawIncoming(arriving, duration);
                    yield return travel;
                }
                finally
                {
                    if (travel != null) StopCoroutine(travel);
                    ClearTravel();
                }
            }
            else
            {
                Phase=TransitionPhase.Drawing;
                if (revealIncoming) yield return DrawIncoming(arriving, duration);
            }
            Phase=TransitionPhase.Holding;
            elapsed = 0f;
            float visibleHold=arriving!=null?Mathf.Max(0.65f,hold):hold;
            while (elapsed<visibleHold)
            {
                elapsed+=Mathf.Min(Time.unscaledDeltaTime,1f/30f);
                if (arriving!=null)
                {
                    float settle=Mathf.SmoothStep(0f,1f,elapsed/0.4f);
                    arriving.SetIntensity(Mathf.Lerp(0.94f,0.50f,settle));
                    arriving.SetEmphasis(1f-settle);
                }
                yield return null;
            }
        }

        private IEnumerator DrawIncoming(NeonLevelPiece arriving, float duration)
        {
            DrawingElapsed=0f;
            float drawDuration=Mathf.Max(MinimumDrawDuration,duration);
            if (arriving!=null) arriving.SetIntensity(0.94f);
            while (arriving!=null && DrawingElapsed<drawDuration)
            {
                // A slow loading/render frame must not consume the visible animation.
                DrawingElapsed+=Mathf.Min(Time.unscaledDeltaTime,1f/30f);
                arriving.SetReveal(Mathf.SmoothStep(0f,1f,DrawingElapsed/drawDuration));
                yield return null;
            }
            if (arriving != null) { arriving.SetIntensity(0.94f); arriving.SetReveal(1f); }
        }

        public void ShowCompletion() => Phase=TransitionPhase.Complete;

        private void ClearTravel()
        {
            if (ActiveTravel == null) return;
            ActiveTravel.Cancel();
            ActiveTravel = null;
        }

        private void OnDisable() => ClearTravel();

        private void Update()
        {
            if (!Application.IsPlaying(gameObject)) return;
            if (Phase==TransitionPhase.Complete && Input.GetKeyDown(KeyCode.Return))
                WorldMapProgression.Current?.GoToSequence(1);
        }

        private void LateUpdate()
        {
            if (MapHost==null || Board==null) return;
            if (lastHostSize != MapHost.rect.size) FitBoard();
            if (!Application.IsPlaying(gameObject))
            {
                if (lastPreview!=PreviewInEditor) RefreshAuthoringPreview();
                return;
            }
            if (!selection) return;
            foreach (NeonLevelPiece piece in Pieces)
            {
                bool current = selected != null && piece.ModuleId == selected.Id;
                float focus=Mathf.MoveTowards(piece.Emphasis,current?1f:0f,Mathf.Min(Time.unscaledDeltaTime,0.05f)*5f);
                piece.SetEmphasis(focus);
                float idle=passed.Contains(piece.ModuleId)?0.40f:0.50f;
                float breath=0.91f+0.03f*Mathf.Sin(Time.unscaledTime*2f);
                piece.SetIntensity(Mathf.Lerp(idle,breath,focus));
            }
        }
    }
}
