using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap
{
    public sealed partial class AudioManager
    {
        public BackgroundMusicLibrary MusicLibrary;
        public MusicTrack? CurrentMusic { get; private set; }
        public bool IsMusicPaused { get; private set; } = true;
        public bool HasPendingEndingMusic => endingMusicOwner != null;

        private sealed class MusicVoice
        {
            public AudioSource Source;
            public float Gain, From, Target, Elapsed, Duration;
            public bool Paused;
        }

        private readonly MusicVoice[] musicVoices = new MusicVoice[2];
        private readonly HashSet<MusicTrack> warnedMusic = new HashSet<MusicTrack>();
        private AudioClip legacyMenuMusic;
        private int keyMusicScene = -1;
        private EndingSequenceController endingMusicOwner;
        private double endingMusicAt;
        private readonly HashSet<Object> musicTransitions = new HashSet<Object>();
        private bool musicPending, musicGapRequired;
        private MusicTrack? pendingMusic;
        private double musicSilentAt = -1d;

        private bool MusicSilent
        {
            get
            {
                foreach (MusicVoice voice in musicVoices)
                    if (voice.Gain > 0f) return false;
                return true;
            }
        }
        private float MusicSilenceDuration => MusicLibrary != null ? Mathf.Max(0f, MusicLibrary.SilenceDuration) : 0.35f;

        // Multiple covers may hand off the same scene change (escape -> router).
        public void BeginMusicTransition(Object owner)
        {
            if (owner == null || !musicTransitions.Add(owner)) return;
            if (musicTransitions.Count == 1)
            {
                if (!musicPending) pendingMusic = CurrentMusic;
                musicPending = musicGapRequired = true;
                CurrentMusic = null;
                IsMusicPaused = true;
                endingMusicOwner = null;
                foreach (MusicVoice voice in musicVoices) FadeMusic(voice, 0f);
            }
        }

        public void EndMusicTransition(Object owner) => musicTransitions.Remove(owner);
        public bool HasMusicTransition(Object owner) => musicTransitions.Contains(owner);

        public IEnumerator WaitForMusicSilence()
        {
            while (!MusicSilent || musicSilentAt < 0d
                || Time.realtimeSinceStartupAsDouble - musicSilentAt < MusicSilenceDuration) yield return null;
        }

        public bool LevelMusicChanges(Scene scene, LevelDefinition level, int number)
            => ResolveLevelMusic(scene, level, number) != CurrentMusic;

        private MusicTrack? ResolveLevelMusic(Scene scene, LevelDefinition level, int number)
            => keyMusicScene == scene.handle || GameStageProgression.StageIn(scene) >= GameStage.CarryingKey
                ? MusicTrack.KeyEscape : MusicLibrary != null ? MusicLibrary.TrackFor(level != null ? level.Style : null, number) : null;

        private void InitializeMusic()
        {
            if (MusicLibrary == null) MusicLibrary = Resources.Load<BackgroundMusicLibrary>(BackgroundMusicLibrary.ResourcePath);
            for (int index = 0; index < musicVoices.Length; index++)
            {
                AudioSource source = NewSource("Music " + (index + 1), true);
                source.loop = true;
                source.volume = 0f;
                musicVoices[index] = new MusicVoice { Source = source };
            }
            MusicSource = musicVoices[0].Source;
        }

        private AudioClip MusicClip(MusicTrack track)
        {
            AudioClip clip = MusicLibrary != null ? MusicLibrary.ClipFor(track) : null;
            if (clip == null && track == MusicTrack.Menu)
                clip = Library != null && Library.BackgroundMusic != null ? Library.BackgroundMusic : legacyMenuMusic;
            return clip;
        }

        // Repeated requests for the same target preserve both the playhead and an in-progress fade.
        private void PlayMusic(MusicTrack? track)
        {
            if (musicPending && pendingMusic == track) return;
            if (!musicPending && musicTransitions.Count == 0 && CurrentMusic == track
                && MusicSource.clip == (track.HasValue ? MusicClip(track.Value) : null)) return;
            pendingMusic = track;
            musicPending = true;
            musicGapRequired |= !MusicSilent;
            CurrentMusic = track;
            IsMusicPaused = true;
            foreach (MusicVoice voice in musicVoices) FadeMusic(voice, 0f);
        }

        private void StartMusic(MusicTrack? track)
        {
            AudioClip clip = track.HasValue ? MusicClip(track.Value) : null;
            if (track.HasValue && clip == null && warnedMusic.Add(track.Value))
                Debug.LogWarning("BGM_" + (int)track.Value + " 尚未配置音频，将淡出并保持静音。请配置 Resources/Audio/BackgroundMusicLibrary。", this);
            CurrentMusic = track;
            IsMusicPaused = clip == null;
            MusicVoice selected = null;
            if (clip != null)
            {
                foreach (MusicVoice voice in musicVoices)
                    if (voice.Source.clip == clip) { selected = voice; break; }
                if (selected == null)
                {
                    // Both voices are silent here; retain the previous track's paused playhead for a quick return.
                    selected = musicVoices[0].Source == MusicSource ? musicVoices[1] : musicVoices[0];
                    selected.Source.Stop();
                    selected.Source.clip = clip;
                    selected.Gain = 0f;
                    selected.Target = -1f;
                    selected.Source.volume = 0f;
                    selected.Paused = false;
                }
                if (selected.Paused) { selected.Source.UnPause(); selected.Paused = false; }
                else if (!selected.Source.isPlaying) selected.Source.Play();
                MusicSource = selected.Source;
            }
            foreach (MusicVoice voice in musicVoices)
                FadeMusic(voice, voice == selected ? 1f : 0f);
        }

        private void FadeMusic(MusicVoice voice, float target)
        {
            if (voice.Target == target) return;
            voice.From = voice.Gain;
            voice.Target = target;
            voice.Elapsed = 0f;
            voice.Duration = MusicLibrary != null
                ? Mathf.Max(0f, target > voice.Gain ? MusicLibrary.FadeInDuration : MusicLibrary.FadeOutDuration) : 2f;
            if (voice.Duration <= 0f) voice.Gain = target;
        }

        private void UpdateMusic()
        {
            if (endingMusicOwner != null)
            {
                if (!endingMusicOwner.isActiveAndEnabled || endingMusicOwner.gameObject.scene != SceneManager.GetActiveScene())
                    endingMusicOwner = null;
                else if (Time.realtimeSinceStartupAsDouble >= endingMusicAt)
                {
                    endingMusicOwner = null;
                    PlayMusic(MusicTrack.Ending);
                }
            }
            foreach (MusicVoice voice in musicVoices)
            {
                voice.Elapsed += Time.unscaledDeltaTime;
                voice.Gain = voice.Duration <= 0f ? voice.Target
                    : Mathf.Lerp(voice.From, voice.Target, Mathf.SmoothStep(0f, 1f, voice.Elapsed / voice.Duration));
                if (voice.Target == 0f && voice.Gain <= 0f && voice.Source.clip != null && !voice.Paused)
                {
                    voice.Source.Pause();
                    voice.Paused = true;
                }
            }
            ApplyMusicVolumes();
            if (MusicSilent)
            {
                if (musicSilentAt < 0d) musicSilentAt = Time.realtimeSinceStartupAsDouble;
                if (musicPending && musicTransitions.Count == 0
                    && (!musicGapRequired || Time.realtimeSinceStartupAsDouble - musicSilentAt >= MusicSilenceDuration))
                {
                    musicPending = musicGapRequired = false;
                    StartMusic(pendingMusic);
                }
            }
            else musicSilentAt = -1d;
        }

        private void ApplyMusicVolumes()
        {
            foreach (MusicVoice voice in musicVoices)
                if (voice != null) voice.Source.volume = voice.Gain * MusicVolume;
        }

        public void ApplyLevelMusic(Scene scene, LevelDefinition level, int sequenceNumber)
        {
            if (scene != SceneManager.GetActiveScene()) return;
            endingMusicOwner = null;
            if (keyMusicScene == scene.handle || GameStageProgression.StageIn(scene) >= GameStage.CarryingKey)
            {
                PlayKeyMusic(scene);
                return;
            }
            PlayMusic(ResolveLevelMusic(scene, level, sequenceNumber));
        }

        public void PlayKeyMusic(Scene scene)
        {
            if (scene != SceneManager.GetActiveScene()) return;
            keyMusicScene = scene.handle;
            endingMusicOwner = null;
            PlayMusic(MusicTrack.KeyEscape);
        }

        public void BeginEndingMusic(EndingSequenceController owner)
        {
            if (owner == null || !owner.isActiveAndEnabled || owner.gameObject.scene != SceneManager.GetActiveScene()
                || owner.State != EndingState.Broadcast || endingMusicOwner == owner || CurrentMusic == MusicTrack.Ending) return;
            PlayMusic(null);
            endingMusicOwner = owner;
            endingMusicAt = Time.realtimeSinceStartupAsDouble + (MusicLibrary != null ? Mathf.Max(0f, MusicLibrary.EndingMusicDelay) : 1f);
        }

        public void CancelPendingEndingMusic(EndingSequenceController owner)
        {
            if (endingMusicOwner == owner) endingMusicOwner = null;
        }

        private void RefreshSceneMusic(Scene scene)
        {
            // Includes reloads: a new run must not inherit the previous run's pickup latch or delayed ending cue.
            keyMusicScene = -1;
            endingMusicOwner = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<EndingSequenceController>(true) != null) { PlayMusic(null); return; }
                if (root.GetComponentInChildren<MainMenuController>(true) != null
                    || root.GetComponentInChildren<WorldLevelMenu>(true) != null) { PlayMusic(MusicTrack.Menu); return; }
            }
            if (GameStageProgression.StageIn(scene) >= GameStage.CarryingKey) { PlayKeyMusic(scene); return; }
            WorldMapProgression progression = WorldMapProgression.Current;
            if (progression != null && progression.gameObject.scene == scene && progression.CurrentModule != null)
                ApplyLevelMusic(scene, progression.CurrentModule.Level, progression.CurrentNumber);
            else PlayMusic(null); // Gameplay initialization selects its actual entry level, without a menu-music blip.
        }
    }
}
