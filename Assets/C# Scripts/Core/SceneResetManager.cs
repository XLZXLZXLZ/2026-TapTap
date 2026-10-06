using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap
{
    public sealed class SceneResetManager : LazySingleton<SceneResetManager>
    {
        public bool IsResetting { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize() => Instance.enabled = true;

        private void Awake() => DontDestroyOnLoad(gameObject);

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R)) ResetCurrentScene();
        }

        public bool ResetCurrentScene()
        {
            if (IsResetting) return false;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                Debug.LogWarning("当前场景尚未保存，无法重新加载。请先保存场景再按 R。");
                return false;
            }
#if !UNITY_EDITOR
            if (scene.buildIndex < 0)
            {
                Debug.LogWarning("当前场景未加入构建列表，无法重新加载。");
                return false;
            }
#endif
            IsResetting = true;
            StartCoroutine(Reload(scene));
            return true;
        }

        private IEnumerator Reload(Scene scene)
        {
            ScreenFadeOverlay overlay = ScreenFadeOverlay.Instance;
            try
            {
                yield return EffectManager.Instance.FadeToBlack();
                // Present an opaque frame before replacing the scene.
                yield return null;
                Time.timeScale = 1f;
                AsyncOperation loading = LoadScene(scene);
                if (loading != null)
                {
                    yield return loading;
                    Time.timeScale = 1f;
                    // Let the new scene initialize while the screen remains black.
                    yield return null;
                }
                yield return EffectManager.Instance.FadeFromBlack();
            }
            finally
            {
                if (overlay != null) overlay.SetOpacity(0f);
                IsResetting = false;
            }
        }

        private static AsyncOperation LoadScene(Scene scene)
        {
            try
            {
#if UNITY_EDITOR
                if (scene.buildIndex < 0)
                    return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                        scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#endif
                return SceneManager.LoadSceneAsync(scene.buildIndex, LoadSceneMode.Single);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return null;
            }
        }
    }
}
