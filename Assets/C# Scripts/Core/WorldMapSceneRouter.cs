using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap
{
    public sealed class WorldMapSceneRouter : LazySingleton<WorldMapSceneRouter>
    {
        private static WorldMapDefinition launchWorld;
        private static string launchModuleId;
        public static bool IsLoading => Existing != null && Existing.loading;
        private bool loading;
        public string LastError { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLaunch() { launchWorld = null; launchModuleId = null; }
        private void Awake() => DontDestroyOnLoad(gameObject);

        public static string PendingModule(WorldMapDefinition world) => launchWorld == world ? launchModuleId : null;
        public static string ConsumeModule(WorldMapDefinition world)
        {
            string id = PendingModule(world);
            if (launchWorld == world) { launchWorld = null; launchModuleId = null; }
            return id;
        }
        public bool Enter(WorldMapDefinition world, WorldMapModule module)
        {
            if (loading || !WorldMapProgressStore.IsUnlocked(world, module)) return false;
            launchWorld = world; launchModuleId = module.Id;
            if (Load(world.PlayScenePath, true)) return true;
            launchWorld = null; launchModuleId = null;
            return false;
        }
        public bool Load(string path) => Load(path, false);

        private bool Load(string path, bool newRun)
        {
            if (loading) return false;
            LastError = null;
            if (string.IsNullOrEmpty(path)) { LastError = "请先在大世界编辑器生成入口与游玩场景。"; return false; }
#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(path) == null)
            { LastError = "找不到场景：" + path; return false; }
#else
            if (!Application.CanStreamedLevelBeLoaded(path)) { LastError = "场景未加入构建列表：" + path; return false; }
#endif
            loading = true;
            AudioManager.Instance?.BeginMusicTransition(this);
            AudioManager.Existing?.StopGameplay();
            StartCoroutine(ChangeScene(path, newRun));
            return true;
        }
        private IEnumerator ChangeScene(string path, bool newRun)
        {
            ScreenFadeOverlay fade = ScreenFadeOverlay.Instance;
            float resumeScale = Time.timeScale;
            try
            {
                RewindManager.Current?.ClearHistory();
                if (EffectManager.Existing != null) EffectManager.Existing.ClearTransientEffects();
                resumeScale = Time.timeScale;
                Time.timeScale = 0f;
                yield return fade.FadeTo(1f, 0.25f);
                if (AudioManager.Existing != null) yield return AudioManager.Existing.WaitForMusicSilence();
                yield return null;
                AsyncOperation operation = null;
                try
                {
                    // Reset before the destination's Awake/OnEnable can restore keys, doors or cues.
                    if (newRun) GameStageProgression.BeginNewRun(path);
#if UNITY_EDITOR
                    operation = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path,
                        new LoadSceneParameters(LoadSceneMode.Single));
#else
                    operation = SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
#endif
                }
                catch (Exception exception) { LastError = exception.Message; Debug.LogException(exception); }
                if (operation != null) yield return operation;
                else { launchWorld = null; launchModuleId = null; }
                yield return null;
                yield return null;
                AudioManager.Existing?.EndMusicTransition(this);
                yield return fade.FadeTo(0f, 0.35f);
            }
            finally { AudioManager.Existing?.EndMusicTransition(this); Time.timeScale = resumeScale; fade.SetOpacity(0f); loading = false; }
        }
    }
}
