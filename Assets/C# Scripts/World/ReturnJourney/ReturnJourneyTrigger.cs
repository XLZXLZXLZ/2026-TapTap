using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace TapTap
{
    public enum ReturnJourneyCue { Warning, BeginReturn }

    [RequireComponent(typeof(BoxCollider2D)), DefaultExecutionOrder(800)]
    public sealed class ReturnJourneyTrigger : MonoBehaviour
    {
        public ReturnJourneyCue Cue;
        [Tooltip("同场景中唯一的演出编号；警告触发一次后不因 Z 重播。")]
        public string CueId = "warning-1";
        public ReturnJourneyPresentation Presentation;
        public ReturnJourneyPlatform Platform;
        public PlayerController Player;
        [Header("平台塌落震动")]
        [Min(0f)] public float CollapseShakeStrength = 0.3f;
        [Min(0.01f)] public float CollapseShakeDuration = 0.35f;
        [Min(1f)] public float CollapseShakeViewportMultiplier = 3f;
        public UnityEvent OnTriggered = new UnityEvent();
        private BoxCollider2D area;
        private bool fired;
        private Coroutine sequence;
        public bool IsPlaying { get; private set; }
        public bool HasTriggered => fired;

        private void Awake() { area = GetComponent<BoxCollider2D>(); area.isTrigger = true; }
        private void FixedUpdate()
        {
            if (fired || GameStageProgression.HasProgressedIn(gameObject.scene)) return;
            if (Player == null) Player = ReturnJourneyContact.FindPlayer(gameObject.scene);
            if (ReturnJourneyContact.CanTrigger(Player) && ReturnJourneyContact.TouchesBody(area, Player)) Trigger();
        }
        public bool Trigger()
        {
            if (!isActiveAndEnabled || fired || GameStageProgression.HasProgressedIn(gameObject.scene)
                || WorldMapProgression.InputBlocked || RewindManager.Rewinding || Time.timeScale <= 0f
                || GameplayActionLocks.IsLocked(GameplayAction.PlayerControls, gameObject.scene)) return false;
            if (Presentation == null) Presentation = ReturnJourneyPresentation.InScene(gameObject.scene);
            if (Player == null) Player = ReturnJourneyContact.FindPlayer(gameObject.scene);
            GameStageProgression stage = GameStageProgression.ForScene(gameObject.scene);
            if (Cue == ReturnJourneyCue.BeginReturn)
            {
                if (!stage.BeginReturn()) return false;
            }
            else
            {
                if (!stage.TryMarkCue(CueId)) { fired = true; return false; }
            }
            fired = true;
            IsPlaying = true;
            GameplayActionLocks.Acquire(this, GameplayAction.PlayerControls | GameplayAction.Rewind | GameplayAction.DebugCamera);
            RewindManager.Current?.EndRewind();
            if (Player != null)
            {
                Player.GetComponent<PlayerInput>()?.ResetForTransition();
                if (Player.Body != null) Player.Body.Velocity = new Vector2(0f, Player.Body.Velocity.y);
            }
            if (Cue == ReturnJourneyCue.Warning && Presentation != null && Presentation.isActiveAndEnabled) Presentation.PlayWarning();
            sequence = StartCoroutine(PlaySequence());
            OnTriggered.Invoke();
            return true;
        }
        private IEnumerator PlaySequence()
        {
            yield return null;
            while (Presentation != null && Presentation.isActiveAndEnabled
                && (Cue == ReturnJourneyCue.Warning ? Presentation.IsWarningPlaying : Presentation.IsEntryPlaying)) yield return null;
            if (Cue == ReturnJourneyCue.BeginReturn)
            {
                Platform?.Collapse();
                EffectManager.Instance.ShakeCamera(CollapseShakeStrength, CollapseShakeDuration, CollapseShakeViewportMultiplier);
                float elapsed = 0f;
                while (elapsed < CollapseShakeDuration || Platform != null && Platform.IsDropping)
                {
                    if (Time.timeScale > 0f && !PauseMenu.IsOpen) elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            ReleaseControls();
            sequence = null;
        }
        private void ReleaseControls()
        {
            GameplayActionLocks.Release(this);
            IsPlaying = false;
            if (Player != null) Player.GetComponent<PlayerInput>()?.ResetForTransition();
        }
        private void OnDisable()
        {
            if (sequence != null) StopCoroutine(sequence);
            sequence = null;
            if (IsPlaying) ReleaseControls();
        }
        private void OnDrawGizmos()
        {
            BoxCollider2D box = GetComponent<BoxCollider2D>();
            if (box == null) return;
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Cue == ReturnJourneyCue.BeginReturn ? new Color(1f, 0.5f, 0.2f, 0.4f) : new Color(0.3f, 0.8f, 1f, 0.3f);
            Gizmos.DrawWireCube(box.offset, box.size);
            Gizmos.matrix = previous;
        }
    }
}
