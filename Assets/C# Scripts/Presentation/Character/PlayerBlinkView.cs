using UnityEngine;

namespace TapTap
{
    // Samples only eye-scale curves. Body animators remain responsible for Idle/Walk.
    public sealed class PlayerBlinkView : MonoBehaviour
    {
        [SerializeField] private PlayerController controller;
        [SerializeField] private PlayerView view;
        [SerializeField] private GameObject lowerEyes;
        [SerializeField] private GameObject upperEyes;
        [SerializeField] private AnimationClip lowerBlink;
        [SerializeField] private AnimationClip upperBlink;
        [SerializeField, Min(0.5f)] private float interval = 5f;
        private float elapsed;
        public bool Suppressed { get; private set; }

        public void SetSuppressed(bool value)
        {
            if (Suppressed == value) return;
            Suppressed = value;
            ResetBlink();
        }

        public void Configure(PlayerController player, PlayerView presentation, GameObject lower, GameObject upper,
            AnimationClip lowerClip, AnimationClip upperClip)
        {
            controller = player; view = presentation;
            lowerEyes = lower; upperEyes = upper;
            lowerBlink = lowerClip; upperBlink = upperClip;
            ResetBlink();
        }

        private void LateUpdate()
        {
            if (controller == null || view == null) return;
            if (view.ExitPresentation) { ResetBlink(); return; }
            if (RewindManager.Rewinding || WorldMapProgression.InputBlocked || LevelAnnotation.IsFeedbackOpen) return;
            if (controller.Body.IsReturning || controller.Head.IsReturning) { ResetBlink(); return; }
            Simulate(Time.deltaTime);
        }

        public void Simulate(float dt)
        {
            if (dt <= 0f || Suppressed) return;
            elapsed += dt;
            // First blink starts at 5s, then 10s, 15s; duration does not shift the cadence.
            if (elapsed >= interval * 2f) elapsed = interval + Mathf.Repeat(elapsed - interval, interval);
            float time = elapsed < interval ? -1f : elapsed - interval;
            Sample(lowerEyes, lowerBlink, time);
            Sample(upperEyes, upperBlink, time);
        }

        private static void Sample(GameObject eyes, AnimationClip clip, float time)
        {
            if (eyes != null && clip != null)
                clip.SampleAnimation(eyes, time >= 0f && time < clip.length ? time : 0f);
        }

        public void ResetBlink()
        {
            elapsed = 0f;
            Sample(lowerEyes, lowerBlink, -1f);
            Sample(upperEyes, upperBlink, -1f);
        }

        private void OnEnable() => ResetBlink();
        private void OnDisable() => ResetBlink();
    }
}
