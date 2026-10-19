using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap
{
    // Persistent service. Gameplay distances are measured in XY against the body, never the distant perspective camera.
    [DefaultExecutionOrder(1000), DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        public const string MusicPreference = "TapTap.Audio.Music";
        public const string EffectsPreference = "TapTap.Audio.Effects";
        private const int VoiceCount = 16;
        private static AudioManager current;
        private static bool quitting;
        public static AudioManager Existing => current;
        public static AudioManager Instance
        {
            get
            {
                if (current == null && !quitting && Application.isPlaying)
                {
                    current = FindObjectOfType<AudioManager>();
                    if (current == null) current = new GameObject("Audio Manager").AddComponent<AudioManager>();
                }
                if (current != null && current.MusicSource == null) current.InitializeService();
                return current;
            }
        }
        public SoundLibrary Library;
        public AudioSource MusicSource { get; private set; }
        public AudioSource EffectsSource { get; private set; }
        public float MusicVolume { get; private set; }
        public float EffectsVolume { get; private set; }
        public int ActiveLoopCount => loops.Count;
        public int PlayedCount { get; private set; }
        public SoundId LastPlayed { get; private set; }

        private sealed class Voice
        {
            public AudioSource Source;
            public SoundDefinition Sound;
            public float Gain;
            public double Started;
            public bool Paused;
        }
        private readonly struct Key : IEquatable<Key>
        {
            private readonly SoundId id;
            private readonly int owner;
            public Key(SoundId sound, Object target) { id = sound; owner = target != null ? target.GetInstanceID() : 0; }
            public bool Equals(Key other) => id == other.id && owner == other.owner;
            public override bool Equals(object other) => other is Key key && Equals(key);
            public override int GetHashCode() => ((int)id * 397) ^ owner;
        }
        private sealed class LoopVoice
        {
            public AudioSource Source;
            public SoundDefinition Sound;
            public Object Owner;
            public float Gain, Target, Pitch = 1f;
            public Vector2? Position;
            public bool Paused;
        }
        private readonly Voice[] voices = new Voice[VoiceCount];
        private readonly Dictionary<Key, double> lastPlayed = new Dictionary<Key, double>();
        private readonly Dictionary<SoundId, int> lastClip = new Dictionary<SoundId, int>();
        private readonly Dictionary<Key, LoopVoice> loops = new Dictionary<Key, LoopVoice>();
        private readonly List<Key> expiredLoops = new List<Key>();
        private readonly System.Random random = new System.Random(7619);
        private PlayerController listener;
        private float nextListenerSearch;
        private AudioListener fallbackListener;
        private AudioClip click;
        private bool wasRewinding;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { current = null; quitting = false; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() { _ = Instance; }

        private void Awake() => InitializeService();

        private void InitializeService()
        {
            if (current != null && current != this) { Destroy(gameObject); return; }
            if (MusicSource != null) return;
            current = this;
            DontDestroyOnLoad(gameObject);
            if (Library == null) Library = Resources.Load<SoundLibrary>(SoundLibrary.ResourcePath);
            if (Library == null) Debug.LogError("缺少 Resources/Audio/GameplaySoundLibrary 音效配置。", this);
            MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicPreference, 0.45f));
            EffectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsPreference, 0.7f));
            MusicSource = NewSource("Music", true);
            MusicSource.loop = true;
            MusicSource.volume = MusicVolume;
            EffectsSource = NewSource("UI", true);
            EffectsSource.volume = EffectsVolume;
            for (int i = 0; i < voices.Length; i++)
                voices[i] = new Voice { Source = NewSource("SFX " + i, true) };
            SceneManager.sceneLoaded += SceneLoaded;
            EventManager.Subscribe<GameStageChanged>(StageChanged);
            RefreshListener();
        }

        private AudioSource NewSource(string label, bool ignorePause)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.ignoreListenerPause = ignorePause;
            return source;
        }

        public void ConfigureMenu(AudioClip music, AudioClip clickSound)
        {
            if (clickSound != null) click = clickSound;
            if (music != null && MusicSource.clip != music)
            {
                MusicSource.clip = music;
                MusicSource.Play();
            }
        }
        public void SetMusicVolume(float value)
        {
            MusicVolume = Mathf.Clamp01(value);
            MusicSource.volume = MusicVolume;
            PlayerPrefs.SetFloat(MusicPreference, MusicVolume);
        }
        public void SetEffectsVolume(float value)
        {
            EffectsVolume = Mathf.Clamp01(value);
            EffectsSource.volume = EffectsVolume;
            foreach (Voice voice in voices) if (voice.Sound != null) voice.Source.volume = voice.Gain * EffectsVolume;
            foreach (LoopVoice loop in loops.Values) loop.Source.volume = loop.Gain * loop.Sound.Volume * EffectsVolume;
            PlayerPrefs.SetFloat(EffectsPreference, EffectsVolume);
        }
        public void PlayClick()
        {
            if (click != null) PlayUIClip(click);
            else Play(SoundId.Click);
        }
        public void PlayUIClip(AudioClip clip)
        {
            if (clip != null) EffectsSource.PlayOneShot(clip);
        }
        public void SaveVolumes() => PlayerPrefs.Save();
        public void SetListener(PlayerController player) { listener = player; RefreshListener(); }

        private static bool Transitioning => WorldMapSceneRouter.IsLoading
            || (SceneResetManager.Existing != null && SceneResetManager.Existing.IsResetting)
            || (WorldMapProgression.Current != null && (WorldMapProgression.Current.IsTransitioning || WorldMapProgression.Current.IsComplete))
            || GameplayActionLocks.IsLocked(GameplayAction.Input, SceneManager.GetActiveScene());
        private static bool Frozen => PauseMenu.IsOpen || Time.timeScale <= 0f || Transitioning;
        public bool CanPlayGameplay => !Frozen && !RewindManager.Rewinding;

        public bool Play(SoundId id, Vector2? position = null, float gain = 1f, float pitch = 1f, Object owner = null)
        {
            SoundDefinition sound = Library != null ? Library.Find(id) : null;
            if (sound == null || sound.Loop || sound.Clips == null || sound.Clips.Length == 0
                || RewindManager.Rewinding && !sound.AllowDuringRewind || sound.PauseWithGameplay && Frozen) return false;
            float volume = sound.Volume * Mathf.Max(0f, gain) * (position.HasValue ? DistanceGain(position.Value) : 1f);
            if (volume <= 0.0001f) return false;
            Key key = new Key(id, owner);
            double now = Time.realtimeSinceStartupAsDouble;
            if (lastPlayed.TryGetValue(key, out double last) && now - last < sound.MinimumInterval) return false;
            Voice chosen = null;
            int same = 0;
            foreach (Voice voice in voices)
            {
                if (!voice.Source.isPlaying && !voice.Paused) { if (chosen == null) chosen = voice; }
                else if (voice.Sound == sound) same++;
            }
            if (same >= sound.MaxVoices) return false;
            if (chosen == null)
                foreach (Voice voice in voices)
                    if (voice.Sound.Priority <= sound.Priority && (chosen == null
                        || voice.Sound.Priority < chosen.Sound.Priority
                        || voice.Sound.Priority == chosen.Sound.Priority && voice.Started < chosen.Started)) chosen = voice;
            if (chosen == null) return false;
            AudioClip clip = PickClip(sound);
            if (clip == null) return false;
            chosen.Source.Stop();
            chosen.Source.clip = clip;
            chosen.Sound = sound;
            chosen.Paused = false;
            chosen.Gain = volume;
            chosen.Started = now;
            chosen.Source.volume = volume * EffectsVolume;
            chosen.Source.pitch = Mathf.Clamp(pitch * Mathf.Lerp(sound.Pitch.x, sound.Pitch.y, (float)random.NextDouble()), 0.25f, 3f);
            chosen.Source.panStereo = position.HasValue ? Pan(position.Value) : 0f;
            chosen.Source.Play();
            lastPlayed[key] = now;
            LastPlayed = id;
            PlayedCount++;
            return true;
        }

        private AudioClip PickClip(SoundDefinition sound)
        {
            int index = random.Next(sound.Clips.Length);
            if (sound.Clips.Length > 1 && lastClip.TryGetValue(sound.Id, out int previous) && index == previous)
                index = (index + 1) % sound.Clips.Length;
            lastClip[sound.Id] = index;
            return sound.Clips[index];
        }

        // Request every frame while active; omitted requests expire, so destroyed/disabled callers cannot leak loops.
        public void SetLoop(SoundId id, Object owner, bool active, Vector2? position = null, float gain = 1f, float pitch = 1f)
        {
            if (owner == null) return;
            Key key = new Key(id, owner);
            if (!loops.TryGetValue(key, out LoopVoice loop))
            {
                if (!active) return;
                SoundDefinition sound = Library != null ? Library.Find(id) : null;
                if (sound == null || !sound.Loop || sound.Clips.Length == 0) return;
                AudioClip clip = PickClip(sound);
                if (clip == null) return;
                loop = new LoopVoice { Owner = owner, Sound = sound, Source = NewSource(id.ToString(), true) };
                loop.Source.clip = clip;
                loop.Source.loop = true;
                loop.Source.volume = 0f;
                loop.Source.Play();
                loops.Add(key, loop);
            }
            loop.Target = active ? Mathf.Max(0f, gain) : 0f;
            loop.Position = position;
            loop.Pitch = Mathf.Clamp(pitch, 0.25f, 3f);
        }

        public void StopOwner(Object owner)
        {
            foreach (LoopVoice loop in loops.Values) if (loop.Owner == owner) loop.Target = 0f;
        }
        public void StopGameplay()
        {
            foreach (Voice voice in voices)
                if (voice.Sound != null && voice.Sound.PauseWithGameplay)
                { voice.Source.Stop(); voice.Paused = false; voice.Sound = null; }
            foreach (LoopVoice loop in loops.Values) { loop.Source.Stop(); Destroy(loop.Source.gameObject); }
            loops.Clear();
        }
        public void BeginRewindAudio()
        {
            if (wasRewinding) return;
            wasRewinding = true;
            StopGameplay();
            Play(SoundId.RewindStart);
        }
        public void EndRewindAudio()
        {
            if (!wasRewinding) return;
            wasRewinding = false;
            SetLoop(SoundId.RewindLoop, this, false);
            Play(SoundId.RewindEnd);
        }

        private void LateUpdate()
        {
            if (listener == null && Time.unscaledTime >= nextListenerSearch)
            { listener = FindObjectOfType<PlayerController>(); nextListenerSearch = Time.unscaledTime + 0.5f; }
            if (RewindManager.Rewinding) BeginRewindAudio(); else EndRewindAudio();
            bool frozen = Frozen;
            foreach (Voice voice in voices)
            {
                if (voice.Sound == null) continue;
                bool pause = voice.Sound.PauseWithGameplay && frozen;
                if (pause && !voice.Paused) { voice.Source.Pause(); voice.Paused = true; }
                else if (!pause && voice.Paused) { voice.Source.UnPause(); voice.Paused = false; }
                if (!voice.Paused && !voice.Source.isPlaying) voice.Sound = null;
            }
            RewindManager rewind = RewindManager.Current;
            bool rewinding = RewindManager.Rewinding;
            SetLoop(SoundId.RewindLoop, this, rewinding, null,
                rewind != null && rewind.AtOldestHistory ? 0.2f : 0.65f + (rewind != null ? rewind.RewindSpeedProgress * 0.35f : 0f),
                rewind != null ? Mathf.Lerp(0.85f, 1.3f, rewind.RewindSpeedProgress) : 1f);
            UpdateConveyors();
            expiredLoops.Clear();
            foreach (KeyValuePair<Key, LoopVoice> item in loops)
            {
                LoopVoice loop = item.Value;
                bool pause = loop.Sound.PauseWithGameplay && frozen;
                bool suppress = rewinding && !loop.Sound.AllowDuringRewind;
                if (loop.Owner == null || suppress || Transitioning) loop.Target = 0f;
                if (pause && !loop.Paused) { loop.Source.Pause(); loop.Paused = true; }
                else if (!pause && loop.Paused) { loop.Source.UnPause(); loop.Paused = false; }
                float target = loop.Target * (loop.Position.HasValue ? DistanceGain(loop.Position.Value) : 1f);
                loop.Gain = Mathf.MoveTowards(loop.Gain, target, Time.unscaledDeltaTime * 8f);
                loop.Source.volume = loop.Gain * loop.Sound.Volume * EffectsVolume;
                loop.Source.pitch = loop.Pitch;
                loop.Source.panStereo = loop.Position.HasValue ? Pan(loop.Position.Value) : 0f;
                if (loop.Gain <= 0.0001f && loop.Target <= 0f)
                { Destroy(loop.Source.gameObject); expiredLoops.Add(item.Key); }
                loop.Target = 0f;
            }
            foreach (Key key in expiredLoops) loops.Remove(key);
        }

        private void UpdateConveyors()
        {
            bool found = false;
            Vector2 nearest = default;
            float distance = float.PositiveInfinity, speed = 1f;
            if (listener != null && listener.Body != null && CanPlayGameplay)
            {
                Vector2 body = listener.Body.Motor.Position;
                foreach (WorldSurface surface in WorldSurface.ActiveSurfaces)
                {
                    if (surface == null || !surface.isActiveAndEnabled || Mathf.Approximately(surface.ConveyorSpeed, 0f)) continue;
                    RegionRevealView reveal = surface.GetComponentInParent<RegionRevealView>();
                    if (reveal != null && !reveal.Discovered) continue;
                    Collider2D collider = surface.GetComponent<Collider2D>();
                    if (collider == null || !collider.enabled) continue;
                    Vector2 point = collider.ClosestPoint(body);
                    float sqr = (point - body).sqrMagnitude;
                    if (sqr >= distance) continue;
                    distance = sqr; nearest = point; speed = Mathf.Abs(surface.ConveyorSpeed); found = true;
                }
            }
            // One aggregate nearby belt bed: adjacent tiles never multiply volume or source count.
            SetLoop(SoundId.ConveyorLoop, this, found && DistanceGain(nearest) > 0f, nearest, 1f,
                Mathf.Clamp(0.85f + speed * 0.06f / UnitSize, 0.85f, 1.2f));
        }
        private float UnitSize => listener != null && listener.Config != null ? Mathf.Max(0.01f, listener.Config.UnitSize) : 1f;
        private float DistanceGain(Vector2 position)
        {
            if (listener == null || listener.Body == null) return 1f;
            float distance = Vector2.Distance(position, listener.Body.Motor.Position) / UnitSize;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 14f, distance));
        }
        private float Pan(Vector2 position) => listener != null && listener.Body != null
            ? Mathf.Clamp((position.x - listener.Body.Motor.Position.x) / (14f * UnitSize), -0.65f, 0.65f) : 0f;

        private void StageChanged(GameStageChanged change)
        {
            if (change.Scene != SceneManager.GetActiveScene()) return;
            if (change.Stage == GameStage.Returning || change.Stage == GameStage.DoorOpened)
                Play(SoundId.PhaseChange, null, 0.8f, change.Stage == GameStage.Returning ? 0.8f : 1.1f);
        }
        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            StopGameplay();
            lastPlayed.Clear();
            listener = null;
            nextListenerSearch = 0f;
            wasRewinding = false;
            RefreshListener();
        }
        private void RefreshListener()
        {
            bool hasListener = false;
            foreach (AudioListener candidate in FindObjectsOfType<AudioListener>())
                if (candidate != fallbackListener && candidate.isActiveAndEnabled) { hasListener = true; break; }
            if (!hasListener && fallbackListener == null) fallbackListener = gameObject.AddComponent<AudioListener>();
            if (fallbackListener != null) fallbackListener.enabled = !hasListener;
        }
        private void OnApplicationPause(bool paused) { if (paused) SaveVolumes(); }
        private void OnApplicationQuit() { quitting = true; SaveVolumes(); }
        private void OnDisable() { if (MusicSource != null) StopGameplay(); }
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            EventManager.Unsubscribe<GameStageChanged>(StageChanged);
            if (current == this) current = null;
        }
    }
}
