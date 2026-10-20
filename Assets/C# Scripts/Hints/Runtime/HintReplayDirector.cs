using UnityEngine;

namespace TapTap.Hints
{
    [DisallowMultipleComponent, RequireComponent(typeof(HintReplayPlayer))]
    public sealed class HintReplayDirector : MonoBehaviour
    {
        public HintReplayCatalog Catalog;
        public PlayerController Player;
        public bool UseKeyboard = true;
        public KeyCode HintKey = KeyCode.H;
        public HintReplayClip CurrentClip { get { Refresh(); return selectedClip; } }
        public Transform CurrentReferenceFrame { get { Refresh(); return selectedOrigin; } }
        public bool IsReturning => GameStageProgression.HasProgressedIn(gameObject.scene);

        private HintReplayPlayer playback;
        private HintReplayClip selectedClip, returnClip;
        private Transform selectedOrigin, returnOrigin;
        private HintReplayReturnTrigger returnOwner;
        private LevelRegion lastRegion;
        private string lastModule;
        private GameStage lastStage;
        private bool initialized;

        private void Awake() => playback = GetComponent<HintReplayPlayer>();
        private void OnEnable()
        {
            EventManager.Subscribe<GameStageChanged>(StageChanged);
            initialized = false;
            Refresh();
        }
        private void OnDisable()
        {
            EventManager.Unsubscribe<GameStageChanged>(StageChanged);
            playback?.Stop();
            ClearReturnSelection();
        }
        private void StageChanged(GameStageChanged message)
        {
            if (message.Scene != gameObject.scene) return;
            playback?.Stop();
            if (message.Previous == GameStage.Outbound) ClearReturnSelection();
            Refresh();
        }

        private void Update()
        {
            Refresh();
            if (RewindManager.Rewinding || HintReplayContext.Transitioning(gameObject.scene)) playback?.Stop();
            if (UseKeyboard && Input.GetKeyDown(HintKey)) RequestHint();
        }

        // Public, parameterless entry points can be wired directly to UI buttons/UnityEvents.
        public void PlayHint() => RequestHint();
        public void StopHint() => playback?.Stop();

        public bool CanRequestHint
        {
            get
            {
                Refresh();
                if (!isActiveAndEnabled || selectedClip == null || Player == null || Player.gameObject.scene != gameObject.scene
                    || Player.Body == null || Player.Head == null
                    || HintReplayContext.Paused || HintReplayContext.Transitioning(gameObject.scene) || RewindManager.Rewinding) return false;
                HintReplayRecorder recorder = HintReplayContext.Find<HintReplayRecorder>(gameObject.scene);
                return recorder == null || !recorder.IsRecording;
            }
        }

        public bool RequestHint()
        {
            if (!CanRequestHint) return false;
            if (playback == null) playback = GetComponent<HintReplayPlayer>();
            return playback.TryPlay(selectedClip, selectedOrigin, Player.Body.Visual, Player.Head.Visual);
        }

        public bool SelectReturnHint(HintReplayReturnTrigger trigger)
        {
            Refresh();
            if (!isActiveAndEnabled || !IsReturning || trigger == null || trigger.gameObject.scene != gameObject.scene
                || trigger.Clip == null || !trigger.Clip.Validate(out _)) return false;
            Transform origin = trigger.Clip.UsesWorldSpace ? null : trigger.PlaybackOrigin != null ? trigger.PlaybackOrigin : trigger.transform;
            if (returnOwner != trigger || returnClip != trigger.Clip || returnOrigin != origin) playback?.Stop();
            returnOwner = trigger; returnClip = trigger.Clip; returnOrigin = origin;
            Refresh();
            return true;
        }

        private void Refresh()
        {
            if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded) return;
            if (Player == null) Player = HintReplayContext.Find<PlayerController>(gameObject.scene);
            GameStage stage = GameStageProgression.StageIn(gameObject.scene);
            bool returning = stage >= GameStage.Returning;
            WorldMapProgression map = HintReplayContext.Progression(gameObject.scene);
            LevelRegion region = HintReplayContext.Region(gameObject.scene, Player);
            string module = map != null ? map.CurrentModule?.Id : region != null ? region.WorldModuleId : null;
            if (initialized && (stage != lastStage || !returning && (region != lastRegion || module != lastModule)))
            {
                playback?.Stop();
                if (lastStage == GameStage.Outbound && returning) ClearReturnSelection();
            }
            if (returnOwner == null || returnClip == null || !returnClip.UsesWorldSpace && returnOrigin == null) ClearReturnSelection();
            HintReplayClip next = returning ? returnClip : Catalog != null && region != null ? Catalog.Resolve(region.Source, module) : null;
            Transform nextOrigin = next != null && next.UsesWorldSpace ? null : returning ? returnOrigin : region != null ? region.transform : null;
            if (stage == GameStage.DoorOpened) next = null;
            if (next != selectedClip || nextOrigin != selectedOrigin) playback?.Stop();
            selectedClip = next; selectedOrigin = nextOrigin;
            lastStage = stage; lastRegion = region; lastModule = module; initialized = true;
        }

        private void ClearReturnSelection() { returnOwner = null; returnClip = null; returnOrigin = null; }
    }
}
