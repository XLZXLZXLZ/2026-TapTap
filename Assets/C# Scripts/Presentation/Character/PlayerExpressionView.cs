using System;
using DG.Tweening;
using UnityEngine;

namespace TapTap
{
    public sealed class PlayerExpressionView : MonoBehaviour
    {
        public enum Expression { Normal, Happy, Sad }
        public enum SadPlaybackPhase { None, Starting, Looping, Recovering }

        [Serializable]
        public sealed class EyeSet
        {
            public Transform Normal, Happy, Sad;
            public Vector3 NormalScale = Vector3.one, HappyScale = Vector3.one, SadScale = Vector3.one;

            public EyeSet(Transform normal, Transform happy, Transform sad)
            {
                Normal = normal; Happy = happy; Sad = sad;
                NormalScale = normal.localScale; HappyScale = happy.localScale; SadScale = sad.localScale;
            }

            public void Render(float normal, float happy, float sad)
            {
                Scale(Normal, NormalScale, normal);
                Scale(Happy, HappyScale, happy);
                Scale(Sad, SadScale, sad);
            }

            private static void Scale(Transform pivot, Vector3 original, float amount)
            {
                if (pivot == null) return;
                // The pivot is centered on the eye line and its Y axis is vertical.
                // FBX eye meshes use different local axes, so animate the pivot.
                pivot.localScale = new Vector3(original.x, original.y * Mathf.Max(0.0001f, amount), original.z);
                pivot.gameObject.SetActive(amount > 0.0001f);
            }
        }

        [SerializeField] private PlayerController controller;
        [SerializeField] private PlayerView view;
        [SerializeField] private PlayerBlinkView blink;
        [SerializeField] private EyeSet lowerEyes, upperEyes;
        [SerializeField, Min(0.02f)] private float transitionDuration = 0.12f;
        [SerializeField, Min(0f), InspectorName("笑眼停留（秒）")] private float happyHoldDuration = 0.5f;
        [SerializeField, Min(0f), InspectorName("哭眼停留（秒）")] private float sadHoldDuration = 0.5f;
        [SerializeField, Min(0.02f), InspectorName("悬空哭眼循环周期（秒）")] private float sadLoopPeriod = 0.6f;
        [SerializeField, Range(0.1f, 1f), InspectorName("悬空哭眼最小高度")] private float sadLoopMinimum = 0.85f;
        private PlayerController subscribedController;
        private Sequence timeline;
        private float timelineTime;
        private float normalWeight = 1f, happyWeight, sadWeight;
        private bool wasDistantPull;
        private float sadLoopTime;
        public Expression Current { get; private set; }
        public SadPlaybackPhase SadPhase { get; private set; }

        public void Configure(PlayerController player, PlayerView presentation, PlayerBlinkView blinking,
            EyeSet lower, EyeSet upper)
        {
            Unsubscribe();
            controller = player; view = presentation; blink = blinking;
            lowerEyes = lower; upperEyes = upper;
            wasDistantPull = false;
            ResetExpression();
            if (Application.isPlaying) Subscribe();
        }

        private void Subscribe()
        {
            if (controller == null || subscribedController == controller) return;
            Unsubscribe();
            subscribedController = controller;
            subscribedController.MagneticJoined += OnMagneticJoined;
        }

        private void Unsubscribe()
        {
            if (subscribedController != null) subscribedController.MagneticJoined -= OnMagneticJoined;
            subscribedController = null;
        }

        private void OnMagneticJoined()
        {
            if (!controller.IsHangingFromHead) Show(Expression.Happy);
        }
        private void LateUpdate() => Simulate(Time.deltaTime);

