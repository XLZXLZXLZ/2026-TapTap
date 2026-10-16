using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static partial class RewindRegressionTests
    {
        private static void ValidateExpressionPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerModelAssets.PlayerPath);
            try
            {
                Check(root.GetComponent<PlayerExpressionView>() != null, "Player lacks magnetic expressions");
                var player = root.GetComponent<PlayerController>();
                foreach (MovableEntity entity in new[] { player.Body, player.Head })
                {
                    foreach (string name in new[] { "Normal Eyes Pivot", "Happy Eyes Pivot", "Sad Eyes Pivot" })
                    {
                        Transform pivot = Array.Find(entity.Visual.GetComponentsInChildren<Transform>(true), t => t.name == name);
                        Check(pivot != null && pivot.parent.name == (entity == player.Body ? "Lower" : "Upper"),
                            "Expression does not follow the matching body bone");
                        Check(pivot.GetComponentsInChildren<Renderer>(true).Length == 2, "Expression must contain only two eyes");
                        Check(pivot.gameObject.activeSelf == (name == "Normal Eyes Pivot"), "Expression visible in default prefab pose");
                    }
                }
                File.WriteAllText(Path.Combine(Application.dataPath, "../player-expression-results.txt"),
                    "PASS: Red/blue normal, happy and sad eye pairs are wired to their own body bones; normal eyes shown by default.\n");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static T ExpressionField<T>(object component, string name) =>
            (T)component.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(component);

        private static PlayerExpressionView ExpressionFixture(Fixture f)
        {
            f.Floor(); f.CreatePlayer(true);
            GameObject inactive = f.NewObject("Inactive expression source", Origin, false);
            GameObject source = UnityEngine.Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelAssets.PlayerPath), inactive.transform);
            var sourcePlayer = source.GetComponent<PlayerController>();
            var sourceBlink = source.GetComponent<PlayerBlinkView>();
            var sourceExpressions = source.GetComponent<PlayerExpressionView>();
            foreach (var pair in new[] { (sourcePlayer.Body, f.Body), (sourcePlayer.Head, f.Head) })
            {
                Transform facing = pair.Item1.Visual.Find("Facing");
                facing.SetParent(pair.Item2.Visual, false);
            }
            var view = f.Player.GetComponent<PlayerView>();
            view.ConfigureModels(f.Body.Visual.Find("Facing"), f.Head.Visual.Find("Facing"),
                f.Body.Visual.GetComponentInChildren<Animator>(), f.Head.Visual.GetComponentInChildren<Animator>());
            var blink = f.Player.gameObject.AddComponent<PlayerBlinkView>();
            blink.Configure(f.Player, view, ExpressionField<GameObject>(sourceBlink, "lowerEyes"),
                ExpressionField<GameObject>(sourceBlink, "upperEyes"), ExpressionField<AnimationClip>(sourceBlink, "lowerBlink"),
                ExpressionField<AnimationClip>(sourceBlink, "upperBlink"));
            var expressions = f.Player.gameObject.AddComponent<PlayerExpressionView>();
            expressions.Configure(f.Player, view, blink,
                ExpressionField<PlayerExpressionView.EyeSet>(sourceExpressions, "lowerEyes"),
                ExpressionField<PlayerExpressionView.EyeSet>(sourceExpressions, "upperEyes"));
            return expressions;
        }

        private static void PlayerMagneticExpressions()
        {
            using (var f = new Fixture())
            {
                PlayerExpressionView expressions = ExpressionFixture(f);
                var blink = f.Player.GetComponent<PlayerBlinkView>();
                int successes = 0;
                f.Player.MagneticJoined += () => successes++;
                f.Detach(f.Spawn, f.Spawn + Vector2.up * 3f);
                f.Input.SetExternalInput(0f, true); f.Step();
                Check(f.Player.Phase == PlayerPhase.Pulling && !f.Player.IsDistantPull, "Short recall not classified correctly");
                expressions.Simulate(0.25f);
                Check(expressions.Current == PlayerExpressionView.Expression.Normal, "Short pull incorrectly cries");
                for (int i = 0; i < 300 && f.Player.Phase == PlayerPhase.Pulling; i++)
                { f.Step(); expressions.Simulate(Time.fixedDeltaTime); }
                Check(successes == 1 && expressions.Current == PlayerExpressionView.Expression.Happy && blink.Suppressed,
                    "Successful magnetic join did not trigger smile or suppress blinking");
                var lower = ExpressionField<PlayerExpressionView.EyeSet>(expressions, "lowerEyes");
                expressions.Simulate(0.05f);
                Check(f.Manager.BeginRewind(), "Cannot rewind during expression transition");
                Vector3 frozenTransition = lower.Normal.localScale;
                expressions.Simulate(0.5f);
                Check(lower.Normal.localScale == frozenTransition, "Active expression transition advanced during rewind");
                f.Manager.EndRewind();
                expressions.Simulate(0.3f);
                Check(lower.Happy.gameObject.activeSelf && !lower.Normal.gameObject.activeSelf && !lower.Sad.gameObject.activeSelf,
                    "Smile did not replace ordinary eyes");
                Check(Mathf.Approximately(lower.Happy.localScale.x, lower.HappyScale.x)
                    && Mathf.Approximately(lower.Happy.localScale.z, lower.HappyScale.z), "Expression changed horizontal or depth scale");
                expressions.Simulate(0.65f);
                Check(expressions.Current == PlayerExpressionView.Expression.Normal && !blink.Suppressed && lower.Normal.gameObject.activeSelf,
                    "Smile did not return to ordinary blinking eyes");

                f.Player.TeleportJoined(f.Spawn);
                // Classification must use the recall distance, even when launch heights match.
                f.Config.DistantLaunchHeight = f.Config.NormalLaunchHeight;
                f.Detach(f.Spawn, f.Spawn + Vector2.up * (f.Config.DistantThreshold + 2f));
                f.Input.SetExternalInput(0f, false); f.Step();
                f.Input.SetExternalInput(0f, true); f.Step();
                Check(f.Player.IsDistantPull, "Long recall not classified independently of launch height");
                expressions.Simulate(0.3f);
                Check(expressions.Current == PlayerExpressionView.Expression.Sad && lower.Sad.gameObject.activeSelf && blink.Suppressed,
                    "Long pull did not show sad eyes");
                expressions.Simulate(0.4f);
                Check(expressions.Current == PlayerExpressionView.Expression.Sad && lower.Sad.gameObject.activeSelf,
                    "Crying eyes did not hold for approximately half a second");
                expressions.Simulate(0.4f);
                Check(expressions.Current == PlayerExpressionView.Expression.Normal && !blink.Suppressed,
                    "Crying eyes did not restore ordinary eyes after the half-second hold");
                expressions.Simulate(3f);
                Check(expressions.Current == PlayerExpressionView.Expression.Normal && lower.Normal.gameObject.activeSelf,
                    "Crying eyes retriggered repeatedly within the same long pull");
                f.Step(2);
                Check(f.Manager.BeginRewind(), "Cannot rewind long pull");
                Vector3 frozen = lower.Sad.localScale;
                expressions.Simulate(0.5f);
                Check(lower.Sad.localScale == frozen, "Expression tween advanced while rewinding");
                f.Manager.RewindBy(Time.fixedDeltaTime); f.Manager.EndRewind();
                Check(f.Player.IsDistantPull, "Long-pull classification did not survive snapshot restoration");
                for (int i = 0; i < 400 && f.Player.Phase == PlayerPhase.Pulling; i++)
                { f.Step(); expressions.Simulate(Time.fixedDeltaTime); }
                Check(successes == 2 && expressions.Current == PlayerExpressionView.Expression.Happy,
                    "Long pull did not switch from crying to smiling on success");
                expressions.enabled = false;
                Check(!blink.Suppressed && lower.Normal.gameObject.activeSelf && !lower.Happy.gameObject.activeSelf,
                    "Disabling presentation left an expression or tween behind");
                expressions.enabled = true;
                f.Respawn.BeginSingleReturn(f.Body);
                expressions.Simulate(0.02f);
                Check(expressions.Current == PlayerExpressionView.Expression.Normal && !blink.Suppressed,
                    "Respawn failed to restore ordinary eyes");
            }
            using (var f = new Fixture())
            {
                PlayerExpressionView expressions = ExpressionFixture(f);
                int successes = 0; f.Player.MagneticJoined += () => successes++;
                f.Box("Recall ceiling", f.Spawn + Vector2.up * 2f, new Vector2(3f, 0.3f)).AddComponent<WorldSurface>();
                f.Detach(f.Spawn, f.Spawn + Vector2.up * (f.Config.DistantThreshold + 2f));
                f.Input.SetExternalInput(0f, true); f.Step(); expressions.Simulate(0.3f);
                for (int i = 0; i < 400 && f.Player.Phase == PlayerPhase.Pulling; i++)
                { f.Step(); expressions.Simulate(Time.fixedDeltaTime); }
                expressions.Simulate(0.5f);
                Check(successes == 0 && expressions.Current == PlayerExpressionView.Expression.Normal
                    && !f.Player.GetComponent<PlayerBlinkView>().Suppressed, "Blocked recall incorrectly smiled or retained crying eyes");
            }
            File.AppendAllText(Path.Combine(Application.dataPath, "../player-expression-results.txt"),
                "PASS: Real short/long recalls, success, blocked recall, rewind freeze/restore, disable and respawn.\n");
        }

        private static void RenderPlayerExpressions()
        {
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Camera camera = new GameObject("Expression preview").AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1.1f;
            camera.transform.position = new Vector3(0f, 0.95f, -8f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.15f, 0.19f);
            Light light = new GameObject("Expression light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
            AnimationClip idle = PlayerModelAssets.AnimationClipAt(PlayerModelAssets.IdlePath);
            for (int i = 0; i < 3; i++)
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelAssets.PlayerPath), scene);
                root.transform.position = Vector3.right * ((i - 1) * 1.4f);
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    if (renderer is SpriteRenderer || renderer is LineRenderer || renderer is ParticleSystemRenderer) renderer.enabled = false;
                var player = root.GetComponent<PlayerController>();
                foreach (MovableEntity entity in new[] { player.Body, player.Head })
                    idle.SampleAnimation(entity.Visual.GetComponentInChildren<Animator>().gameObject, idle.length * 0.3f);
                var expressions = root.GetComponent<PlayerExpressionView>(); expressions.ResetExpression();
                if (i == 2)
                {
                    typeof(PlayerController).GetField("phase", BindingFlags.NonPublic | BindingFlags.Instance)
                        .SetValue(player, PlayerPhase.Pulling);
                    typeof(PlayerController).GetField("distantPull", BindingFlags.NonPublic | BindingFlags.Instance)
                        .SetValue(player, true);
                }
                if (i == 1) typeof(PlayerExpressionView).GetMethod("OnMagneticJoined", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(expressions, null);
                expressions.Simulate(0.3f);
            }
            var target = new RenderTexture(1440, 720, 24);
            var pixels = new Texture2D(1440, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(Application.dataPath, "../player-expression-preview.png"), pixels.EncodeToPNG());
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
