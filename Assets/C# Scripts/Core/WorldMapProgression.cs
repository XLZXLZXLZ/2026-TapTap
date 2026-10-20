using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldMapRoot))]
    public sealed class WorldMapProgression : MonoBehaviour, IRewindable<WorldMapProgression.Snapshot>
    {
        public struct Snapshot { public int CurrentIndex; }
        [Tooltip("身体中心离开当前区域超过此格数后才切区，避免边界反复切换。")]
        [Range(0f, 0.45f)] public float RegionBuffer = 0.12f;
        [Min(0.01f)] public float FadeDuration = 0.25f;
        [Min(0.1f)] public float AssemblyDuration = 1.2f;
        [Min(0f)] public float AssemblyHold = 0.45f;
        public bool UseKeyboard = true;
        [Tooltip("直接启动此场景时的关卡序号；0 从首关开始。从选关界面进入时仍优先使用选中的关卡。")]
        [Min(0)] public int InitialLevelNumber;
        private WorldMapRoot world;
        private PlayerController player;
        private LevelSceneRuntime runtime;
        private WorldMapAssemblyView assembly;
        private List<WorldMapModule> ordered;
        private readonly HashSet<string> completed = new HashSet<string>();
        private readonly HashSet<string> discovered = new HashSet<string>();
        private PlayerController.Snapshot initialPlayer;
        private MovableEntity.Snapshot initialBody, initialHead;
        private bool initialPhase;
        private bool initialized;
        private float resumeTimeScale = 1f;
        private PlayerExitView playerExit;
        private static WorldMapProgression current;

        public static WorldMapProgression Current => current;
        public static bool InputBlocked => IsInputBlocked();
        public static bool IsInputBlocked(Component exceptOwner = null) => PauseMenu.BlocksInput
            || GameplayActionLocks.IsLocked(GameplayAction.Input, UnityEngine.SceneManagement.SceneManager.GetActiveScene(), exceptOwner)
            || WorldMapSceneRouter.IsLoading || WorldLevelMenu.IsOpen
            || (current != null && (current.IsTransitioning || current.IsComplete));
        public bool IsTransitioning { get; private set; }
        public bool IsComplete { get; private set; }
        public bool ExitsEnabled { get; private set; } = true;
        public int CurrentIndex { get; private set; }
        public int LevelCount => ordered?.Count ?? 0;
        public int CompletedCount => completed.Count;
        public WorldMapModule CurrentModule => initialized ? ordered[CurrentIndex] : null;
        public LevelRegion CurrentRegion => CurrentModule != null ? world.FindRegion(CurrentModule) : null;
        public int CurrentNumber => CurrentModule != null ? world.Source.SequenceNumber(CurrentModule) : 0;
        public float DeathBoundary => world.WorldBounds.min.y + player.Config.DeathBoundary;
        public IReadOnlyList<WorldMapModule> Modules => ordered;
        public bool HasCompleted(WorldMapModule module) => completed.Contains(module.Id);
        public bool HasDiscovered(WorldMapModule module) => discovered.Contains(module.Id);
        public event Action<WorldMapModule> LevelChanged;
        public event Action WorldCompleted;

        public void SetExitsEnabled(bool enabled) => ExitsEnabled = enabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => current = null;

        private void Start()
        {
            if (!initialized) Initialize(GetComponent<WorldMapRoot>(), FindObjectOfType<PlayerController>(),
                FindObjectOfType<LevelSceneRuntime>());
        }

        private void OnEnable()
        {
            if (!initialized) return;
            current = this;
            RewindManager.Current?.Register(this, this, 40);
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
            if (start < 0 && InitialLevelNumber > 0)
                start = ordered.FindIndex(module => world.Source.SequenceNumber(module) == InitialLevelNumber);
            Activate(Mathf.Max(0, start), false);
            RewindManager.Current?.Register(this, this, 40);
            return true;
        }

        // Called after movement and before death handling, using logical body geometry rather than render interpolation.
        public void UpdatePlayerRegion(PlayerController character)
        {
            if (!initialized || character != player || IsTransitioning || IsComplete || player.Body.IsReturning) return;
            Vector2 center = player.Body.Motor.Position + player.Body.Motor.CenterOffset;
            if (!CurrentRegion.ContainsWorldPoint(center, RegionBuffer))
            {
                for (int i = 0; i < ordered.Count; i++)
                    if (world.FindRegion(ordered[i]).ContainsWorldPoint(center))
                    {
                        if (i != CurrentIndex) SelectRegion(i, false, true);
                        break;
                    }
            }
            player.GetComponent<RespawnService>().SetCheckpoint(CurrentRegion.EntrySpawnPosition);
        }

        private void SelectRegion(int index, bool snapCamera, bool notify)
        {
            CurrentIndex = index;
            if (discovered.Add(CurrentModule.Id)) WorldMapProgressStore.Unlock(world.Source, CurrentModule);
            LevelRegion region = CurrentRegion;
            region.GetComponent<RegionRevealView>()?.SetDiscovered(true);
            DemoCamera camera = runtime != null ? runtime.CameraRig : FindObjectOfType<DemoCamera>();
            camera?.SetRegion(region, snapCamera);
            AudioManager.Instance?.ApplyLevelMusic(gameObject.scene, CurrentModule.Level, CurrentNumber);
            if (notify) LevelChanged?.Invoke(CurrentModule);
        }

        public Snapshot CaptureState() => new Snapshot { CurrentIndex = CurrentIndex };
        public bool CanSkipRecording(in Snapshot previous, in Snapshot current) => previous.CurrentIndex == current.CurrentIndex;
        public void RestoreState(in Snapshot state)
        {
            if (!initialized || state.CurrentIndex < 0 || state.CurrentIndex >= ordered.Count) return;
            // Discovery lasts for this run and survives rewind; resetting the world clears it.
            SelectRegion(state.CurrentIndex, true, false);
        }

        private void Update()
        {
            if (!initialized || !UseKeyboard || IsTransitioning) return;
            if (WorldMapSceneRouter.IsLoading || PauseMenu.BlocksInput) return;
            if (!PauseMenu.HandlesEscape && Input.GetKeyDown(KeyCode.Escape)) ReturnToSelection();
        }

        private bool CanTransition => initialized && !PauseMenu.IsOpen && !IsTransitioning && !GameStageProgression.InputBlocked && !WorldMapSceneRouter.IsLoading
            && !GameplayActionLocks.IsLocked(GameplayAction.Input, gameObject.scene)
            && !GameplayActionLocks.IsLocked(GameplayAction.PlayerControls, gameObject.scene)
            && !RewindManager.Rewinding && !(SceneResetManager.Existing != null && SceneResetManager.Existing.IsResetting);

        public bool CompleteCurrent(LevelEndpoint endpoint)
        {
            if (!ExitsEnabled || !CanTransition || IsComplete || endpoint == null
                || endpoint.GetComponentInParent<LevelRegion>() != CurrentRegion || player.Body.IsReturning) return false;
            StartTransition(CurrentIndex + 1, true, endpoint);
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

        private void StartTransition(int nextIndex, bool passed, LevelEndpoint endpoint = null)
        {
            AudioManager.Existing?.StopGameplay();
            if (passed) AudioManager.Instance?.Play(SoundId.Complete);
            CancelPlayerExit();
            IsTransitioning = true;
            RewindManager.Current?.ClearHistory();
            player.BeginRewind();
            player.GetComponent<RespawnService>().CancelAll();
            EffectManager.Instance.ClearTransientEffects();
            resumeTimeScale = Time.timeScale;
            player.GetComponent<PlayerInput>().ResetForTransition();
            StartCoroutine(Transition(nextIndex, passed, endpoint));
        }

        private IEnumerator Transition(int nextIndex, bool passed, LevelEndpoint endpoint)
        {
            ScreenFadeOverlay fade = ScreenFadeOverlay.Instance;
            bool success = false;
            try
            {
                if (passed && endpoint != null)
                {
                    // Let the fixed step finish before taking over presentation. InputBlocked
                    // freezes gameplay; the entry walk uses unscaled animation independently.
                    yield return null;
                    playerExit = player.GetComponent<PlayerExitView>();
                    if (playerExit == null) playerExit = player.gameObject.AddComponent<PlayerExitView>();
                    if (playerExit.Begin(endpoint))
                        while (playerExit != null && playerExit.IsPlaying) yield return null;
                }
                Time.timeScale = 0f;
                yield return fade.FadeTo(1f, FadeDuration);
                yield return null;
                WorldMapModule finished = CurrentModule;
                bool showBoard = passed || nextIndex != CurrentIndex;
                if (passed)
                {
                    WorldMapModule upcoming = nextIndex < LevelCount ? ordered[nextIndex] : null;
                    yield return assembly.Play(world, ordered, completed, upcoming, AssemblyDuration, AssemblyHold,
                        departing: finished);
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
                    CancelPlayerExit();
                    Activate(nextIndex, true);
                    // Start/OnEnable callbacks initialize the refreshed room while simulation is paused.
                    yield return null;
                }
                yield return fade.FadeTo(0f, FadeDuration);
                success = true;
            }
            finally
            {
                if (!success) { assembly.Hide(); CancelPlayerExit(); }
                fade.SetOpacity(0f);
                Time.timeScale = resumeTimeScale;
                player.GetComponent<PlayerInput>().ResetForTransition();
                IsTransitioning = false;
            }
        }

        private void CancelPlayerExit()
        {
            if (playerExit != null) playerExit.Cancel();
            playerExit = null;
        }

        private void Activate(int index, bool refresh)
        {
            if (refresh) world.RefreshLevels();
            discovered.Clear();
            discovered.Add(ordered[index].Id);
            world.PrepareExploration(discovered);
            WorldPhaseState.Instance.RestoreState(in initialPhase);
            player.GetComponent<RespawnService>().CancelAll();
            player.Body.RestoreState(in initialBody); player.Head.RestoreState(in initialHead);
            player.RestoreState(in initialPlayer);
            CurrentIndex = index;
            WorldMapProgressStore.Unlock(world.Source, ordered[index]);
            IsComplete = false;
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
            SelectRegion(index, true, false);
            GameStageProgression.ForScene(gameObject.scene).ApplyStage();
            Physics2D.SyncTransforms();
            RewindManager.Current?.ClearHistory();
            LevelChanged?.Invoke(CurrentModule);
        }

        private void OnDisable()
        {
            CancelPlayerExit();
            RewindManager.Current?.Unregister(this);
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
