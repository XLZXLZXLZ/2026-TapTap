using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldMapRoot))]
    public sealed class WorldMapProgression : MonoBehaviour
    {
        [Min(0.01f)] public float FadeDuration = 0.25f;
        [Min(0.1f)] public float AssemblyDuration = 1.2f;
        [Min(0f)] public float AssemblyHold = 0.45f;
        public bool UseKeyboard = true;
        private WorldMapRoot world;
        private PlayerController player;
        private LevelSceneRuntime runtime;
        private WorldMapAssemblyView assembly;
        private List<WorldMapModule> ordered;
        private readonly HashSet<string> completed = new HashSet<string>();
        private PlayerController.Snapshot initialPlayer;
        private MovableEntity.Snapshot initialBody, initialHead;
        private bool initialPhase;
        private bool initialized;
        private float resumeTimeScale = 1f;
        private static WorldMapProgression current;

        public static WorldMapProgression Current => current;
        public static bool InputBlocked => WorldMapSceneRouter.IsLoading || WorldLevelMenu.IsOpen
            || (current != null && (current.IsTransitioning || current.IsComplete));
        public bool IsTransitioning { get; private set; }
        public bool IsComplete { get; private set; }
        public int CurrentIndex { get; private set; }
        public int LevelCount => ordered?.Count ?? 0;
        public int CompletedCount => completed.Count;
        public WorldMapModule CurrentModule => initialized ? ordered[CurrentIndex] : null;
        public LevelRegion CurrentRegion => CurrentModule != null ? world.FindRegion(CurrentModule) : null;
        public int CurrentNumber => CurrentModule != null ? world.Source.SequenceNumber(CurrentModule) : 0;
        public IReadOnlyList<WorldMapModule> Modules => ordered;
        public bool HasCompleted(WorldMapModule module) => completed.Contains(module.Id);
        public event Action<WorldMapModule> LevelChanged;
        public event Action WorldCompleted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => current = null;

        private void Start()
        {
            if (!initialized) Initialize(GetComponent<WorldMapRoot>(), FindObjectOfType<PlayerController>(),
                FindObjectOfType<LevelSceneRuntime>());
        }

        public bool Initialize(WorldMapRoot map, PlayerController character, LevelSceneRuntime shared)
        {
            if (initialized) return true;
            if (map == null || map.Source == null || character == null || !character.PrepareSimulation())
            {
                Debug.LogError("大世界流程缺少地图或玩家配置，请通过大世界编辑器生成入口与游玩场景。", this);
                return false;
            }
            world = map; player = character; runtime = shared;
            ordered = world.Source.OrderedModules();
            if (ordered.Count == 0) return false;
            foreach (WorldMapModule module in ordered)
                if (world.FindRegion(module) == null)
                {
                    Debug.LogError("大世界缺少关卡实例：" + module.DisplayName + "，请重新生成整体 Prefab。", this);
                    return false;
                }
            initialPlayer = player.CaptureState();
            initialBody = player.Body.CaptureState(); initialHead = player.Head.CaptureState();
            initialPhase = WorldPhaseState.Instance.Active;
            assembly = GetComponent<WorldMapAssemblyView>();
            if (assembly == null) assembly = gameObject.AddComponent<WorldMapAssemblyView>();
            current = this;
            initialized = true;
            completed.UnionWith(WorldMapProgressStore.Completed(world.Source));
            completed.RemoveWhere(id => !ordered.Exists(module => module.Id == id));
            string requested = WorldMapSceneRouter.ConsumeModule(world.Source);
            int start = ordered.FindIndex(module => module.Id == requested);
            Activate(Mathf.Max(0, start), false);
            return true;
        }

        private void Update()
        {
            if (!initialized || !UseKeyboard || IsTransitioning || LevelAnnotation.IsFeedbackOpen) return;
            if (WorldMapSceneRouter.IsLoading) return;
            if (Input.GetKeyDown(KeyCode.P)) WorldMapProgressStore.UnlockAll(world.Source);
            if (Input.GetKeyDown(KeyCode.Escape)) ReturnToSelection();
            if (Input.GetKeyDown(KeyCode.PageDown)) SkipNext();
        }

        private bool CanTransition => initialized && !IsTransitioning && !WorldMapSceneRouter.IsLoading && !LevelAnnotation.IsFeedbackOpen
            && !RewindManager.Rewinding && !(SceneResetManager.Existing != null && SceneResetManager.Existing.IsResetting);

        public bool CompleteCurrent(LevelEndpoint endpoint)
        {
            if (!CanTransition || IsComplete || endpoint == null
                || endpoint.GetComponentInParent<LevelRegion>() != CurrentRegion) return false;
            StartTransition(CurrentIndex + 1, true);
            return true;
        }

        public bool SkipNext()
        {
            if (!CanTransition || IsComplete || CurrentIndex + 1 >= LevelCount) return false;
            StartTransition(CurrentIndex + 1, false);
            return true;
        }

        public bool GoToSequence(int number)
        {
            if (!CanTransition) return false;
            int index = ordered.FindIndex(module => world.Source.SequenceNumber(module) == number);
            if (index < 0) return false;
            StartTransition(index, false);
            return true;
        }

        public bool RestartCurrent()
        {
            if (!CanTransition) return false;
            StartTransition(CurrentIndex, false);
            return true;
        }

        public bool ReturnToSelection()
        {
            if (!CanTransition) return false;
            return WorldMapSceneRouter.Instance.Load(world.Source.SelectionScenePath);
        }

        private void StartTransition(int nextIndex, bool passed)
        {
            IsTransitioning = true;
            RewindManager.Current?.ClearHistory();
            player.BeginRewind();
            player.GetComponent<RespawnService>().CancelAll();
            EffectManager.Instance.ClearTransientEffects();
            resumeTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            player.GetComponent<PlayerInput>().ResetForTransition();
            StartCoroutine(Transition(nextIndex, passed));
        }

        private IEnumerator Transition(int nextIndex, bool passed)
        {
            ScreenFadeOverlay fade = ScreenFadeOverlay.Instance;
            bool success = false;
            try
            {
                yield return fade.FadeTo(1f, FadeDuration);
                yield return null;
                WorldMapModule finished = CurrentModule;
                bool showBoard = passed || nextIndex != CurrentIndex;
                if (passed)
                {
                    yield return assembly.Play(world, ordered, completed, finished, AssemblyDuration, AssemblyHold);
                    completed.Add(finished.Id);
                    WorldMapProgressStore.Complete(world.Source, finished);
                }
                else if (showBoard)
                    yield return assembly.Play(world, ordered, completed, null, 0f, Mathf.Min(0.3f, AssemblyHold),
                        "前往第 " + world.Source.SequenceNumber(ordered[nextIndex]) + " 关");
                if (nextIndex >= LevelCount)
                {
                    IsComplete = true;
                    assembly.ShowCompletion();
                    WorldCompleted?.Invoke();
                }
                else
                {
                    if (showBoard) yield return fade.FadeTo(1f, FadeDuration);
                    assembly.Hide();
                    Activate(nextIndex, true);
                    // Start/OnEnable callbacks initialize the refreshed room while simulation is paused.
                    yield return null;
                }
                yield return fade.FadeTo(0f, FadeDuration);
                success = true;
            }
            finally
            {
                if (!success) assembly.Hide();
                fade.SetOpacity(0f);
                Time.timeScale = resumeTimeScale;
                player.GetComponent<PlayerInput>().ResetForTransition();
                IsTransitioning = false;
            }
        }

        private void Activate(int index, bool refresh)
        {
            if (refresh) world.RefreshLevels();
            WorldPhaseState.Instance.RestoreState(in initialPhase);
            player.GetComponent<RespawnService>().CancelAll();
            player.Body.RestoreState(in initialBody); player.Head.RestoreState(in initialHead);
            player.RestoreState(in initialPlayer);
            CurrentIndex = index;
            WorldMapProgressStore.Unlock(world.Source, ordered[index]);
            IsComplete = false;
            world.ShowOnly(CurrentModule);
            LevelRegion region = CurrentRegion;
            Vector2 spawn = region.EntrySpawnPosition;
            player.TeleportJoined(spawn);
            player.GetComponent<RespawnService>().SetCheckpoint(spawn);
            player.GetComponent<PlayerInput>().ResetForTransition();
            runtime?.Configure(player, region, player.Config, spawn);
            if (runtime == null)
            {
                DemoCamera camera = FindObjectOfType<DemoCamera>();
                camera?.BindPlayer(player); camera?.SetRegion(region);
            }
            Physics2D.SyncTransforms();
            RewindManager.Current?.ClearHistory();
            LevelChanged?.Invoke(CurrentModule);
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            if (IsTransitioning)
            {
                Time.timeScale = resumeTimeScale;
                if (ScreenFadeOverlay.Existing != null) ScreenFadeOverlay.Existing.SetOpacity(0f);
                IsTransitioning = false;
            }
            if (assembly != null) assembly.Hide();
            if (current == this) current = null;
        }
    }
}
