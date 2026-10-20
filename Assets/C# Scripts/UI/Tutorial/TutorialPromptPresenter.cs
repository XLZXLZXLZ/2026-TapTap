using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TapTap
{
    [DisallowMultipleComponent, DefaultExecutionOrder(900)]
    [RequireComponent(typeof(Canvas), typeof(CanvasScaler))]
    public sealed class TutorialPromptPresenter : MonoBehaviour
    {
        [Tooltip("屏幕归一化位置；默认位于中部偏上。")]
        public Vector2 ScreenAnchor = new Vector2(0.5f, 0.8f);
        public bool UseKeyboard = true;
        [Tooltip("单条提示最长显示时间，使用真实时间，不受缓时或回溯影响。")]
        [Min(0.1f)] public float DisplayDuration = 15f;
        public TutorialPromptView ActiveView { get; private set; }
        public int QueuedCount => queue.Count;

        private enum Stage { Idle, Showing, Exiting, Gap, Dismissing }
        private Stage stage;
        private readonly Queue<TutorialPromptView> queue = new Queue<TutorialPromptView>();
        private readonly HashSet<TutorialPromptView> chain = new HashSet<TutorialPromptView>();
        private TutorialPromptView activePrefab, nextPrefab;
        private TutorialPromptInput activeInput, nextInput;
        private readonly bool[] externalHeld = new bool[3];
        private float gapRemaining;
        private float displayElapsed;

        private bool Transitioning => WorldMapSceneRouter.IsLoading
            || (SceneResetManager.Existing != null && SceneResetManager.Existing.IsResetting)
            || (WorldMapProgression.Current != null && WorldMapProgression.Current.gameObject.scene == gameObject.scene
                && (WorldMapProgression.Current.IsTransitioning || WorldMapProgression.Current.IsComplete));

        public static bool GameplayBlocked => WorldMapProgression.InputBlocked
            || (SceneResetManager.Existing != null && SceneResetManager.Existing.IsResetting);
        private bool CanObserve => !GameStageProgression.HasProgressedIn(gameObject.scene)
            && !GameplayBlocked && (!UseKeyboard || Application.isFocused);

        private void OnEnable()
        {
            EventManager.Subscribe<GameStageChanged>(StageChanged);
            if (GameStageProgression.HasProgressedIn(gameObject.scene)) ClearPrompts();
        }
        private void StageChanged(GameStageChanged message)
        {
            if (message.Scene == gameObject.scene && message.Stage >= GameStage.Returning) DismissPrompts();
        }

        private bool CanObserveKey(TutorialPromptKey key) => CanObserve
            && (!RewindManager.Rewinding || key == TutorialPromptKey.Z)
            && (key != TutorialPromptKey.Z || !GameplayActionLocks.IsLocked(GameplayAction.Rewind, gameObject.scene));

        public static TutorialPromptPresenter Ensure(Scene scene, TutorialPromptPresenter prefab = null)
        {
            foreach (TutorialPromptPresenter presenter in FindObjectsOfType<TutorialPromptPresenter>())
                if (presenter.gameObject.scene == scene) return presenter;
            TutorialPromptPresenter instance;
            if (prefab != null) instance = Instantiate(prefab);
            else
            {
                var root = new GameObject("Tutorial Prompts", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                root.layer = 5;
                instance = root.AddComponent<TutorialPromptPresenter>();
                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 900;
                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            }
            SceneManager.MoveGameObjectToScene(instance.gameObject, scene);
            return instance;
        }

        public bool Request(TutorialPromptView prefab)
        {
            if (prefab == null || !isActiveAndEnabled || Transitioning || stage == Stage.Dismissing
                || GameStageProgression.HasProgressedIn(gameObject.scene)) return false;
            queue.Enqueue(prefab);
            return true;
        }

        // Also usable by alternate input sources and deterministic interaction verification.
        public void SubmitInput(TutorialPromptKey key, bool held, bool pressed, bool released)
        {
            externalHeld[(int)key] = held;
            if (!CanObserveKey(key)) return;
            if (stage == Stage.Showing && activePrefab != null && activePrefab.Key == key)
                activeInput.Observe(held, pressed, released);
            else if (nextPrefab != null && nextPrefab.Key == key)
                nextInput.Observe(held, pressed, released);
        }

        private bool Held(TutorialPromptKey key) => UseKeyboard ? KeyboardHeld(key) : externalHeld[(int)key];

        private static bool KeyboardHeld(TutorialPromptKey key)
        {
            switch (key)
            {
                case TutorialPromptKey.Space: return Input.GetKey(KeyCode.Space);
                case TutorialPromptKey.Z: return Input.GetKey(KeyCode.Z);
                default: return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            }
        }

        private void ReadKeyboard(TutorialPromptKey key, TutorialPromptInput observer)
        {
            if (!CanObserveKey(key)) { observer.Synchronize(Held(key)); return; }
            KeyCode primary = key == TutorialPromptKey.Space ? KeyCode.Space
                : key == TutorialPromptKey.Z ? KeyCode.Z : KeyCode.LeftControl;
            bool pressed = Input.GetKeyDown(primary);
            bool released = Input.GetKeyUp(primary);
            if (key == TutorialPromptKey.Control)
            {
                pressed |= Input.GetKeyDown(KeyCode.RightControl);
                released |= Input.GetKeyUp(KeyCode.RightControl);
            }
            observer.Observe(Held(key), pressed, released);
        }

        private void LateUpdate()
        {
            bool suppressed = Transitioning || GameStageProgression.HasProgressedIn(gameObject.scene);
            if (suppressed) DismissPrompts();
            if (stage == Stage.Dismissing)
            {
                // Finish the fade even while transition locks block gameplay input.
                if (ActiveView == null || ActiveView.ExitFinished) ClearPrompts();
                return;
            }
            if (suppressed) return;
            if (stage == Stage.Showing)
            {
                displayElapsed += Time.unscaledDeltaTime;
                if (displayElapsed >= DisplayDuration && !ActiveView.IsEntering) BeginExit(false);
            }
            if (!CanObserve)
            {
                if (activeInput != null) activeInput.Synchronize(Held(activePrefab.Key));
                if (nextInput != null) nextInput.Synchronize(Held(nextPrefab.Key));
                return;
            }

            if (stage == Stage.Idle)
            {
                // Z's visible prompt remains active during rewind; new triggers and requests wait.
                if (queue.Count == 0 || RewindManager.Rewinding) return;
                chain.Clear();
                Show(queue.Dequeue(), null);
            }
            if (UseKeyboard)
            {
                if (stage == Stage.Showing) ReadKeyboard(activePrefab.Key, activeInput);
                else if (nextPrefab != null) ReadKeyboard(nextPrefab.Key, nextInput);
            }
            if (stage == Stage.Showing && !ActiveView.IsEntering && activeInput.Completed)
            {
                BeginExit(true);
            }
            else if (stage == Stage.Exiting && ActiveView.ExitFinished)
            {
                gapRemaining = ActiveView.NextDelay;
                Destroy(ActiveView.gameObject);
                ActiveView = null;
                stage = Stage.Gap;
            }
            else if (stage == Stage.Gap)
            {
                gapRemaining -= Time.unscaledDeltaTime;
                if (gapRemaining > 0f) return;
                if (nextPrefab != null)
                {
                    TutorialPromptView prefab = nextPrefab;
                    TutorialPromptInput observer = nextInput;
                    nextPrefab = null;
                    nextInput = null;
                    Show(prefab, observer);
                }
                else
                {
                    activeInput = null;
                    activePrefab = null;
                    stage = Stage.Idle;
                }
            }
        }

        private void BeginExit(bool completed)
        {
            ActiveView.Exit();
            stage = Stage.Exiting;
            // Follow-up instructions depend on completing the preceding operation.
            nextPrefab = completed ? ActiveView.NextPromptPrefab : null;
            nextInput = null;
            if (nextPrefab != null && chain.Contains(nextPrefab))
            {
                Debug.LogWarning("教学提示的 Next 链存在循环，已停止继续衔接。", ActiveView);
                nextPrefab = null;
            }
            if (nextPrefab != null)
                nextInput = new TutorialPromptInput(nextPrefab.Completion, Held(nextPrefab.Key));
        }

        private void DismissPrompts()
        {
            queue.Clear();
            chain.Clear();
            nextPrefab = null;
            nextInput = null;
            if (ActiveView == null) { ClearPrompts(); return; }
            if (stage == Stage.Dismissing) return;
            if (stage != Stage.Exiting) ActiveView.Exit();
            stage = Stage.Dismissing;
        }

        private void Show(TutorialPromptView prefab, TutorialPromptInput observer)
        {
            chain.Add(prefab);
            activePrefab = prefab;
            activeInput = observer ?? new TutorialPromptInput(prefab.Completion, Held(prefab.Key));
            ActiveView = Instantiate(prefab, transform, false);
            RectTransform rect = (RectTransform)ActiveView.transform;
            rect.anchorMin = rect.anchorMax = ScreenAnchor;
            rect.anchoredPosition = Vector2.zero;
            stage = Stage.Showing;
            displayElapsed = 0f;
            ActiveView.Enter();
        }

        private void OnDisable()
        {
            EventManager.Unsubscribe<GameStageChanged>(StageChanged);
            ClearPrompts();
        }

        private void ClearPrompts()
        {
            queue.Clear();
            chain.Clear();
            if (ActiveView != null)
            {
                // Destroy is deferred; stop rendering and kill any remaining tween immediately.
                ActiveView.gameObject.SetActive(false);
                Destroy(ActiveView.gameObject);
            }
            ActiveView = null;
            activePrefab = nextPrefab = null;
            activeInput = nextInput = null;
            gapRemaining = 0f;
            displayElapsed = 0f;
            System.Array.Clear(externalHeld, 0, externalHeld.Length);
            stage = Stage.Idle;
        }
    }
}
