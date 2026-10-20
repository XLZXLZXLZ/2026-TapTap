using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TapTap.Editor
{
    [InitializeOnLoad]
    public static class ReleaseRegressionTests
    {
        private const string Pending = "TapTap.ReleaseRegression.Pending";
        static ReleaseRegressionTests()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                    EditorApplication.delayCall += Verify;
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch mode only.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        private static void Verify()
        {
            SessionState.SetBool(Pending, false);
            try
            {
                typeof(RewindRegressionTests).GetMethod("BulletTimeFeel", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, null);
                Debug.Log("RELEASE REGRESSION PASS: bullet toggle, normal speed restoration, tint, transient slowdown, pause and scene change.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
