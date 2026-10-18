namespace TapTap
{
    public enum TutorialPromptKey { Space, Z, Control }
    public enum TutorialPromptCompletion { Press, ReleaseAfterHold }

    /// <summary>Observes input without consuming the player's gameplay input queue.</summary>
    public sealed class TutorialPromptInput
    {
        private readonly TutorialPromptCompletion condition;
        private bool armed;
        private bool previousHeld;
        public bool Completed { get; private set; }

        public TutorialPromptInput(TutorialPromptCompletion condition, bool held)
        {
            this.condition = condition;
            Synchronize(held);
        }

        // A pause, lost focus or transition must not manufacture an input edge on resume.
        public void Synchronize(bool held) { previousHeld = held; armed = held; }

        public void Observe(bool held, bool pressed, bool released)
        {
            if (Completed) return;
            pressed |= held && !previousHeld;
            released |= !held && previousHeld;
            armed |= held || pressed;
            if (condition == TutorialPromptCompletion.Press ? pressed : armed && released)
                Completed = true;
            previousHeld = held;
        }
    }
}
