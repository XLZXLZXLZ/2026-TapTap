using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    public enum VoiceStopReason { Completed, Interrupted, Replaced, Disabled, AudioLoadFailed }

    [DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
    public sealed class VoicePlayer : MonoBehaviour
    {
        public VoiceSubtitlePresenter SubtitlePrefab;
        private static readonly Dictionary<int, VoicePlayer> active = new Dictionary<int, VoicePlayer>();
        private AudioSource source;
        private VoiceSubtitlePresenter subtitles;
        private float remaining, elapsed;
        private int startedFrame;
        private bool started, suspended;

        public VoiceSequence Sequence { get; private set; }
        public int SegmentIndex { get; private set; } = -1;
        public bool IsPlaying => Sequence != null;
        public bool IsInGap { get; private set; }
        public bool IsPaused { get; private set; }
        public float SegmentElapsed => elapsed;
        public float DistanceGain { get; private set; } = 1f;
        public bool IsAudible => DistanceGain > 0.0001f;
        public AudioSource Source => source;
        public VoiceSegment CurrentSegment => IsPlaying && SegmentIndex >= 0
            && SegmentIndex < Sequence.Segments.Length ? Sequence.Segments[SegmentIndex] : null;
        public string CurrentSubtitle => IsPlaying && started && !IsInGap && !IsPaused && IsAudible
            ? CurrentSegment.SubtitleAt(elapsed, SubtitleLanguageSettings.Language) : "";
        public bool HasSubtitle => !string.IsNullOrWhiteSpace(CurrentSubtitle);
        // A radio close-up ignores its own input lease; other locks and pause still suspend audio.
        public Component InputLockOwner { get; set; }
        public event Action<VoicePlayer, VoiceSequence> Began;
        public event Action<VoicePlayer, VoiceSequence, VoiceStopReason> Ended;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => active.Clear();

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = source.loop = false;
            // Distance is measured in the gameplay XY plane, against the player rather than the camera.
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.pitch = 1f;
        }

        public bool Play(VoiceSequence sequence)
        {
            if (!isActiveAndEnabled || sequence == null || !sequence.IsConfigured) return false;
            if (source == null) Awake();
            Stop();
            int scene = gameObject.scene.handle;
            if (active.TryGetValue(scene, out VoicePlayer other) && other != null && other != this)
                other.Stop(VoiceStopReason.Replaced);
            active[scene] = this;
            Sequence = sequence;
            ApplyVolume();
            subtitles = VoiceSubtitlePresenter.Ensure(gameObject.scene, SubtitlePrefab);
            subtitles?.Bind(this);
            BeginSegment(0);
            Began?.Invoke(this, sequence);
            return true;
        }

        public void SetDistanceGain(float gain)
        {
            DistanceGain = Mathf.Clamp01(gain);
            ApplyVolume();
        }

        private void ApplyVolume()
        {
            if (source == null) return;
            float effects = MenuAudio.Current != null ? MenuAudio.Current.EffectsVolume
                : Mathf.Clamp01(PlayerPrefs.GetFloat(MenuAudio.EffectsPreference, 0.7f));
            source.volume = DistanceGain * (Sequence != null ? Mathf.Clamp01(Sequence.Volume) : 0f) * effects;
        }

        public void Stop(VoiceStopReason reason = VoiceStopReason.Interrupted)
        {
            if (!IsPlaying) return;
            VoiceSequence previous = Sequence;
            Sequence = null;
            SegmentIndex = -1;
            started = suspended = IsPaused = IsInGap = false;
            remaining = elapsed = 0f;
            if (source != null) { source.Stop(); source.clip = null; }
            int scene = gameObject.scene.handle;
            if (active.TryGetValue(scene, out VoicePlayer owner) && owner == this) active.Remove(scene);
            subtitles?.Release(this);
            Ended?.Invoke(this, previous, reason);
        }

        private bool GameplayPaused => Time.timeScale <= 0f || RewindManager.Rewinding
            || WorldMapProgression.IsInputBlocked(InputLockOwner) || WorldMapSceneRouter.IsLoading
            || (SceneResetManager.Existing != null && SceneResetManager.Existing.IsResetting);

        private void BeginSegment(int index)
        {
            SegmentIndex = index;
            IsInGap = started = suspended = false;
            elapsed = 0f;
            VoiceSegment segment = CurrentSegment;
            source.Stop();
            source.clip = segment.Clip;
            remaining = segment.Clip.length;
            if (segment.Clip.loadState == AudioDataLoadState.Unloaded) segment.Clip.LoadAudioData();
            IsPaused = GameplayPaused;
            if (!IsPaused && segment.Clip.loadState == AudioDataLoadState.Loaded) StartAudio();
        }

        private void StartAudio()
        {
            source.pitch = 1f;
            source.Play();
            started = true;
            startedFrame = Time.frameCount;
        }

        private void Update()
        {
            if (source == null) return;
            ApplyVolume();
            if (!IsPlaying) return;
            IsPaused = GameplayPaused;
            if (IsPaused)
            {
                if (started && !suspended) { source.Pause(); suspended = true; }
                return;
            }
            if (suspended) { source.UnPause(); suspended = false; }
            if (IsInGap)
            {
                remaining -= Time.unscaledDeltaTime;
                if (remaining <= 0f) BeginSegment(SegmentIndex + 1);
                return;
            }
            if (!started)
            {
                if (CurrentSegment.Clip.loadState == AudioDataLoadState.Failed)
                {
                    Debug.LogWarning("语音加载失败，未将本阶段标记为已收听。", this);
                    Stop(VoiceStopReason.AudioLoadFailed);
                }
                else if (CurrentSegment.Clip.loadState == AudioDataLoadState.Loaded) StartAudio();
                return;
            }
            if (Time.frameCount == startedFrame) return;
            // Subtitle timing follows the actual audio sample position rather than accumulated frames.
            // AudioSource.Pause/UnPause therefore freezes/resumes the same subtitle clock.
            if (source.isPlaying)
            {
                elapsed = source.timeSamples / (float)CurrentSegment.Clip.frequency;
                return;
            }
            elapsed = CurrentSegment.Clip.length;
            source.Stop();
            started = false;
            if (SegmentIndex + 1 >= Sequence.Segments.Length) Stop(VoiceStopReason.Completed);
            else
            {
                remaining = Mathf.Max(0f, CurrentSegment.GapAfter);
                IsInGap = true;
                if (remaining <= 0f) BeginSegment(SegmentIndex + 1);
            }
        }

        private void OnDisable() => Stop(VoiceStopReason.Disabled);
    }
}
