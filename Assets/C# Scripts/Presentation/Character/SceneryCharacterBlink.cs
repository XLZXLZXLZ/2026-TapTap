using UnityEngine;

namespace TapTap
{
    // Independent scenery presentation: no player, input, rewind or progression dependencies.
    public sealed class SceneryCharacterBlink : MonoBehaviour
    {
        [SerializeField] private GameObject lowerEyes;
        [SerializeField] private GameObject upperEyes;
        [SerializeField] private AnimationClip lowerBlink;
        [SerializeField] private AnimationClip upperBlink;
        [SerializeField, Min(0.5f)] private float interval = 5f;
        private float elapsed;

        public void Configure(GameObject lower, GameObject upper, AnimationClip lowerClip, AnimationClip upperClip)
        {
            lowerEyes = lower; upperEyes = upper;
            lowerBlink = lowerClip; upperBlink = upperClip;
            ResetBlink();
        }

        private void LateUpdate() => Simulate(Time.deltaTime);

        public void Simulate(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            elapsed += deltaTime;
            float cadence = Mathf.Max(0.5f, interval);
            if (elapsed >= cadence * 2f) elapsed = cadence + Mathf.Repeat(elapsed - cadence, cadence);
            float time = elapsed < cadence ? -1f : elapsed - cadence;
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
