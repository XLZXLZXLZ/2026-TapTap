using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static partial class RewindRegressionTests
    {
        private static void ImmediateRecallFeel()
        {
            using (var f = new Fixture())
            {
                f.Floor(); f.CreatePlayer();
                f.Detach(f.Spawn, f.Spawn + new Vector2(0.39f, 3f));
                f.Input.SetExternalInput(0f, true); f.Step();
                Check(f.Player.Phase == PlayerPhase.Pulling && !f.Player.GuideVisible,
                    "Aligned press waited for release or left the aiming guide active");
                f.Input.SetExternalInput(0f, false); f.Step();
                Check(f.Player.Phase == PlayerPhase.Pulling, "Release interrupted an immediate recall");
            }
            using (var f = new Fixture())
            {
                f.Floor(); f.CreatePlayer();
                f.Detach(f.Spawn, f.Spawn + new Vector2(1.2f, 3f));
                f.Input.SetExternalInput(0f, true); f.Step();
                Check(f.Player.Phase == PlayerPhase.Detached && f.Player.GuideVisible,
                    "Unaligned press lost the existing aiming flow");
                f.Head.Motor.Teleport(f.Body.Motor.Position + new Vector2(0.2f, 3f));
                f.Input.SetExternalInput(0f, false); f.Step();
                Check(f.Player.Phase == PlayerPhase.Pulling, "Release did not retry after alignment");
            }
            using (var f = new Fixture())
            {
                f.Floor(); f.CreatePlayer();
                f.Detach(f.Spawn, f.Spawn + Vector2.up * 3f);
                var state = f.Player.CaptureState();
                state.RecallReadyAt = state.Clock + 0.06f;
                f.Player.RestoreState(in state);
                f.Input.SetExternalInput(0f, true); f.Step();
                Check(f.Player.Phase == PlayerPhase.Detached && f.Player.CaptureState().RecallBuffered,
                    "Press bypassed recall cooldown");
                f.Step(3);
                Check(f.Player.Phase == PlayerPhase.Pulling, "Buffered aligned press still required release after cooldown");
            }
        }

        private static void ApexGravityFeel()
        {
            using (var f = new Fixture())
            {
                f.CreatePlayer();
                f.Body.Velocity = f.Head.Velocity = Vector2.up * 2f;
                f.Step(3);
                Check(Mathf.Abs(f.Body.Velocity.y - 0.56f) < 0.0001f, "Grace changed upward acceleration");
                f.Step(2);
                Check(f.Body.Velocity.y < 0f && f.Body.Velocity.y > -0.4f,
                    "Natural apex did not soften the first descending step");
                Check(f.Body.CaptureState().ApexGraceRemaining > 0f, "Natural apex did not start grace");
            }
            using (var f = new Fixture())
            {
                f.CreatePlayer();
                f.Detach(Origin + Vector2.up * 3f, Origin + Vector2.up * 6f);
                f.Body.Velocity = Vector2.down;
                f.Head.Velocity = Vector2.up * 0.24f;
                f.Step();
                Check(Mathf.Abs(f.Body.Velocity.y + 1.48f) < 0.0001f, "Head apex slowed the independent body");
                float grace = f.Head.CaptureState().ApexGraceRemaining;
                Check(grace > 0f && f.Head.Velocity.y > -0.24f, "Detached head did not receive apex grace");
                f.Step(3);
                Vector2 expected = f.Head.Motor.Position;
                float speed = f.Head.Velocity.y;
                f.Back(3); f.Manager.EndRewind();
                Check(Mathf.Abs(f.Head.CaptureState().ApexGraceRemaining - grace) < 0.0001f,
                    "Rewind lost remaining apex grace");
                f.Step(3);
                Near(f.Head.Motor.Position, expected, "Apex motion diverged after rewind");
                Check(Mathf.Abs(f.Head.Velocity.y - speed) < 0.0001f, "Apex speed diverged after rewind");
                f.Step(8);
                float before = f.Head.Velocity.y;
                f.Step();
                Check(f.Head.CaptureState().ApexGraceRemaining == 0f
                    && Mathf.Abs(before - f.Head.Velocity.y - 0.48f) < 0.0001f,
                    "Gravity did not return to normal after the short window");
            }
        }

        private static void CeilingGravityFeel()
        {
            using (var f = new Fixture())
            {
                f.CreatePlayer();
                f.Player.TeleportJoined(Origin + Vector2.up * 2f);
                f.Box("Joined ceiling", Origin + Vector2.up * 3.5f, new Vector2(4f, 0.2f)).AddComponent<WorldSurface>();
                f.Body.Velocity = f.Head.Velocity = Vector2.up * 5f;
                for (int i = 0; i < 8 && f.Body.CaptureState().ApexGraceRemaining <= 0f; i++) f.Step();
                Check(f.Body.Velocity.y == 0f && f.Body.CaptureState().ApexGraceRemaining > 0f,
                    "Joined ceiling collision did not start grace");
                f.Step();
                Check(f.Body.Velocity.y < 0f && f.Body.Velocity.y > -0.48f, "Joined ceiling descent was not softened");
            }
            using (var f = new Fixture())
            {
                f.CreatePlayer();
                f.Detach(Origin + Vector2.up * 3f, Origin + Vector2.up * 6f);
                f.Box("Head ceiling", Origin + Vector2.up * 6.6f, new Vector2(4f, 0.2f)).AddComponent<WorldSurface>();
                f.Head.Velocity = Vector2.up * 5f;
                for (int i = 0; i < 8 && f.Head.CaptureState().ApexGraceRemaining <= 0f; i++) f.Step();
                Check(f.Head.Velocity.y == 0f && f.Head.CaptureState().ApexGraceRemaining > 0f,
                    "Detached head ceiling collision did not start grace");
                f.Step();
                Check(f.Head.Velocity.y < 0f && f.Head.Velocity.y > -0.48f, "Detached head ceiling descent was not softened");
            }
        }

        private static void AssemblyFeedbackFeel()
        {
            using (var f = new Fixture())
            {
                EffectManager effects = EffectManager.Instance;
                float originalAmount = effects.ZoomAmount;
                float originalSpeed = Time.timeScale;
                var camera = f.NewObject("Assembly camera", Vector2.zero).AddComponent<Camera>();
                var rig = camera.gameObject.AddComponent<DemoCamera>();
                rig.Configure(null, camera, null);
                var region = f.NewObject("Assembly region", Vector2.zero).AddComponent<LevelRegion>();
                rig.SetRegion(region);
                float baseFov = camera.fieldOfView;
                try
                {
                    foreach (float amount in new[] { 0f, 0.02f, 0.035f, 0.08f })
                    {
                        effects.ClearTransientEffects();
                        SetField(effects, "zoomAmount", amount);
                        Time.timeScale = 1f;
                        effects.Play(PlayerEffect.DownwardJoined, null);
                        Check(Mathf.Abs(Time.timeScale - 0.25f) < 0.0001f, "Ordinary join did not strengthen hit stop");
                        effects.Simulate(0.07f);
                        rig.SnapToPlayer();
                        float ratio = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f)
                            / Mathf.Tan(baseFov * Mathf.Deg2Rad * 0.5f);
                        Check(Mathf.Abs(ratio - (1f - amount)) < 0.0001f, "Camera ignored assembly zoom amount");
                        effects.Simulate(0.03f);
                        Check(Time.timeScale == 1f, "Ordinary join did not restore speed");
                        effects.Simulate(0.2f);
                        rig.SnapToPlayer();
                        Check(effects.ZoomPulse == 0f && Mathf.Abs(camera.fieldOfView - baseFov) < 0.0001f,
                            "Assembly zoom did not restore its base lens");
                    }
                    SetField(effects, "zoomAmount", 0.02f);
                    effects.Play(PlayerEffect.Joined, null);
                    effects.Simulate(0.1f);
                    Check(Mathf.Abs(Time.timeScale - 0.15f) < 0.0001f, "Launching join did not retain stronger hit stop");
                    effects.SetGameplayTimeScale(2f);
                    Check(Mathf.Abs(Time.timeScale - 0.3f) < 0.0001f, "Speed change lost active join slowdown");
                    effects.Simulate(0.07f);
                    Check(Time.timeScale == 2f, "Launching join restored the wrong speed");
                    effects.Simulate(0.1f);
                    Check(effects.ZoomPulse > 0f, "Launching zoom ended as early as an ordinary join");
                    effects.Simulate(0.2f);
                    Check(effects.ZoomPulse == 0f, "Launching zoom did not finish");
                    effects.Play(PlayerEffect.Joined, null);
                    effects.Play(PlayerEffect.DownwardJoined, null);
                    Check(Mathf.Abs(Time.timeScale - 0.3f) < 0.0001f, "Repeated joins compounded or weakened slowdown");
                    effects.ClearTransientEffects();
                    Check(Time.timeScale == 2f && effects.ZoomPulse == 0f, "Clearing feedback leaked slowdown or zoom");
                }
                finally
                {
                    effects.ClearTransientEffects();
                    SetField(effects, "zoomAmount", originalAmount);
                    Time.timeScale = originalSpeed;
                }
            }
        }

        private static void BulletTimeFeel()
        {
            using (var f = new Fixture())
            {
                f.CreatePlayer();
                var timer = UnityEngine.Object.FindObjectOfType<GameDebugTime>();
                if (timer == null) timer = f.NewObject("Test Gameplay Time", Vector2.zero).AddComponent<GameDebugTime>();
                var playerField = typeof(GameDebugTime).GetField("player", BindingFlags.NonPublic | BindingFlags.Instance);
                var previousPlayer = playerField.GetValue(timer);
                int previousIndex = (int)typeof(GameDebugTime).GetField("speedIndex", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(timer);
                playerField.SetValue(timer, f.Player);
                SetField(timer, "speedIndex", 0);
                Time.timeScale = 1f;
                try
                {
                    timer.ToggleBulletTime();
                    Check(timer.BulletTimeActive && Time.timeScale == 0.5f, "Ctrl toggle did not select half speed");
                    var overlay = ScreenFadeOverlay.Instance;
                    overlay.AdvanceBulletTint(0.1f, true);
                    Check(overlay.BulletOpacity > 0f && overlay.BulletOpacity < 0.1f, "Blue overlay snapped instead of fading");
                    overlay.AdvanceBulletTint(0.1f, true);
                    Check(Mathf.Abs(overlay.BulletOpacity - 0.1f) < 0.0001f, "Blue overlay did not finish fading in");
                    EffectManager.Instance.Play(PlayerEffect.Joined, null);
                    Check(Mathf.Abs(Time.timeScale - 0.075f) < 0.0001f, "Assembly slow motion replaced bullet time");
                    EffectManager.Instance.Simulate(0.2f);
                    Check(Time.timeScale == 0.5f, "Assembly effect did not restore bullet speed");
                    timer.ToggleBulletTime(); overlay.AdvanceBulletTint(0.2f, false);
                    Check(Time.timeScale == 1f && overlay.BulletOpacity == 0f, "Second toggle did not clear speed/tint");
                    timer.CycleSpeed(); timer.ToggleBulletTime(); timer.ToggleBulletTime();
                    Check(Time.timeScale == 2f, "Leaving bullet time lost the previous debug speed");
                    Time.timeScale = 0f; timer.ToggleBulletTime();
                    Check(!timer.BulletTimeActive && Time.timeScale == 0f, "Bullet toggle unpaused the game");
                    Time.timeScale = 2f; timer.ToggleBulletTime();
                    typeof(GameDebugTime).GetMethod("SceneLoaded", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(timer, new object[] { default(Scene), LoadSceneMode.Single });
                    playerField.SetValue(timer, null);
                    typeof(GameDebugTime).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(timer, null);
                    Check(!timer.BulletTimeActive && Time.timeScale == 2f, "Half-speed leaked into the next menu scene");
                }
                finally
                {
                    SetField(timer, "bulletTimeActive", false);
                    SetField(timer, "speedIndex", previousIndex);
                    playerField.SetValue(timer, previousPlayer);
                    ScreenFadeOverlay.Instance.AdvanceBulletTint(1f, false);
                    EffectManager.Instance.ClearTransientEffects();
                    Time.timeScale = 1f;
                }
            }
        }
    }
}
