using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap
{
    // One persistent player: moving between scenes with the prefab never doubles the BGM.
    public sealed class MenuAudio : MonoBehaviour
    {
        public const string MusicPreference = "TapTap.Audio.Music";
        public const string EffectsPreference = "TapTap.Audio.Effects";
        public static MenuAudio Current { get; private set; }
        public float MusicVolume { get; private set; }
        public float EffectsVolume { get; private set; }
        public AudioSource MusicSource { get; private set; }
        public AudioSource EffectsSource { get; private set; }
        private AudioClip click;
        private AudioListener fallbackListener;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;

        public static MenuAudio Ensure(AudioClip music, AudioClip clickSound)
        {
            if (Current == null) new GameObject("Menu Audio").AddComponent<MenuAudio>();
            Current.click = clickSound;
            if (music != null && Current.MusicSource.clip != music)
            {
                Current.MusicSource.clip = music;
                Current.MusicSource.Play();
            }
            return Current;
        }

        private void Awake()
        {
            if (Current != null && Current != this) { Destroy(gameObject); return; }
            Current = this;
            DontDestroyOnLoad(gameObject);
            MusicSource = gameObject.AddComponent<AudioSource>();
            EffectsSource = gameObject.AddComponent<AudioSource>();
            MusicSource.playOnAwake = EffectsSource.playOnAwake = false;
            MusicSource.spatialBlend = EffectsSource.spatialBlend = 0f;
            MusicSource.ignoreListenerPause = EffectsSource.ignoreListenerPause = true;
            MusicSource.loop = true;
            MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicPreference, 0.45f));
            EffectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsPreference, 0.7f));
            MusicSource.volume = MusicVolume;
            EffectsSource.volume = EffectsVolume;
            SceneManager.sceneLoaded += SceneLoaded;
            RefreshListener();
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode) => RefreshListener();

        private void RefreshListener()
        {
            bool hasListener = false;
            foreach (AudioListener listener in FindObjectsOfType<AudioListener>())
                if (listener != fallbackListener && listener.isActiveAndEnabled) { hasListener = true; break; }
            if (!hasListener && fallbackListener == null) fallbackListener = gameObject.AddComponent<AudioListener>();
            if (fallbackListener != null) fallbackListener.enabled = !hasListener;
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
            PlayerPrefs.SetFloat(EffectsPreference, EffectsVolume);
        }

        public void PlayClick() => PlayEffect(click);
        public void PlayEffect(AudioClip clip)
        {
            if (clip != null) EffectsSource.PlayOneShot(clip);
        }

        public void SaveVolumes() => PlayerPrefs.Save();
        private void OnApplicationPause(bool paused) { if (paused) SaveVolumes(); }
        private void OnApplicationQuit() => SaveVolumes();
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            if (Current == this) Current = null;
        }
    }
}
