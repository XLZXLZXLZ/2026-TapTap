using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent, RequireComponent(typeof(SphereCollider), typeof(VoicePlayer))]
    public sealed class EndingRadioTrigger : MonoBehaviour
    {
        public EndingSequenceController Sequence;
        public VoiceSequence EndingVoice;
        [Min(0.1f)] public float TriggerRadius = 1.6f;
        public Transform BreathingTarget;
        [Range(0f, 0.15f)] public float BreathingAmount = 0.025f;
        [Min(0.1f)] public float BreathingDuration = 2.4f;
        private VoicePlayer playback;
        private Vector3 restingScale;
        private float phase;
        private bool consumed;
        public VoicePlayer Playback => playback != null ? playback : playback = GetComponent<VoicePlayer>();
        public bool HasTriggered => consumed;

        private void Awake()
        {
            ConfigureArea();
            playback = GetComponent<VoicePlayer>();
            if (BreathingTarget != null) restingScale = BreathingTarget.localScale;
        }

        private void OnValidate() => ConfigureArea();
        private void ConfigureArea()
        {
            SphereCollider area = GetComponent<SphereCollider>();
            if (area == null) return;
            area.isTrigger = true;
            area.radius = Mathf.Max(0.1f, TriggerRadius);
        }

        private void OnTriggerEnter(Collider other) => TryActivate(other.GetComponentInParent<EndingPlayerController>());
        // Also catches a player who was already inside the sphere when intro control was released.
        private void OnTriggerStay(Collider other) => TryActivate(other.GetComponentInParent<EndingPlayerController>());

        public bool TryActivate(EndingPlayerController player)
        {
            if (!isActiveAndEnabled || consumed || Sequence == null || player == null || !player.HasControl
                || Time.timeScale <= 0f || PauseMenu.BlocksInput) return false;
            if (!Sequence.TryBeginBroadcast(this, player)) return false;
            consumed = true;
            return true;
        }

        public void ResetTrigger() => consumed = false;

        private void LateUpdate()
        {
            if (BreathingTarget == null || BreathingTarget == transform || !BreathingTarget.IsChildOf(transform)) return;
            if (!Playback.IsPlaying) { BreathingTarget.localScale = restingScale; phase = 0f; return; }
            if (Playback.IsPaused) return;
            phase = Mathf.Repeat(phase + Time.unscaledDeltaTime / Mathf.Max(0.1f, BreathingDuration), 1f);
            float swell = (1f - Mathf.Cos(phase * Mathf.PI * 2f)) * 0.5f;
            BreathingTarget.localScale = restingScale * (1f + BreathingAmount * swell);
        }

        private void OnDisable()
        {
            if (BreathingTarget != null) BreathingTarget.localScale = restingScale;
            if (playback != null) playback.Stop(VoiceStopReason.Disabled);
        }
    }
}
