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
        public UnityEvent OnTriggered = new UnityEvent();
        private BoxCollider2D area;
        private bool fired;

        private void Awake() { area = GetComponent<BoxCollider2D>(); area.isTrigger = true; }
        private void FixedUpdate()
        {
            if (fired || GameStageProgression.HasProgressedIn(gameObject.scene)) return;
            if (Player == null) Player = ReturnJourneyContact.FindPlayer(gameObject.scene);
            if (ReturnJourneyContact.CanTrigger(Player) && ReturnJourneyContact.TouchesBody(area, Player)) Trigger();
        }
        public bool Trigger()
        {
            if (fired || GameStageProgression.HasProgressedIn(gameObject.scene)) return false;
            GameStageProgression stage = GameStageProgression.ForScene(gameObject.scene);
            if (Cue == ReturnJourneyCue.BeginReturn)
            {
                if (!stage.BeginReturn()) return false;
                Platform?.Collapse();
                EffectManager.Instance.ShakeCamera(0.1f);
            }
            else
            {
                if (!stage.TryMarkCue(CueId)) { fired = true; return false; }
                if (Presentation == null) Presentation = ReturnJourneyPresentation.InScene(gameObject.scene);
                Presentation?.PlayWarning();
            }
            fired = true;
            OnTriggered.Invoke();
            return true;
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
