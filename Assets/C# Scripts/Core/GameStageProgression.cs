using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap
{
    public enum GameStage { Outbound, Returning, CarryingKey, DoorOpened }

    public readonly struct GameStageChanged
    {
        public readonly Scene Scene;
        public readonly GameStage Previous, Stage;
        public GameStageChanged(Scene scene, GameStage previous, GameStage stage)
        { Scene = scene; Previous = previous; Stage = stage; }
    }

    [DisallowMultipleComponent]
    public sealed class GameStageProgression : MonoBehaviour
    {
        private sealed class Session
        {
            public GameStage Stage;
            public readonly HashSet<string> Cues = new HashSet<string>();
        }
        // Progress survives scene reloads in this Play session only.
        private static readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>();
        private static GameStageProgression transitioning;
        private Sequence emphasis;
        private DemoCamera cameraRig;
        private PlayerController player;
        private ProgressKey pendingKey, carriedKey;
        public static bool InputBlocked => transitioning != null;
        public GameStage Stage => StageIn(gameObject.scene);
        public bool HasProgressed => Stage >= GameStage.Returning;
        public bool HasKey => Stage == GameStage.CarryingKey;
        public ProgressKey CarriedKey => carriedKey;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() { sessions.Clear(); transitioning = null; }
        private static string SceneKey(Scene scene) => string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
        private static Session State(Scene scene)
        {
            string key = SceneKey(scene);
            if (!sessions.TryGetValue(key, out Session state)) sessions.Add(key, state = new Session());
            return state;
        }
        public static GameStage StageIn(Scene scene) => State(scene).Stage;
        public static bool HasProgressedIn(Scene scene) => StageIn(scene) >= GameStage.Returning;
        public bool TryMarkCue(string id) => !string.IsNullOrEmpty(id) && State(gameObject.scene).Cues.Add(id);

        public static GameStageProgression ForScene(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                GameStageProgression found = root.GetComponentInChildren<GameStageProgression>(true);
                if (found != null) return found;
            }
            var owner = new GameObject("Game Stage Progression");
            SceneManager.MoveGameObjectToScene(owner, scene);
            return owner.AddComponent<GameStageProgression>();
        }
        private void Start() => ApplyStage();

        // Restoration does not re-publish a transition. Late listeners query StageIn on enable.
        public void ApplyStage()
        {
            if (HasProgressed && WorldMapProgression.Current != null
                && WorldMapProgression.Current.gameObject.scene == gameObject.scene)
                WorldMapProgression.Current.SetExitsEnabled(false);
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                foreach (IndicatorLight light in root.GetComponentsInChildren<IndicatorLight>(true)) light.ApplyStage(true);
                foreach (ProgressKey key in root.GetComponentsInChildren<ProgressKey>(true)) key.ApplyStage();
            }
        }
        public bool BeginReturn()
        {
            if (HasProgressed || InputBlocked || WorldMapProgression.InputBlocked || RewindManager.Rewinding) return false;
            CommitStage(GameStage.Returning);
            return true;
        }
        private void CommitStage(GameStage next)
        {
            Session state = State(gameObject.scene);
            if (next <= state.Stage) return;
            GameStage previous = state.Stage;
            state.Stage = next;
            RewindManager.Current?.ClearHistory();
            if (WorldMapProgression.Current != null && WorldMapProgression.Current.gameObject.scene == gameObject.scene)
                WorldMapProgression.Current.SetExitsEnabled(false);
            EventManager.Publish(new GameStageChanged(gameObject.scene, previous, next));
        }
        public bool BindCarriedKey(ProgressKey key)
        {
            if (!HasKey || key == null || carriedKey != null && carriedKey != key) return false;
            carriedKey = key;
            return true;
        }
        public bool Collect(ProgressKey key, PlayerController character)
        {
            if (key == null || character == null || key.gameObject.scene != gameObject.scene || Stage != GameStage.Returning
                || InputBlocked || WorldMapProgression.InputBlocked || RewindManager.Rewinding
                || Time.timeScale <= 0f || character.Body.IsReturning || character.Head.IsReturning
                || (SceneResetManager.Existing != null && SceneResetManager.Existing.IsResetting)) return false;
            pendingKey = key;
            player = character;
            transitioning = this;
            GameplayActionLocks.Acquire(this, GameplayAction.Input | GameplayAction.Rewind);
            key.BeginCarry(character);
            player.GetComponent<PlayerInput>()?.ResetForTransition();
            StartCoroutine(CollectSequence());
            return true;
        }
        private IEnumerator CollectSequence()
        {
            // Collection happens within a simulation step; let it finish before clearing history.
            yield return null;
            EffectManager effects = EffectManager.Instance;
            effects.ClearTransientEffects();
            RewindManager.Current?.ClearHistory();
            cameraRig = FindCamera();
            effects.BeginProgressEmphasis(pendingKey.HitStopDuration > 0f ? 0f : pendingKey.SlowMotionScale);
            float closeSize = 1f - pendingKey.ZoomAmount;
            emphasis = DOTween.Sequence().SetUpdate(true);
            emphasis.Append(DOVirtual.Float(1f, closeSize, pendingKey.ZoomInDuration,
                value => cameraRig?.SetProgressZoom(value)).SetEase(Ease.OutSine));
            emphasis.AppendInterval(pendingKey.ZoomHoldDuration);
            emphasis.Append(DOVirtual.Float(closeSize, 1f, pendingKey.ZoomOutDuration,
                value => cameraRig?.SetProgressZoom(value)).SetEase(Ease.InOutSine));
            emphasis.InsertCallback(pendingKey.HitStopDuration, () =>
            {
                effects.EndProgressEmphasis();
                effects.BeginProgressEmphasis(pendingKey.SlowMotionScale);
                pendingKey.PlayPickupBurst();
            });
            yield return emphasis.WaitForCompletion();
            emphasis = null;
            effects.EndProgressEmphasis();
            cameraRig?.SetProgressZoom(1f);
            carriedKey = pendingKey;
            CommitStage(GameStage.CarryingKey);
            player.GetComponent<PlayerInput>()?.ResetForTransition();
            pendingKey = null;
            transitioning = null;
            GameplayActionLocks.Release(this);
            // Open only after restoring the original game speed and releasing the pickup lock.
            PauseMenu menu = PauseMenu.Current;
            if (menu != null && menu.gameObject.scene == gameObject.scene && menu.Open())
                menu.GetComponent<ReturnJourneyPausePortrait>()?.Reveal();
        }
        public bool CompleteDoor()
        {
            if (!HasKey) return false;
            carriedKey?.Consume();
            carriedKey = null;
            CommitStage(GameStage.DoorOpened);
            return true;
        }
        private DemoCamera FindCamera()
        {
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                DemoCamera found = root.GetComponentInChildren<DemoCamera>();
                if (found != null) return found;
            }
            return null;
        }
        private void OnDisable()
        {
            if (transitioning != this) return;
            StopAllCoroutines();
            emphasis?.Kill();
            emphasis = null;
            cameraRig?.SetProgressZoom(1f);
            EffectManager.Existing?.EndProgressEmphasis();
            if (pendingKey != null) pendingKey.SetCollected(false);
            player?.GetComponent<PlayerInput>()?.ResetForTransition();
            pendingKey = null;
            transitioning = null;
            GameplayActionLocks.Release(this);
        }
    }
}
