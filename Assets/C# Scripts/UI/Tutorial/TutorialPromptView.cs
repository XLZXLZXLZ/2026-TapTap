using DG.Tweening;
using ReusableTextEffects;
using TMPro;
using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    public sealed class TutorialPromptView : MonoBehaviour
    {
        [Header("提示内容")]
        public string Prefix = "按住 ";
        public TutorialPromptKey Key;
        public string Suffix = " 使头部悬浮";
        public TutorialPromptCompletion Completion = TutorialPromptCompletion.ReleaseAfterHold;
        [Tooltip("可留空。当前提示完整退出后，优先播放这里的下一条。")]
        public TutorialPromptView NextPromptPrefab;

        [Header("表现引用")]
        public RectTransform AnimatedRoot;
        public CanvasGroup PanelGroup;
        public CanvasGroup TextGroup;
        public TextMeshProUGUI Label;
        public TMPTextShatter Shatter;

        [Header("动画（真实时间秒）")]
        [Min(0.05f)] public float EnterDuration = 0.32f;
        [Min(0.05f)] public float PanelFadeDuration = 0.16f;
        [Min(0f)] public float NextDelay = 0.2f;
        [Range(0f, 3f)] public float JellyOvershoot = 0.75f;

        private Tween transition;
        public bool IsEntering { get; private set; }
        public bool IsExiting => !IsEntering && transition != null && transition.IsActive();
        public bool ExitFinished => !IsEntering && !IsExiting;
        public string RichText => Prefix + "<sprite name=\"" + Key + "\">" + Suffix;

        public void Enter()
        {
            transition?.Kill();
            Shatter?.StopAndRestore();
            Label.text = RichText;
            Label.enabled = true;
            Label.ForceMeshUpdate();
            PanelGroup.alpha = TextGroup.alpha = 0f;
            AnimatedRoot.localScale = Vector3.zero;
            IsEntering = true;
            Sequence sequence = DOTween.Sequence().SetUpdate(true);
            sequence.Join(AnimatedRoot.DOScale(1f, EnterDuration).SetEase(Ease.OutBack, JellyOvershoot));
            sequence.Join(Fade(PanelGroup, 1f, EnterDuration * 0.75f));
            sequence.Insert(EnterDuration * 0.12f, Fade(TextGroup, 1f, EnterDuration * 0.85f));
            transition = sequence.OnComplete(() => { IsEntering = false; transition = null; });
        }

        public void Exit()
        {
            transition?.Kill();
            IsEntering = false;
            Shatter?.StopAndRestore();
            Sequence sequence = DOTween.Sequence().SetUpdate(true);
            sequence.Join(Fade(PanelGroup, 0f, PanelFadeDuration));
            sequence.Join(Fade(TextGroup, 0f, PanelFadeDuration));
            transition = sequence.OnComplete(() => transition = null);
        }

        private static Tween Fade(CanvasGroup group, float alpha, float duration) =>
            DOTween.To(() => group.alpha, value => group.alpha = value, alpha, duration);

        private void OnDisable()
        {
            transition?.Kill();
            transition = null;
            IsEntering = false;
        }
    }
}
