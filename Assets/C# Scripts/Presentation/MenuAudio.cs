using UnityEngine;

namespace TapTap
{
    // Compatibility facade: AudioManager owns all sources, preferences and scene lifetime.
    public sealed class MenuAudio : MonoBehaviour
    {
        public const string MusicPreference = AudioManager.MusicPreference;
        public const string EffectsPreference = AudioManager.EffectsPreference;
        public static MenuAudio Current { get; private set; }
        public float MusicVolume => AudioManager.Instance.MusicVolume;
        public float EffectsVolume => AudioManager.Instance.EffectsVolume;
        public AudioSource MusicSource => AudioManager.Instance.MusicSource;
        public AudioSource EffectsSource => AudioManager.Instance.EffectsSource;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
        public static MenuAudio Ensure(AudioClip music, AudioClip clickSound)
        {
            if (Current == null) new GameObject("Menu Audio (Compatibility)").AddComponent<MenuAudio>();
            AudioManager.Instance.ConfigureMenu(music, clickSound);
            return Current;
        }
        private void Awake()
        {
            if (Current != null && Current != this) { Destroy(gameObject); return; }
            Current = this;
            DontDestroyOnLoad(gameObject);
        }
        public void SetMusicVolume(float value) => AudioManager.Instance.SetMusicVolume(value);
        public void SetEffectsVolume(float value) => AudioManager.Instance.SetEffectsVolume(value);
        public void PlayClick() => AudioManager.Instance.PlayClick();
        public void PlayEffect(AudioClip clip) => AudioManager.Instance.PlayUIClip(clip);
        public void SaveVolumes() => AudioManager.Instance.SaveVolumes();
        private void OnDestroy() { if (Current == this) Current = null; }
    }
}
