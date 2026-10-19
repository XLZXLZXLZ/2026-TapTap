namespace TapTap
{
    // Capture and restore data only. Restoring must not replay gameplay events.
    public interface IRewindable<TState> where TState : struct
    {
        TState CaptureState();
        void RestoreState(in TState state);
        // True only when gameplay is unchanged and no ongoing task needs a timeline.
        // Ignore an advancing clock itself, but keep recording its active timers.
        bool CanSkipRecording(in TState previous, in TState current);
    }
}
