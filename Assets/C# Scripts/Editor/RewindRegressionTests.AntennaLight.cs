using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static partial class RewindRegressionTests
    {
        private const string AntennaPending = "TapTap.AntennaLight.Pending";
        private const string AntennaMaterials = "Assets/Art/3D Resources/Player/New/Materials/";

        // Run only in an isolated project: -executeMethod TapTap.Editor.RewindRegressionTests.RunAntennaLightBatch
        public static void RunAntennaLightBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project.");
            PlayerModelAssets.UpdateAntennaLight();
            ValidateAntennaPrefab();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/AntennaLightRegressionScene.unity");
            SessionState.SetBool(AntennaPending, true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void SubscribeAntennaValidation()
        {
            EditorApplication.playModeStateChanged -= OnAntennaPlayMode;
            EditorApplication.playModeStateChanged += OnAntennaPlayMode;
        }

        private static void OnAntennaPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(AntennaPending, false)) return;
            SessionState.SetBool(AntennaPending, false);
            EditorApplication.delayCall += () =>
            {
                try
                {
                    AntennaStateChanges();
                    File.WriteAllText(Path.Combine(Application.dataPath, "../antenna-light-results.txt"),
                        "PASS: Prefab bulb owns its listener and two HDR URP/Unlit materials.\n" +
                        "PASS: Magnetic connection stays yellow in every phase; only Detached is red.\n" +
                        "PASS: Snapshot restoration, rewind, re-enable and teleport restore the matching color.\n");
                    EditorApplication.Exit(0);
                }
                catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
            };
        }

        private static void ValidateAntennaPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerModelAssets.PlayerPath);
            try
            {
                var player = root.GetComponent<PlayerController>();
                PlayerAntennaLight[] lights = root.GetComponentsInChildren<PlayerAntennaLight>(true);
                Check(lights.Length == 1 && lights[0].name == "ImportedLight_AntennaYellowBall",
                    "State listener must live only on the antenna bulb");
                var serialized = new SerializedObject(lights[0]);
                Check(serialized.FindProperty("controller").objectReferenceValue == player, "Bulb references another player");
                Material joined = (Material)serialized.FindProperty("joinedMaterial").objectReferenceValue;
                Material separated = (Material)serialized.FindProperty("separatedMaterial").objectReferenceValue;
                Check(joined != null && separated != null && joined != separated, "Bulb needs two distinct materials");
                Check(joined.shader.name == "Universal Render Pipeline/Unlit" && separated.shader == joined.shader,
                    "Bulb materials must both use URP/Unlit");
                Color yellow = joined.GetColor("_BaseColor"), red = separated.GetColor("_BaseColor");
                Check(yellow.r > 1f && yellow.g > 1f && yellow.b < 0.1f, "Joined bulb is not fluorescent yellow");
                Check(red.r > 1f && red.g < 0.1f && red.b < 0.1f, "Separated bulb is not fluorescent red");
                Check(lights[0].GetComponent<Renderer>().sharedMaterial == joined, "Default prefab bulb is not yellow");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void AntennaStateChanges()
        {
            using (var f = new Fixture())
            {
                f.Floor(); f.CreatePlayer();
                Material joined = AssetDatabase.LoadAssetAtPath<Material>(AntennaMaterials + "AntennaLight.mat");
                Material separated = AssetDatabase.LoadAssetAtPath<Material>(AntennaMaterials + "AntennaLightSeparated.mat");
                GameObject bulb = f.NewObject("Bulb", Origin);
                Renderer renderer = bulb.AddComponent<MeshRenderer>();
                var light = bulb.AddComponent<PlayerAntennaLight>();
                light.Configure(f.Player, joined, separated);
                Check(renderer.sharedMaterial == joined, "Initial joined state is not yellow");
                f.Step(2);
                f.Input.SetExternalInput(0f, true); f.Step(3);
                Check(f.Player.Phase == PlayerPhase.Extending && renderer.sharedMaterial == joined,
                    "Magnetically connected extension must keep the bulb yellow");
                f.Back(3); light.RefreshState();
                Check(f.Player.Phase == PlayerPhase.Joined && renderer.sharedMaterial == joined,
                    "Rewind to joined did not restore yellow");
                f.Manager.EndRewind();
                foreach (PlayerPhase phase in Enum.GetValues(typeof(PlayerPhase)))
                {
                    PlayerController.Snapshot snapshot = f.Player.CaptureState();
                    snapshot.Phase = phase;
                    f.Player.RestoreState(in snapshot);
                    light.RefreshState();
                    Check(renderer.sharedMaterial == (phase == PlayerPhase.Detached ? separated : joined),
                        "Restored phase has the wrong bulb color: " + phase);
                }
                f.Player.TeleportJoined(f.Spawn);
                Check(renderer.sharedMaterial == joined, "Teleport join event did not restore yellow");
                light.enabled = false;
                f.Detach(f.Spawn, f.Spawn + Vector2.up * 3f);
                light.enabled = true;
                Check(renderer.sharedMaterial == separated, "Re-enabled bulb did not synchronize current state");
                f.Player.TeleportJoined(f.Spawn);
                Check(renderer.sharedMaterial == joined, "Re-enabled bulb lost its event subscription");
            }
        }
    }
}
