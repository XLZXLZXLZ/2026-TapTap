using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace TapTap
{
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class RewindManager : MonoBehaviour
    {
        private interface ITrack
        {
            MonoBehaviour Owner { get; }
            int Order { get; }
            void Capture(int slot);
            void Restore(int slot);
        }

        private sealed class Track<TState> : ITrack where TState : struct
        {
            private readonly IRewindable<TState> participant;
            private readonly TState[] frames;
            public MonoBehaviour Owner { get; }
            public int Order { get; }

            public Track(MonoBehaviour owner, IRewindable<TState> target, int capacity, int order)
            {
                Owner = owner;
                participant = target;
                frames = new TState[capacity];
                Order = order;
            }

            public void Capture(int slot) => frames[slot] = participant.CaptureState();
            public void Restore(int slot) => participant.RestoreState(in frames[slot]);
        }

        [SerializeField, Min(0.1f)] private float historySeconds = 20f;
        [Header("回溯速度")]
        [Tooltip("每次开始按住 Z 时的回溯倍速。")]
        [FormerlySerializedAs("rewindSpeed")]
        [SerializeField, Min(0.1f)] private float startSpeed = 1f;
        [SerializeField, Min(0.1f)] private float maxSpeed = 2f;
        [Tooltip("从起始速度线性提升到最大速度的真实时间（秒）；0 表示立即达到最大速度。")]
        [SerializeField, Min(0f)] private float accelerationDuration = 5f;
        public bool UseKeyboard = true;
        private static RewindManager current;
        private readonly List<ITrack> tracks = new List<ITrack>();
        private readonly List<PlayerController> players = new List<PlayerController>();
        private readonly List<MovableEntity> entities = new List<MovableEntity>();
        private readonly List<MechanismSwitch> switches = new List<MechanismSwitch>();
        private double[] times;
        private int capacity;
        private float stepDuration;
        private long oldestTick, newestTick, currentTick;
        private double rewindRemainder;
        private double rewindHeldSeconds;
        private bool discovered;
        private bool historyDirty = true;
        private bool hasHistory;

        public static RewindManager Current => current;
        public static bool DrivesSimulation => current != null && current.isActiveAndEnabled;
        public static bool Rewinding => DrivesSimulation && current.IsRewinding;
        public bool IsRewinding { get; private set; }
        public double SimulationTime { get; private set; }
        public long CurrentTick => currentTick;
        public long OldestTick => oldestTick;
        public long NewestTick => newestTick;
        public float AvailableSeconds => hasHistory ? (currentTick - oldestTick) * stepDuration : 0f;
        public bool AtOldestHistory => hasHistory && currentTick <= oldestTick;
        public double RewindHeldSeconds => rewindHeldSeconds;
        public float RewindSpeedProgress => accelerationDuration > 0f
            ? Mathf.Clamp01((float)(rewindHeldSeconds / accelerationDuration)) : 1f;
        public float CurrentRewindSpeed => Mathf.Lerp(Mathf.Max(0.1f, startSpeed),
            Mathf.Max(0.1f, startSpeed, maxSpeed), RewindSpeedProgress);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            current = null;
            SceneManager.sceneLoaded -= EnsureSceneManager;
            SceneManager.sceneLoaded += EnsureSceneManager;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() => EnsureSceneManager(SceneManager.GetActiveScene(), LoadSceneMode.Single);

        private static void EnsureSceneManager(Scene scene, LoadSceneMode mode)
        {
            if (current != null) return;
            current = FindObjectOfType<RewindManager>();
            if (current == null) current = new GameObject("World Rewind").AddComponent<RewindManager>();
        }

        private void Awake()
        {
            current = this;
            AllocateTimeline(Time.fixedDeltaTime);
        }

        private void Start() => DiscoverParticipants();

        private void AllocateTimeline(float dt)
        {
            stepDuration = dt;
            capacity = Mathf.Max(2, Mathf.CeilToInt(historySeconds / dt) + 1);
            times = new double[capacity];
        }

        public void Register<TState>(MonoBehaviour owner, IRewindable<TState> participant, int order)
            where TState : struct
        {
            foreach (ITrack track in tracks) if (track.Owner == owner) return;
            tracks.Add(new Track<TState>(owner, participant, capacity, order));
            tracks.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order)
                : a.Owner.GetInstanceID().CompareTo(b.Owner.GetInstanceID()));
            if (owner is PlayerController player) { players.Add(player); players.Sort(CompareOwners); }
            if (owner is MovableEntity entity) { entities.Add(entity); entities.Sort(CompareOwners); }
            if (owner is MechanismSwitch button) { switches.Add(button); switches.Sort(CompareOwners); }
            InvalidateHistory();
        }

        private static int CompareOwners<T>(T a, T b) where T : UnityEngine.Object =>
            a.GetInstanceID().CompareTo(b.GetInstanceID());

        public void Unregister(MonoBehaviour owner)
        {
            if (tracks.RemoveAll(track => track.Owner == owner) == 0) return;
            if (owner is PlayerController player) players.Remove(player);
            if (owner is MovableEntity entity) entities.Remove(entity);
            if (owner is MechanismSwitch button) switches.Remove(button);
            // V1 handles resident objects; changing the participant set starts a new history.
            InvalidateHistory();
        }

        private void DiscoverParticipants()
        {
            if (discovered) return;
            discovered = true;
            Register(WorldPhaseState.Instance, WorldPhaseState.Instance, 0);
            foreach (MovableEntity entity in FindObjectsOfType<MovableEntity>()) Register(entity, entity, 10);
            foreach (PlayerController player in FindObjectsOfType<PlayerController>()) Register(player, player, 20);
            foreach (MechanismSwitch button in FindObjectsOfType<MechanismSwitch>()) Register(button, button, 30);
        }

        private void InvalidateHistory()
        {
            EndRewind();
            historyDirty = true;
            hasHistory = false;
        }

        public void ClearHistory() => InvalidateHistory();

        private bool PrepareHistory()
        {
            DiscoverParticipants();
            foreach (PlayerController player in players) if (!player.PrepareSimulation()) return false;
            if (!historyDirty) return true;
            historyDirty = false;
            oldestTick = newestTick = currentTick = 0;
            CaptureFrame();
            hasHistory = true;
            return true;
        }

        private bool Blocked => LevelAnnotation.IsFeedbackOpen
            || (SceneResetManager.Existing != null && SceneResetManager.Existing.IsResetting);

        private void ReadControls()
        {
            if (Blocked) { EndRewind(); return; }
            if (!UseKeyboard) return;
            if (Application.isFocused && Input.GetKey(KeyCode.Z)) BeginRewind();
            else EndRewind();
        }

        private void Update()
        {
            ReadControls();
            SimulateRewind(Time.unscaledDeltaTime);
        }

        public void SimulateRewind(float unscaledDt)
        {
            if (!IsRewinding || unscaledDt <= 0f || Blocked) return;
            double start = rewindHeldSeconds;
            rewindHeldSeconds += unscaledDt;
            double initial = Math.Max(0.1f, startSpeed);
            double maximum = Math.Max(initial, maxSpeed);
            double distance;
            if (accelerationDuration <= 0f) distance = maximum * unscaledDt;
            else
            {
                // Integrate the linear ramp exactly, including a frame that crosses the speed cap.
                double rampStart = Math.Min(start, accelerationDuration);
                double rampEnd = Math.Min(rewindHeldSeconds, accelerationDuration);
                double rampIntegral = (rampEnd * rampEnd - rampStart * rampStart) / (2d * accelerationDuration);
                double cappedTime = Math.Max(0d, rewindHeldSeconds - accelerationDuration)
                    - Math.Max(0d, start - accelerationDuration);
                distance = initial * unscaledDt + (maximum - initial) * (rampIntegral + cappedTime);
            }
            RewindBy((float)distance);
            EffectManager.Instance.SetRewindVisual(true, RewindSpeedProgress, AtOldestHistory);
        }

        private void FixedUpdate()
        {
            ReadControls();
            if (!Blocked && !IsRewinding) SimulateStep(Time.fixedDeltaTime);
        }

        public void SimulateStep(float dt)
        {
            if (dt <= 0f || IsRewinding || Blocked) return;
            if (!Mathf.Approximately(dt, stepDuration))
                throw new ArgumentException("Rewind simulation must use its fixed timestep.", nameof(dt));
            if (!PrepareHistory()) return;
            SimulationTime += dt;
            foreach (PlayerController player in players) player.AdvanceRespawn(dt);
            foreach (MovableEntity entity in entities) entity.Motor.BeginStep(dt, false);
            foreach (MovableEntity entity in entities) entity.Motor.ProbeGround();
            foreach (PlayerController player in players) player.SimulatePreparedStep(dt);
            foreach (MovableEntity entity in entities)
                if (!entity.ControlledExternally && !entity.IsReturning) entity.SimulateFree(dt, 0f);
            foreach (MechanismSwitch button in switches) button.SimulateStep();
            foreach (MovableEntity entity in entities) entity.Motor.Commit();
            currentTick++;
            newestTick = currentTick;
            oldestTick = Math.Max(oldestTick, newestTick - capacity + 1);
            CaptureFrame();
        }

        private void CaptureFrame()
        {
            int slot = (int)(currentTick % capacity);
            times[slot] = SimulationTime;
            foreach (ITrack track in tracks) track.Capture(slot);
        }

        public bool BeginRewind()
        {
            if (IsRewinding) return true;
            if (Blocked || !PrepareHistory()) return false;
            IsRewinding = true;
            rewindRemainder = 0d;
            rewindHeldSeconds = 0d;
            EffectManager.Instance.ClearTransientEffects();
            foreach (SpringPad spring in FindObjectsOfType<SpringPad>()) spring.ResetAnimation();
            foreach (PlayerController player in players) player.BeginRewind();
            foreach (MovableEntity entity in entities) entity.Motor.SetRewinding(true);
            RestoreFrame();
            EffectManager.Instance.SetRewindVisual(true, RewindSpeedProgress, AtOldestHistory);
            return true;
        }

        public void RewindBy(float seconds)
        {
            if (!IsRewinding || seconds <= 0f || Blocked) return;
            rewindRemainder += seconds / stepDuration;
            long steps = (long)Math.Floor(rewindRemainder + 0.00001d);
            if (steps == 0) return;
            rewindRemainder -= steps;
            long target = Math.Max(oldestTick, currentTick - steps);
            if (target == currentTick) return;
            currentTick = target;
            RestoreFrame();
            if (EffectManager.Existing != null)
                EffectManager.Existing.SetRewindVisual(true, RewindSpeedProgress, AtOldestHistory);
        }

        private void RestoreFrame()
        {
            int slot = (int)(currentTick % capacity);
            SimulationTime = times[slot];
            foreach (ITrack track in tracks) track.Restore(slot);
            Physics2D.SyncTransforms();
            foreach (MovableEntity entity in entities) entity.Motor.ProbeGround();
            foreach (PlayerController player in players) player.AfterRewindRestore();
        }

        public void EndRewind()
        {
            if (!IsRewinding) return;
            // Discard the abandoned future. Its slots are overwritten by the new branch.
            newestTick = currentTick;
            IsRewinding = false;
            rewindRemainder = 0d;
            rewindHeldSeconds = 0d;
            if (EffectManager.Existing != null) EffectManager.Existing.SetRewindVisual(false);
            foreach (MovableEntity entity in entities) if (entity != null) entity.Motor.SetRewinding(false);
            foreach (PlayerController player in players) if (player != null) player.EndRewind();
        }

        private void OnApplicationFocus(bool focused) { if (!focused) EndRewind(); }
        private void OnDisable() => EndRewind();
        private void OnDestroy() { if (current == this) current = null; }
    }
}
