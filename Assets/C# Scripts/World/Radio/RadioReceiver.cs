using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider2D), typeof(VoicePlayer))]
    [DefaultExecutionOrder(850)]
    public sealed class RadioReceiver : MonoBehaviour
    {
        [Header("两套独立语音")]
        public VoiceSequence FirstEncounter;
        public VoiceSequence ReturnJourney;
        [Tooltip("可留空，自动寻找同场景角色；只检测身体。")]
        public PlayerController Player;
        [Tooltip("后续演出用收音机可关闭自动触发，再由演出调用 TryPlay。")]
        public bool TriggerAutomatically = true;

        [Header("收听范围（世界单位，按玩家身体的 XY 距离）")]
        [Min(0f)] public float FullVolumeRadius = 3f;
        [Min(0.01f)] public float ListeningRadius = 10f;
        [Tooltip("超出最大收听范围即中断；未播完不计入已收听，再进入可重新播放。")]
        public bool StopOutsideListeningRange = true;

        private BoxCollider2D area;
        private VoicePlayer playback;
        private bool returning, playingReturn, wasInside, warnedConfiguration;
        private float nextPlayerSearch;
        public bool FirstEncounterCompleted { get; private set; }
        public bool ReturnJourneyCompleted { get; private set; }
        public bool IsReturning => returning;
        public VoicePlayer Playback => playback;

        private void Awake()
        {
            area = GetComponent<BoxCollider2D>();
            area.isTrigger = true;
            playback = GetComponent<VoicePlayer>();
        }

        private void OnEnable()
        {
            if (playback == null) Awake();
            returning = GameStageProgression.HasProgressedIn(gameObject.scene);
            wasInside = false;
            EventManager.Subscribe<GameStageChanged>(StageChanged);
            playback.Ended += PlaybackEnded;
        }

        private void OnDisable()
        {
            EventManager.Unsubscribe<GameStageChanged>(StageChanged);
            if (playback != null)
            {
                playback.Ended -= PlaybackEnded;
                playback.Stop(VoiceStopReason.Disabled);
            }
            wasInside = false;
        }

        private void ResolvePlayer()
        {
            if (Player != null || Time.unscaledTime < nextPlayerSearch) return;
            nextPlayerSearch = Time.unscaledTime + 0.5f;
            Player = ReturnJourneyContact.FindPlayer(gameObject.scene);
        }

        private void Update()
        {
            ResolvePlayer();
            float distance = Player != null && Player.Body != null && Player.Body.Motor != null
                ? Vector2.Distance(transform.position, Player.Body.Motor.Position) : float.PositiveInfinity;
            playback.SetDistanceGain(Attenuation(distance, FullVolumeRadius, ListeningRadius));
            if (playback.IsPlaying && StopOutsideListeningRange && distance >= Mathf.Max(FullVolumeRadius, ListeningRadius)
                && ReturnJourneyContact.CanTrigger(Player)) playback.Stop();
        }

        private void FixedUpdate()
        {
            ResolvePlayer();
            if (!ReturnJourneyContact.CanTrigger(Player)) return;
            bool inside = ReturnJourneyContact.TouchesBody(area, Player);
            if (TriggerAutomatically && inside && !wasInside) TryPlay();
            wasInside = inside;
        }

        public bool TryPlay()
        {
            ResolvePlayer();
            if (!isActiveAndEnabled || playback.IsPlaying || !ReturnJourneyContact.CanTrigger(Player)
                || (returning ? ReturnJourneyCompleted : FirstEncounterCompleted)) return false;
            VoiceSequence sequence = returning ? ReturnJourney : FirstEncounter;
            if (sequence == null || !sequence.IsConfigured)
            {
                if (!warnedConfiguration)
                    Debug.LogWarning("收音机的当前阶段语音尚未配置完成；请填入分段音频与字幕。", this);
                warnedConfiguration = true;
                return false;
            }
            playingReturn = returning;
            playback.SetDistanceGain(Attenuation(Vector2.Distance(transform.position, Player.Body.Motor.Position),
                FullVolumeRadius, ListeningRadius));
            bool result = playback.Play(sequence);
            if (result) warnedConfiguration = false;
            return result;
        }

        private void PlaybackEnded(VoicePlayer player, VoiceSequence sequence, VoiceStopReason reason)
        {
            if (reason != VoiceStopReason.Completed) return;
            if (playingReturn) ReturnJourneyCompleted = true;
            else FirstEncounterCompleted = true;
        }

        private void StageChanged(GameStageChanged change)
        {
            if (change.Scene != gameObject.scene) return;
            bool next = change.Stage >= GameStage.Returning;
            if (returning == next) return;
            playback.Stop();
            returning = next;
            wasInside = warnedConfiguration = false;
        }

        public void ResetPlaybackHistory()
        {
            playback.Stop();
            FirstEncounterCompleted = ReturnJourneyCompleted = false;
            wasInside = false;
        }

        public static float Attenuation(float distance, float fullRadius, float listeningRadius)
        {
            fullRadius = Mathf.Max(0f, fullRadius);
            listeningRadius = Mathf.Max(fullRadius + 0.001f, listeningRadius);
            if (distance <= fullRadius) return 1f;
            if (distance >= listeningRadius) return 0f;
            return 1f - Mathf.InverseLerp(fullRadius, listeningRadius, distance);
        }

        private void Reset()
        {
            BoxCollider2D box = GetComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = Vector2.one * 4f;
            box.offset = Vector2.up;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            BoxCollider2D box = GetComponent<BoxCollider2D>();
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.7f);
            if (box != null) Gizmos.DrawWireCube(box.offset, box.size);
            Gizmos.matrix = previous;
            UnityEditor.Handles.color = new Color(0.4f, 1f, 0.75f, 0.8f);
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, FullVolumeRadius);
            UnityEditor.Handles.color = new Color(0.4f, 0.75f, 1f, 0.5f);
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, Mathf.Max(FullVolumeRadius, ListeningRadius));
        }
#endif
    }
}
