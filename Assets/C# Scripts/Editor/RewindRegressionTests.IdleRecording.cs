using UnityEngine;

namespace TapTap.Editor
{
    public static partial class RewindRegressionTests
    {
        private static void IdleRecordingHistory()
        {
            using (var f = new Fixture())
            {
                f.Floor(); f.CreatePlayer(); f.Step(10);
                f.Manager.ClearHistory();
                f.Step(499);
                Check(!f.Manager.IsRecordingSuspended && f.Manager.CurrentTick == 499,
                    "History stopped before ten seconds of stable gameplay");
                f.Step();
                Check(f.Manager.IsRecordingSuspended && f.Manager.CurrentTick == 500,
                    "History did not suspend after ten seconds");
                long endpoint = f.Manager.CurrentTick;
                double beforeWait = f.Manager.SimulationTime;
                f.Step(30000);
                double afterWait = f.Manager.SimulationTime;
                float playerClock = f.Player.CaptureState().Clock;
                Check(f.Manager.CurrentTick == endpoint && f.Manager.NewestTick == endpoint
                    && f.Manager.AvailableSeconds < 10.01f && afterWait - beforeWait > 599d,
                    "Waiting grew history or stopped simulation");
                Check(f.Manager.BeginRewind(), "Cannot rewind directly from idle");
                Check(System.Math.Abs(f.Manager.SimulationTime - afterWait) < 0.0001d
                    && Mathf.Abs(f.Player.CaptureState().Clock - playerClock) < 0.0001f,
                    "Beginning rewind restored a stale idle endpoint");
                f.Manager.EndRewind();

                Vector2 position = f.Body.Motor.Position;
                f.Input.SetExternalInput(1f, false); f.Step();
                Check(f.Manager.CurrentTick == endpoint + 1 && !f.Manager.IsRecordingSuspended,
                    "The first movement step was not recorded");
                Check(f.Body.Motor.Position.x > position.x, "Idle recording blocked movement");
                f.Back(1);
                Near(f.Body.Motor.Position, position, "Rewind lost the state before resuming movement");
                Check(System.Math.Abs(f.Manager.SimulationTime - afterWait) < 0.0001d,
                    "Movement lost the latest pre-movement simulation time");
                f.Back(1);
                Check(f.Manager.SimulationTime < beforeWait, "Rewind did not skip the omitted idle time");
                f.Manager.EndRewind();
                f.Input.SetExternalInput(-1f, false); f.Step();
                Check(f.Manager.NewestTick == endpoint, "Resuming retained the discarded future branch");
                f.Manager.ClearHistory();
                Check(!f.Manager.IsRecordingSuspended && f.Manager.AvailableSeconds == 0f,
                    "Clearing history retained idle recording state");
            }
        }

        private static void IdleRecordingWorldChanges()
        {
            using (var f = new Fixture())
            {
                f.Floor(); f.CreatePlayer();
                f.Detach(f.Spawn, f.Spawn + Vector2.right * 3f);
                MovableEntity actor = f.Actor("Other recorded actor", f.Spawn + Vector2.right * 8f, true);
                f.Step(600);
                Check(f.Manager.IsRecordingSuspended, "Stable detached halves did not suspend recording");
                long tick = f.Manager.CurrentTick;
                Vector2 bodyPosition = f.Body.Motor.Position;
                f.Head.Motor.Teleport(f.Head.Motor.Position + Vector2.right * 0.001f); f.Step();
                Check(!f.Manager.IsRecordingSuspended && f.Manager.CurrentTick == tick + 1,
                    "Detached head movement was omitted");
                Near(f.Body.Motor.Position, bodyPosition, "Head-only test moved the body");

                f.Step(600); tick = f.Manager.CurrentTick;
                Check(f.Manager.IsRecordingSuspended, "Recording did not suspend again");
                actor.Motor.Teleport(actor.Motor.Position + Vector2.right); f.Step();
                Check(!f.Manager.IsRecordingSuspended && f.Manager.CurrentTick == tick + 1,
                    "Another recorded actor moved without resuming history");

                f.Step(600); tick = f.Manager.CurrentTick;
                bool phase = WorldPhaseState.Instance.Active;
                WorldPhaseState.Instance.Toggle(); f.Step();
                Check(!f.Manager.IsRecordingSuspended && f.Manager.CurrentTick == tick + 1,
                    "A world phase change was omitted");
                f.Back(1);
                Check(WorldPhaseState.Instance.Active == phase, "Cannot undo a phase change after idle");
            }
        }

        private static void IdleRecordingTasks()
        {
            using (var f = new Fixture())
            {
                f.Floor(); f.CreatePlayer(); f.Step(600);
                Check(f.Manager.IsRecordingSuspended, "Test did not reach idle recording");
                long tick = f.Manager.CurrentTick;
                f.Config.DeathShakeDuration = 15f;
                f.Respawn.BeginGroupReturn(f.Body, f.Head, f.Config.JoinedOffset);
                f.Step(550);
                Check(f.Respawn.Journeys.Count > 0 && !f.Manager.IsRecordingSuspended
                    && f.Manager.CurrentTick == tick + 550,
                    "A stationary death/respawn task stopped being recorded");
                f.Back(550);
                Check(f.Respawn.Journeys.Count == 0 && !f.Body.IsReturning,
                    "Idle history cannot undo the beginning of a respawn");
                f.Manager.EndRewind();

                var state = f.Player.CaptureState();
                state.RecallReadyAt = state.Clock + 30f;
                f.Player.RestoreState(in state);
                f.Step(600);
                Check(!f.Manager.IsRecordingSuspended, "An active player cooldown was omitted");
            }
        }
    }
}
