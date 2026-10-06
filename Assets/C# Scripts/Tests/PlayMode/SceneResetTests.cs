#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TapTap.Tests
{
    public sealed class SceneResetTestSceneSetup : IPrebuildSetup, IPostBuildCleanup
    {
        public const string Folder = "Assets/__SceneResetTests";
        public const string ScenePath = Folder + "/ReloadFixture.unity";

        public void Setup()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__SceneResetTests");
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                new GameObject("Reset marker");
                var effects = new GameObject("Effects");
                effects.AddComponent<EffectManager>();
                effects.AddComponent<WorldPhaseState>();
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save reset fixture.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        public void Cleanup() => AssetDatabase.DeleteAsset(Folder);
    }

    [PrebuildSetup(typeof(SceneResetTestSceneSetup)), PostBuildCleanup(typeof(SceneResetTestSceneSetup))]
    public sealed class SceneResetTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            ScreenFadeOverlay.Instance.SetOpacity(0f);
            Scene fixture = SceneManager.GetSceneByPath(SceneResetTestSceneSetup.ScenePath);
            if (fixture.IsValid() && fixture.isLoaded)
            {
                Scene cleanup = SceneManager.CreateScene("Reset test cleanup");
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(fixture);
            }
        }

        [UnityTest]
        public IEnumerator ResetReloadsUnlistedSceneUnderBlackAndRestoresItsInitialState()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(SceneResetTestSceneSetup.ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            Scene fixture = SceneManager.GetSceneByPath(SceneResetTestSceneSetup.ScenePath);
            SceneManager.SetActiveScene(fixture);
            Assert.That(fixture.buildIndex, Is.EqualTo(-1), "The editor reset must work without adding test scenes to Build Settings.");
            GameObject marker = fixture.GetRootGameObjects().Single(item => item.name == "Reset marker");
            WorldPhaseState phase = fixture.GetRootGameObjects().Select(item => item.GetComponent<WorldPhaseState>()).First(item => item != null);
            marker.transform.position = new Vector3(4f, 6f, 0f);
            phase.SetActive(true);
            var manager = SceneResetManager.Instance;
            var overlay = ScreenFadeOverlay.Instance;
            int loads = 0;
            void Loaded(Scene scene, LoadSceneMode mode)
            {
                if (scene.path != SceneResetTestSceneSetup.ScenePath) return;
                loads++;
                Assert.That(overlay.Opacity, Is.EqualTo(1f), "New scene initialization must remain hidden by the persistent blackout.");
            }
            SceneManager.sceneLoaded += Loaded;
            try
            {
                Time.timeScale = 0f;
                Assert.That(manager.ResetCurrentScene(), Is.True);
                Assert.That(manager.ResetCurrentScene(), Is.False, "Repeated R presses must not schedule another reload.");
                yield return WaitForReset(manager);
                Assert.That(loads, Is.EqualTo(1));
                Assert.That(marker == null, Is.True, "The old scene must be replaced, rather than moving only the player.");
                Scene reloaded = SceneManager.GetActiveScene();
                GameObject restored = reloaded.GetRootGameObjects().Single(item => item.name == "Reset marker");
                Assert.That(restored.transform.position, Is.EqualTo(Vector3.zero));
                WorldPhaseState restoredPhase = reloaded.GetRootGameObjects().Select(item => item.GetComponent<WorldPhaseState>()).First(item => item != null);
                Assert.That(restoredPhase.Active, Is.False);
                Assert.That(overlay.Opacity, Is.EqualTo(0f));
                Assert.That(Time.timeScale, Is.EqualTo(1f));
                Assert.That(SceneResetManager.Instance, Is.SameAs(manager));
                Assert.That(ScreenFadeOverlay.Instance, Is.SameAs(overlay));

                Assert.That(manager.ResetCurrentScene(), Is.True);
                yield return WaitForReset(manager);
                Assert.That(loads, Is.EqualTo(2));
                Assert.That(Object.FindObjectsOfType<SceneResetManager>().Length, Is.EqualTo(1));
                Assert.That(Object.FindObjectsOfType<ScreenFadeOverlay>().Length, Is.EqualTo(1));
            }
            finally { SceneManager.sceneLoaded -= Loaded; }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator EffectManagerBlackoutFadesWhileGameTimeIsPaused()
        {
            EffectManager effects = EffectManager.Instance;
            Time.timeScale = 0f;
            effects.BlackScreen();
            Assert.That(ScreenFadeOverlay.Instance.Opacity, Is.EqualTo(1f));
            yield return effects.FadeFromBlack();
            Assert.That(ScreenFadeOverlay.Instance.Opacity, Is.EqualTo(0f));
            yield return effects.FadeToBlack();
            Assert.That(ScreenFadeOverlay.Instance.Opacity, Is.EqualTo(1f));
            effects.ClearBlackScreen();
            Assert.That(ScreenFadeOverlay.Instance.Opacity, Is.EqualTo(0f));
            LogAssert.NoUnexpectedReceived();
        }

        private static IEnumerator WaitForReset(SceneResetManager manager)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (manager.IsResetting && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(manager.IsResetting, Is.False, "Reset must finish even when Time.timeScale is zero.");
        }
    }
}
#endif
