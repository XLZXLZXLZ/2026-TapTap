using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap
{
    [DisallowMultipleComponent]
    public sealed class GameStageProgression : MonoBehaviour
    {
        // Session progress survives R/reloading this scene, but is cleared on the next Play session.
        private static readonly HashSet<string> progressedScenes = new HashSet<string>();
        private static GameStageProgression transitioning;
        private Sequence emphasis;
        private DemoCamera cameraRig;
        private PlayerController player;
        private ProgressKey pendingKey;
        public static bool InputBlocked => transitioning != null;
        public bool HasProgressed => HasProgressedIn(gameObject.scene);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() { progressedScenes.Clear(); transitioning = null; }

        private static string SceneKey(Scene scene) => string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
        public static bool HasProgressedIn(Scene scene) => progressedScenes.Contains(SceneKey(scene));

        public static GameStageProgression ForScene(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                GameStageProgression found = root.GetComponentInChildren<GameStageProgression>();
                if (found != null) return found;
            }
            var owner = new GameObject("Game Stage Progression");
            SceneManager.MoveGameObjectToScene(owner, scene);
            return owner.AddComponent<GameStageProgression>();
        }

        private void Start() => ApplyStage();

        // Also called after the world recreates its regions, while normal gameplay is paused.
        public void ApplyStage()
        {
            if (!HasProgressed)
            {
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                    foreach (IndicatorLight light in root.GetComponentsInChildren<IndicatorLight>(true))
                        light.SetOn(light.InitiallyOn);
                return;
            }
            WorldMapProgression flow = WorldMapProgression.Current;
            if (flow != null && flow.gameObject.scene == gameObject.scene) flow.SetExitsEnabled(false);
            EventManager.Publish(new LightsStateRequested(gameObject.scene, true));
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                // Inactive authored lights do not receive events; apply the stage before they become active too.
                foreach (IndicatorLight light in root.GetComponentsInChildren<IndicatorLight>(true)) light.SetOn(true);
                foreach (ProgressKey key in root.GetComponentsInChildren<ProgressKey>(true)) key.ApplyStage();
            }
        }

        public bool Collect(ProgressKey key, PlayerController character)
        {
            if (key == null || character == null || key.gameObject.scene != gameObject.scene || HasProgressed
                || InputBlocked || WorldMapProgression.InputBlocked || LevelAnnotation.IsFeedbackOpen || RewindManager.Rewinding
                || Time.timeScale <= 0f || character.Body.IsReturning || character.Head.IsReturning
                || (SceneResetManager.Existing != null && SceneResetManager.Existing.IsResetting)) return false;
            pendingKey = key;
            player = character;
            transitioning = this;
            key.SetCollected(true);
            player.GetComponent<PlayerInput>()?.ResetForTransition();
            StartCoroutine(AdvanceStage());
            return true;
        }

        private IEnumerator AdvanceStage()
        {
            // Finish the kinematic step that found the key before touching the rewind history.
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
            progressedScenes.Add(SceneKey(gameObject.scene));
            ApplyStage();
            // The first subsequent simulation frame is the baseline of the new stage.
            RewindManager.Current?.ClearHistory();
            player.GetComponent<PlayerInput>()?.ResetForTransition();
            pendingKey = null;
            transitioning = null;
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
            if (pendingKey != null) pendingKey.SetCollected(HasProgressed);
            player?.GetComponent<PlayerInput>()?.ResetForTransition();
            transitioning = null;
        }
    }
}
