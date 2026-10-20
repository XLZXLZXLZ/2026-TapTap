using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace TapTap
{
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider2D), typeof(WorldSurface)), DefaultExecutionOrder(800)]
    public sealed class ReturnJourneyDoor : MonoBehaviour
    {
        public BoxCollider2D Proximity;
        public Transform KeySocket;
        public Transform View;
        public GoldenDoorVisual DoorVisual;
        [Tooltip("钥匙模型中齿尖的局部坐标，用于准确对准锁孔。")]
        public Vector3 KeyTipLocalOffset = new Vector3(0.43f, 0.12f, 0f);
        [Min(0.01f)] public float KeyTravelDuration = 0.55f;
        [Min(0.01f)] public float OpenDuration = 0.9f;
        public PlayerController Player;
        public UnityEvent OnOpened = new UnityEvent();
        private BoxCollider2D barrier;
        private Coroutine opening;
        private ProgressKey key;
        public bool IsOpen { get; private set; }
        public bool IsOpening => opening != null;

        private void Awake()
        {
            barrier = GetComponent<BoxCollider2D>();
            if (DoorVisual == null && View != null) DoorVisual = View.GetComponent<GoldenDoorVisual>();
            if (Proximity != null) Proximity.isTrigger = true;
        }
        private void OnEnable()
        {
            if (GameStageProgression.StageIn(gameObject.scene) == GameStage.DoorOpened) SetOpen();
            else DoorVisual?.ResetClosed();
        }
        private void FixedUpdate()
        {
            // Keyless proximity must stay cheap: ForScene searches the scene hierarchy.
            if (opening != null || IsOpen
                || GameStageProgression.StageIn(gameObject.scene) != GameStage.CarryingKey) return;
            if (Player == null) Player = ReturnJourneyContact.FindPlayer(gameObject.scene);
            if (ReturnJourneyContact.CanTrigger(Player) && ReturnJourneyContact.TouchesBody(Proximity, Player)) TryOpen();
        }
        public bool TryOpen()
        {
            // Reject before resolving the scene service, including direct callers.
            if (opening != null || IsOpen
                || GameStageProgression.StageIn(gameObject.scene) != GameStage.CarryingKey
                || !ReturnJourneyContact.CanTrigger(Player)) return false;
            GameStageProgression stage = GameStageProgression.ForScene(gameObject.scene);
            if (!stage.HasKey || stage.CarriedKey == null) return false;
            key = stage.CarriedKey;
            if (key.CarryVisual == null) return false;
            GameplayActionLocks.Acquire(this, GameplayAction.Input | GameplayAction.Rewind);
            RewindManager.Current?.EndRewind();
            Player.GetComponent<PlayerInput>()?.ResetForTransition();
            key.BeginUnlock();
            opening = StartCoroutine(OpenSequence(stage));
            return true;
        }
        private IEnumerator OpenSequence(GameStageProgression stage)
        {
            Transform flyingKey = key.CarryVisual;
            Vector3 from = flyingKey.position;
            Vector3 originalScale = flyingKey.localScale;
            float elapsed = 0f;
            while (elapsed < KeyTravelDuration)
            {
                elapsed += Time.deltaTime;
                Vector3 target = KeySocket != null ? KeySocket.position : transform.position;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, KeyTravelDuration));
                float blend = Mathf.SmoothStep(0f, 1f, t);
                flyingKey.localScale = originalScale * Mathf.Lerp(1f, 0.25f, Mathf.SmoothStep(0f, 1f, t));
                Vector3 tipOffset = flyingKey.TransformVector(KeyTipLocalOffset);
                flyingKey.position = Vector3.Lerp(from, target - tipOffset, blend);
                yield return null;
            }
            key.PlayUnlockBurst(KeySocket != null ? KeySocket.position : transform.position);
            // Commit consumption before the dissolve so disabling/reloading cannot restore a spent key.
            if (!stage.CompleteDoor())
            {
                flyingKey.localScale = originalScale;
                key.ResumeCarry();
                GameplayActionLocks.Release(this);
                key = null;
                opening = null;
                yield break;
            }
            AudioManager.Instance?.Play(SoundId.DoorUnlock,
                KeySocket != null ? KeySocket.position : transform.position, owner: this);
            IsOpen = true;
            key = null;
            DoorVisual?.BeginDissolve();
            elapsed = 0f;
            while (elapsed < OpenDuration)
            {
                elapsed += Time.deltaTime;
                DoorVisual?.SetProgress(elapsed / Mathf.Max(0.01f, OpenDuration));
                yield return null;
            }
            SetOpen();
            RewindManager.Current?.ClearHistory();
            if (Player != null) Player.GetComponent<PlayerInput>()?.ResetForTransition();
            GameplayActionLocks.Release(this);
            opening = null;
            key = null;
            OnOpened.Invoke();
        }
        private void SetOpen()
        {
            IsOpen = true;
            if (barrier == null) barrier = GetComponent<BoxCollider2D>();
            barrier.enabled = false;
            if (DoorVisual != null) DoorVisual.SetOpen();
            else if (View != null) View.gameObject.SetActive(false);
        }
        private void OnDisable()
        {
            if (opening != null) StopCoroutine(opening);
            opening = null;
            if (key != null)
            {
                // A pre-insertion interruption keeps the carried key at its original scale.
                if (key.CarryVisual != null) key.CarryVisual.localScale = key.transform.lossyScale;
                key.ResumeCarry();
            }
            key = null;
            GameplayActionLocks.Release(this);
            if (IsOpen)
            {
                SetOpen();
                RewindManager.Current?.ClearHistory();
            }
            else DoorVisual?.ResetClosed();
        }
    }
}
