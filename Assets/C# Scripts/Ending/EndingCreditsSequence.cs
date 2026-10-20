using System.Collections;
using UnityEngine;

namespace TapTap
{
    /// <summary>Three editable credit panels played above the persistent white cover.</summary>
    [DisallowMultipleComponent]
    public sealed class EndingCreditsSequence : MonoBehaviour
    {
        [Header("面板引用")]
        public CanvasGroup StudioPanel;
        public CanvasGroup AuthorsPanel;
        public CanvasGroup ThanksPanel;
        [Tooltip("从上到下：特别鸣谢、游玩本作的、你。")]
        public CanvasGroup[] ThanksLines = new CanvasGroup[0];

        [Header("播放时间（秒，不受 Time Scale 影响）")]
        [Min(0f)] public float FadeInDuration = 1.2f;
        [Min(0f)] public float FadeOutDuration = 1.2f;
        [Min(0f)] public float StudioHoldDuration = 3f;
        [Min(0f)] public float AuthorsHoldDuration = 4.5f;
        [Min(0f)] public float PanelGapDuration = 0.5f;
        [Min(0f)] public float ThanksLineGapDuration = 0.45f;
        [Min(0f)] public float ThanksHoldDuration = 3.5f;

        private void Awake() => ResetPanels();

        private void ResetPanels()
        {
            Hide(StudioPanel);
            Hide(AuthorsPanel);
            Hide(ThanksPanel);
            if (ThanksLines == null) return;
            foreach (CanvasGroup line in ThanksLines)
                if (line != null) line.alpha = 0f;
        }

        public IEnumerator Play()
        {
            ResetPanels();
            yield return ShowPanel(StudioPanel, StudioHoldDuration);
            yield return Wait(PanelGapDuration);
            yield return ShowPanel(AuthorsPanel, AuthorsHoldDuration);
            yield return Wait(PanelGapDuration);
            if (ThanksPanel == null) yield break;

            ThanksPanel.gameObject.SetActive(true);
            ThanksPanel.alpha = 1f;
            if (ThanksLines != null)
                for (int index = 0; index < ThanksLines.Length; index++)
                {
                    CanvasGroup line = ThanksLines[index];
                    if (line == null) continue;
                    line.gameObject.SetActive(true);
                    yield return Fade(line, 1f, FadeInDuration);
                    if (index + 1 < ThanksLines.Length) yield return Wait(ThanksLineGapDuration);
                }
            yield return Wait(ThanksHoldDuration);
            // Fade their parent so all three lines disappear together.
            yield return Fade(ThanksPanel, 0f, FadeOutDuration);
            Hide(ThanksPanel);
        }

        private IEnumerator ShowPanel(CanvasGroup panel, float hold)
        {
            if (panel == null) yield break;
            panel.gameObject.SetActive(true);
            yield return Fade(panel, 1f, FadeInDuration);
            yield return Wait(hold);
            yield return Fade(panel, 0f, FadeOutDuration);
            Hide(panel);
        }

        private static void Hide(CanvasGroup group)
        {
            if (group == null) return;
            group.alpha = 0f;
            group.gameObject.SetActive(false);
        }

        private static IEnumerator Wait(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration) { elapsed += Time.unscaledDeltaTime; yield return null; }
        }

        private static IEnumerator Fade(CanvasGroup group, float target, float duration)
        {
            float from = group.alpha;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.SmoothStep(from, target, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            group.alpha = target;
        }
    }
}
