using System.Collections;
using DG.Tweening;
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
        public Vector3 OpenLocalOffset = Vector3.up;
        [Min(0.01f)] public float KeyTravelDuration = 0.55f;
        [Min(0.01f)] public float OpenDuration = 0.45f;
        public PlayerController Player;
        public UnityEvent OnOpened = new UnityEvent();
        private BoxCollider2D barrier;
        private Coroutine opening;
        private Tween doorTween;
        private ProgressKey key;
        private Vector3 closedPosition;
        public bool IsOpen { get; private set; }

        private void Awake()
        {
            barrier = GetComponent<BoxCollider2D>();
            if (View != null) closedPosition = View.localPosition;
            if (Proximity != null) Proximity.isTrigger = true;
        }
        private void OnEnable()
        {
            if (GameStageProgression.StageIn(gameObject.scene) == GameStage.DoorOpened) SetOpen();
        }
        private void FixedUpdate()
        {
            if (opening != null || IsOpen) return;
            if (Player == null) Player = ReturnJourneyContact.FindPlayer(gameObject.scene);
            if (ReturnJourneyContact.CanTrigger(Player) && ReturnJourneyContact.TouchesBody(Proximity, Player)) TryOpen();
        }
        public bool TryOpen()
        {
            GameStageProgression stage = GameStageProgression.ForScene(gameObject.scene);
            if (opening != null || IsOpen || !stage.HasKey || stage.CarriedKey == null
                || !ReturnJourneyContact.CanTrigger(Player)) return false;
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
            Vector3 from = key.CarryVisual.position;
            float elapsed = 0f;
            while (elapsed < KeyTravelDuration)
            {
                elapsed += Time.deltaTime;
                Vector3 target = KeySocket != null ? KeySocket.position : transform.position;
                key.CarryVisual.position = Vector3.Lerp(from, target, Mathf.SmoothStep(0f, 1f, elapsed / KeyTravelDuration));
                yield return null;
            }
            key.PlayUnlockBurst(KeySocket != null ? KeySocket.position : transform.position);
            stage.CompleteDoor();
            // Collision opens immediately; only the view slides away.
            IsOpen = true;
            barrier.enabled = false;
            if (View != null)
            {
                doorTween = View.DOLocalMove(closedPosition + OpenLocalOffset, OpenDuration).SetEase(Ease.InOutSine);
                yield return doorTween.WaitForCompletion();
                doorTween = null;
            }
            RewindManager.Current?.ClearHistory();
            Player.GetComponent<PlayerInput>()?.ResetForTransition();
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
            if (View != null) View.localPosition = closedPosition + OpenLocalOffset;
        }
        private void OnDisable()
        {
            if (opening != null) StopCoroutine(opening);
            opening = null;
            doorTween?.Kill(); doorTween = null;
            if (key != null) key.ResumeCarry();
            key = null;
            GameplayActionLocks.Release(this);
            if (IsOpen) SetOpen();
        }
    }
}