        public void Simulate(float dt)
        {
            if (controller == null || view == null) return;
            if (view.ExitPresentation || controller.Body.IsReturning || controller.Head.IsReturning)
            { wasDistantPull = false; ResetExpression(); return; }
            if (dt <= 0f || RewindManager.Rewinding || WorldMapProgression.InputBlocked) return;
            bool distantPull = controller.IsDistantPull;
            bool hanging = controller.IsHangingFromHead;
            bool sustainedSad = SadPhase == SadPlaybackPhase.Starting || SadPhase == SadPlaybackPhase.Looping;
            if (hanging)
            {
                if (!sustainedSad) Show(Expression.Sad, true);
            }
            else if (sustainedSad)
            {
                Show(Expression.Normal);
                SadPhase = SadPlaybackPhase.Recovering;
            }
            else if (distantPull && !wasDistantPull) Show(Expression.Sad);
            else if (!distantPull && wasDistantPull && Current == Expression.Sad) Show(Expression.Normal);
            wasDistantPull = distantPull;
            if (SadPhase == SadPlaybackPhase.Looping) { AdvanceSadLoop(dt); return; }
            if (timeline == null) return;
            float remaining = Mathf.Max(0f, timelineTime + dt - timeline.Duration());
            timelineTime = Mathf.Min(timelineTime + dt, timeline.Duration());
            // A paused sequence advances only with presentation time, so rewind,
            // menus and gameplay pause cannot independently advance its tweens.
            timeline.Goto(timelineTime, false);
            if (timelineTime < timeline.Duration()) return;
            timeline.Kill(); timeline = null;
            if (SadPhase == SadPlaybackPhase.Starting)
            {
                SadPhase = SadPlaybackPhase.Looping;
                sadLoopTime = 0f;
                AdvanceSadLoop(remaining);
                return;
            }
            ResetExpression();
        }

        private void AdvanceSadLoop(float dt)
        {
            float period = Mathf.Max(0.02f, sadLoopPeriod);
            sadLoopTime = Mathf.Repeat(sadLoopTime + dt, period);
            float pulse = (1f - Mathf.Cos(sadLoopTime / period * Mathf.PI * 2f)) * 0.5f;
            sadWeight = Mathf.Lerp(1f, Mathf.Clamp(sadLoopMinimum, 0.1f, 1f), pulse);
            Render();
        }

        private void Show(Expression expression, bool sustainedSad = false)
        {
            timeline?.Kill();
            Current = expression;
            SadPhase = sustainedSad ? SadPlaybackPhase.Starting : SadPlaybackPhase.None;
            timelineTime = 0f;
            if (blink != null) blink.SetSuppressed(true);
            timeline = DOTween.Sequence().SetAutoKill(false).Pause();
            timeline.Append(Weight(normalWeight, 0f, v => { normalWeight = v; Render(); }));
            timeline.Join(Weight(happyWeight, 0f, v => { happyWeight = v; Render(); }));
            timeline.Join(Weight(sadWeight, 0f, v => { sadWeight = v; Render(); }));
            if (expression == Expression.Normal)
                timeline.Append(Weight(0f, 1f, v => { normalWeight = v; Render(); }));
            else
            {
                bool happy = expression == Expression.Happy;
                timeline.Append(Weight(0f, 1f, v => { if (happy) happyWeight = v; else sadWeight = v; Render(); }));
                if (sustainedSad) return;
                timeline.AppendInterval(happy ? happyHoldDuration : sadHoldDuration);
                timeline.Append(Weight(1f, 0f, v => { if (happy) happyWeight = v; else sadWeight = v; Render(); }));
                timeline.Append(Weight(0f, 1f, v => { normalWeight = v; Render(); }));
            }
        }

        private Tween Weight(float from, float to, TweenCallback<float> setter) =>
            DOVirtual.Float(from, to, Mathf.Max(0.02f, transitionDuration), setter).SetEase(Ease.InOutSine);

        private void Render()
        {
            lowerEyes?.Render(normalWeight, happyWeight, sadWeight);
            upperEyes?.Render(normalWeight, happyWeight, sadWeight);
        }

        public void ResetExpression()
        {
            timeline?.Kill(); timeline = null;
            Current = Expression.Normal;
            SadPhase = SadPlaybackPhase.None;
            sadLoopTime = 0f;
            normalWeight = 1f; happyWeight = sadWeight = 0f;
            Render();
            if (blink != null) blink.SetSuppressed(false);
        }

        private void OnEnable() { wasDistantPull = false; ResetExpression(); Subscribe(); }
        private void OnDisable() { Unsubscribe(); wasDistantPull = false; ResetExpression(); }
        private void OnDestroy() { Unsubscribe(); timeline?.Kill(); }
    }
}
