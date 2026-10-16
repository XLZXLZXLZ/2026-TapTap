using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static partial class RewindRegressionTests
    {
        public static void RunPlayerBlinkBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project.");
            try
            {
                Dictionary<string, string> placement = PlayerVisualPlacement();
                PlayerModelAssets.ReplacePlayerModels();
                Dictionary<string, string> updated = PlayerVisualPlacement();
                foreach (var pair in placement)
                    Check(updated.ContainsKey(pair.Key) && updated[pair.Key] == pair.Value,
                        "Model replacement changed authored placement: " + pair.Key);
                // The current prefab has designer-authored scale and offsets;
                // validate their preservation instead of the old auto-fit silhouette.
                ValidateSkinnedAnimation();
                ValidatePlayerBlink();
                ValidateExpressionPrefab();
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) RenderPlayerBlink();
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) RenderPlayerExpressions();
                RunBatch();
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(Application.dataPath, "../player-blink-results.txt"), exception.ToString());
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static Dictionary<string, string> PlayerVisualPlacement()
        {
            var result = new Dictionary<string, string>();
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerModelAssets.PlayerPath);
            try
            {
                var player = root.GetComponent<PlayerController>();
                foreach (MovableEntity entity in new[] { player.Body, player.Head })
                {
                    foreach (Transform part in new[] { entity.transform, entity.Visual,
                        entity.Visual.Find("Facing"), entity.Visual.Find("Facing/Model Fit") })
                    {
                        if (part == null) continue;
                        result.Add(entity.name + "/" + part.name,
                            part.localPosition.ToString("R") + part.localRotation.ToString("R") + part.localScale.ToString("R")
                            + new SerializedObject(part).FindProperty("m_ConstrainProportionsScale").boolValue);
                    }
                }
                return result;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ValidatePlayerBlink()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerModelAssets.PlayerPath);
            try
            {
                PlayerController player = root.GetComponent<PlayerController>();
                PlayerBlinkView blink = root.GetComponent<PlayerBlinkView>();
                Check(blink != null, "Player prefab lacks blink presentation");
                var eyes = new MeshRenderer[4];
                var openHeights = new float[4];
                var localPositions = new Vector3[4];
                int i = 0;
                foreach (MovableEntity entity in new[] { player.Body, player.Head })
                {
                    Transform normal = Array.Find(entity.Visual.GetComponentsInChildren<Transform>(true),
                        t => t.name == (entity == player.Body ? "Blue Eyes" : "Red Eyes"));
                    Check(normal != null, "Missing ordinary eye group");
                    MeshRenderer[] group = normal.GetComponentsInChildren<MeshRenderer>(true);
                    Check(group.Length == 2, entity.name + " must have exactly two clean eye meshes");
                    foreach (MeshRenderer eye in group)
                    {
                        Transform bone = Array.Find(entity.Visual.GetComponentsInChildren<Transform>(true),
                            t => t.name == (entity == player.Body ? "Lower" : "Upper"));
                        Check(eye.transform.IsChildOf(bone),
                            "Eye group does not follow its live body bone");
                        eyes[i] = eye;
                        openHeights[i] = eye.bounds.size.y;
                        localPositions[i] = eye.transform.localPosition;
                        Check(openHeights[i] > 0.01f, "Eye is not visibly open");
                        i++;
                    }
                }
                blink.ResetBlink();
                blink.Simulate(4.9f);
                for (i = 0; i < 4; i++) Check(Mathf.Abs(eyes[i].bounds.size.y - openHeights[i]) < 0.0001f,
                    "Eyes blinked before 5s");
                blink.Simulate(0.1f + 4f / 24f);
                var ratios = new float[4];
                for (i = 0; i < 4; i++)
                {
                    ratios[i] = eyes[i].bounds.size.y / openHeights[i];
                    Check(ratios[i] < 0.3f, "Blink curves do not close actual eye geometry: " + ratios[i]);
                    Check(Vector3.Distance(eyes[i].transform.localPosition, localPositions[i]) < 0.0001f,
                        "Blink moved eyes away from their sockets");
                }
                blink.Simulate(0.3f);
                for (i = 0; i < 4; i++) Check(Mathf.Abs(eyes[i].bounds.size.y - openHeights[i]) < 0.0001f,
                    "Eyes did not return to their authored open pose");
                blink.ResetBlink();
                blink.Simulate(10f + 4f / 24f);
                for (i = 0; i < 4; i++) Check(eyes[i].bounds.size.y / openHeights[i] < 0.3f,
                    "Second blink is not scheduled at 10s");
                AnimationClip walk = PlayerModelAssets.AnimationClipAt(PlayerModelAssets.WalkPath);
                foreach (MovableEntity entity in new[] { player.Body, player.Head })
                {
                    var animator = entity.Visual.GetComponentInChildren<Animator>();
                    walk.SampleAnimation(animator.gameObject, walk.length * 0.35f);
                }
                blink.ResetBlink();
                for (i = 0; i < 4; i++) Check(Vector3.Distance(eyes[i].transform.localPosition, localPositions[i]) < 0.0001f,
                    "Walk overwrote eye placement");
                File.WriteAllText(Path.Combine(Application.dataPath, "../player-blink-results.txt"),
                    "PASS: Four clean eyes attached to Upper/Lower; original Idle/Walk binding and skin deformation validated.\n"
                    + "PASS: No blink before 5s, eye geometry closes at 5s and 10s, open pose restored.\n"
                    + "PASS: Blink changes scale only; walk and blink retain eye socket placement.\n"
                    + "Closed/open height ratios: " + string.Join(", ", ratios) + "\n");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void RenderPlayerBlink()
        {
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Camera camera = new GameObject("Blink preview").AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1.1f;
            camera.transform.position = new Vector3(0f, 0.9f, -8f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.15f, 0.19f);
            Light light = new GameObject("Preview light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
            AnimationClip walk = PlayerModelAssets.AnimationClipAt(PlayerModelAssets.WalkPath);
            for (int i = 0; i < 3; i++)
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelAssets.PlayerPath), scene);
                root.transform.position = Vector3.right * ((i - 1) * 1.4f);
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    if (renderer is SpriteRenderer || renderer is LineRenderer || renderer is ParticleSystemRenderer)
                        renderer.enabled = false;
                PlayerController player = root.GetComponent<PlayerController>();
                if (i == 2)
                    foreach (MovableEntity entity in new[] { player.Body, player.Head })
                    {
                        entity.Visual.Find("Facing").localRotation = Quaternion.Euler(0f, 120f, 0f);
                        walk.SampleAnimation(entity.Visual.GetComponentInChildren<Animator>().gameObject, walk.length * 0.35f);
                    }
                var blink = root.GetComponent<PlayerBlinkView>();
                blink.ResetBlink();
                if (i == 1) blink.Simulate(5f + 4f / 24f);
            }
            var target = new RenderTexture(1440, 720, 24);
            var pixels = new Texture2D(1440, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(Application.dataPath, "../player-blink-preview.png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous; target.Release();
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
                ShaderUtil.allowAsyncCompilation = previousAsync;
            }
        }
    }
}
