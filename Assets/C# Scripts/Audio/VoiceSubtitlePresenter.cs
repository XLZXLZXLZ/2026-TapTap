using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TapTap
{
    [DisallowMultipleComponent, RequireComponent(typeof(Canvas), typeof(CanvasScaler))]
    [DefaultExecutionOrder(950)]
    public sealed class VoiceSubtitlePresenter : MonoBehaviour
    {
        public const string ResourcePath = "UI/VoiceSubtitles";
        public CanvasGroup Group;
        public TextMeshProUGUI Label;
        public RectTransform Panel;
        [Min(0f)] public float FadeInDuration = 0.18f;
        [Min(0f)] public float FadeOutDuration = 0.18f;
        public Vector2 Padding = new Vector2(32f, 18f);
        public VoicePlayer Owner { get; private set; }
        public string DisplayedText => Label != null ? Label.text : "";

        public static VoiceSubtitlePresenter Ensure(Scene scene, VoiceSubtitlePresenter prefab = null)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (VoiceSubtitlePresenter existing in root.GetComponentsInChildren<VoiceSubtitlePresenter>(true))
                    if (existing.isActiveAndEnabled) return existing;
            if (prefab == null) prefab = Resources.Load<VoiceSubtitlePresenter>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogWarning("缺少 VoiceSubtitles 预制体，请运行 TapTap/语音与收音机/创建缺失资源。");
                return null;
            }
            VoiceSubtitlePresenter instance = Instantiate(prefab);
            instance.name = "Voice Subtitles";
            SceneManager.MoveGameObjectToScene(instance.gameObject, scene);
            return instance;
        }

        private void Awake()
        {
            if (Group != null)
            {
                Group.alpha = 0f;
                Group.interactable = Group.blocksRaycasts = false;
            }
            if (Label != null) { Label.text = ""; Label.raycastTarget = false; }
        }

        public void Bind(VoicePlayer player) => Owner = player;
        public void Release(VoicePlayer player) { if (Owner == player) Owner = null; }
        public void SetLanguage(SubtitleLanguage language) => SubtitleLanguageSettings.SetLanguage(language);

        private void LateUpdate()
        {
            if (Group == null || Label == null || Panel == null) return;
            string wanted = Owner != null ? Owner.CurrentSubtitle : "";
            if (!string.IsNullOrWhiteSpace(wanted))
            {
                // Adjacent cues and runtime language switches replace text immediately;
                // fading the previous line out first can swallow short cues entirely.
                if (wanted != Label.text) SetText(wanted);
                Group.alpha = Fade(Group.alpha, 1f, FadeInDuration);
            }
            else
            {
                Group.alpha = Fade(Group.alpha, 0f, FadeOutDuration);
                if (Group.alpha <= 0f && Label.text != "") SetText("");
            }
        }

        private void SetText(string text)
        {
            Label.text = text;
            float width = Mathf.Max(100f, Panel.rect.width - Padding.x * 2f);
            float height = Label.GetPreferredValues(text, width, 0f).y;
            Panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(58f, height + Padding.y * 2f));
        }

        private static float Fade(float value, float target, float duration) => duration <= 0f ? target
            : Mathf.MoveTowards(value, target, Time.unscaledDeltaTime / duration);

        private void OnDisable()
        {
            Owner = null;
            if (Group != null) Group.alpha = 0f;
            if (Label != null) Label.text = "";
        }
    }
}
