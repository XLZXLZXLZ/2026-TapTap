namespace TapTap
{
    // Capture and restore data only. Restoring must not replay gameplay events.
    public interface IRewindable<TState> where TState : struct
    {
        TState CaptureState();
        void RestoreState(in TState state);
    }
}
