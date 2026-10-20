using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TapTap.Hints.Editor
{
    public static class HintReplayUiAssets
    {
        public const string PausePrefabPath = "Assets/Prefabs/UI/PauseMenu.prefab";
        public const string Question = "要偷看一眼这关的解法吗?";
        private const string Art = "Assets/Art/Sprite/UI/CheatPanel/";

        [MenuItem("TapTap/UI/Hints/Build Hint Confirmation HUD")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring UI.");
            GameObject root = PrefabUtility.LoadPrefabContents(PausePrefabPath);
            try
            {
                PauseMenu pause = root.GetComponent<PauseMenu>();
                if (pause == null || pause.OpenButton == null) throw new InvalidOperationException("Pause prefab has no HUD icon.");
                var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/UI/PauseIconOutline.mat");
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/text(SMALL) SDF.asset");
                if (material == null || font == null) throw new InvalidOperationException("Missing outline material or requested TMP font.");
                EnsureHover(pause.OpenButton, pause.OpenButton.targetGraphic, material);
                foreach (string ownedName in new[] { "HintButton", "HintReplayPrompt" })
                {
                    Transform old = root.transform.Find(ownedName);
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                }
                HintReplayPanel view = root.GetComponent<HintReplayPanel>();
                if (view == null) view = root.AddComponent<HintReplayPanel>();

                RawImage icon = ArtImage("HintButton", root.transform, "6313def7a851145c65ce82d7e4fe69c8.png",
                    new Rect(64f, 30f, 260f, 359f));
                var iconRect = icon.rectTransform;
                RectTransform pauseRect = (RectTransform)pause.OpenButton.transform;
                iconRect.anchorMin = iconRect.anchorMax = Vector2.one;
                iconRect.sizeDelta = new Vector2(80f * 260f / 359f, 80f);
                iconRect.anchoredPosition = new Vector2(pauseRect.anchoredPosition.x - pauseRect.sizeDelta.x * 0.5f - 24f - iconRect.sizeDelta.x * 0.5f,
                    pauseRect.anchoredPosition.y);
                Button open = AddButton(icon);
                open.transition = Selectable.Transition.None;
                EnsureHover(open, icon, material);

                RectTransform prompt = Rect("HintReplayPrompt", root.transform);
                Stretch(prompt);
                CanvasGroup group = prompt.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false;
                RectTransform dismissRect = Rect("DismissOutside", prompt);
                Stretch(dismissRect);
                Image dismissGraphic = dismissRect.gameObject.AddComponent<Image>();
                dismissGraphic.color = new Color(0f, 0f, 0f, 0f);
                Button dismiss = AddButton(dismissGraphic);
                dismiss.transition = Selectable.Transition.None;

                RawImage panel = ArtImage("SubPanel", prompt, "TipSubPanel.png", new Rect(73f, 37f, 1307f, 145f));
                panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = Vector2.one;
                panel.rectTransform.pivot = Vector2.one;
                panel.rectTransform.sizeDelta = new Vector2(650f, 224f);
                panel.rectTransform.anchoredPosition = new Vector2(-30f, -132f);
                panel.color = new Color(1f, 1f, 1f, 0.68f);
                panel.raycastTarget = true;

                RectTransform captionRect = Rect("Question", panel.transform);
                captionRect.anchorMin = captionRect.anchorMax = new Vector2(0.5f, 1f);
                captionRect.anchoredPosition = new Vector2(0f, -60f);
                captionRect.sizeDelta = new Vector2(570f, 58f);
                TextMeshProUGUI caption = captionRect.gameObject.AddComponent<TextMeshProUGUI>();
                caption.font = font;
                caption.text = Question;
                caption.fontSize = 32f;
                caption.alignment = TextAlignmentOptions.Center;
                caption.enableWordWrapping = false;
                caption.color = new Color(1f, 1f, 1f, 0.96f);
                caption.raycastTarget = false;

                RawImage yes = ArtImage("Confirm", panel.transform, "1d68fe573cecdc9573f289e584a1c565.png",
                    new Rect(1309f, 418f, 274f, 105f));
                RawImage no = ArtImage("Cancel", panel.transform, "c1d5dff9bd4a988c4c6610aed735cf8d.png",
                    new Rect(1312f, 559f, 271f, 107f));
                ConfigureChoice(yes, -128f);
                ConfigureChoice(no, 128f);
                Button confirm = AddButton(yes);
                Button cancel = AddButton(no);
                yes.gameObject.AddComponent<UIInteractionScale>();
                no.gameObject.AddComponent<UIInteractionScale>();
                view.Director = null;
                view.OpenButton = open; view.ConfirmButton = confirm; view.CancelButton = cancel; view.DismissButton = dismiss;
                view.IconGraphic = icon; view.PanelRoot = prompt.gameObject; view.PanelGroup = group;
                view.FadeDuration = 0.16f;
                prompt.gameObject.SetActive(false);
                root.GetComponent<Canvas>().additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
                PrefabUtility.SaveAsPrefabAsset(root, PausePrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureChoice(RawImage image, float x)
        {
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            image.rectTransform.anchoredPosition = new Vector2(x, -151f);
            image.rectTransform.sizeDelta = new Vector2(220f, 82f);
            image.color = new Color(1f, 1f, 1f, 0.78f);
        }
        private static void EnsureHover(Button button, Graphic graphic, Material material)
        {
            graphic.material = material;
            PauseIconOutline outline = graphic.GetComponent<PauseIconOutline>();
            if (outline == null) outline = graphic.gameObject.AddComponent<PauseIconOutline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0f);
            outline.effectDistance = new Vector2(3f, -3f);
            outline.useGraphicAlpha = false;
            if (graphic.GetComponent<PauseIconHover>() == null) graphic.gameObject.AddComponent<PauseIconHover>();
            if (button.GetComponent<UIInteractionScale>() == null) button.gameObject.AddComponent<UIInteractionScale>();
        }
        private static Button AddButton(Graphic graphic)
        {
            Button button = graphic.gameObject.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.82f, 0.88f, 0.94f, 0.85f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.1f;
            button.colors = colors;
            return button;
        }
        private static RawImage ArtImage(string name, Transform parent, string filename, Rect topLeftCrop)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + filename);
            if (texture == null) throw new InvalidOperationException("Missing hint art: " + filename);
            RawImage image = Rect(name, parent).gameObject.AddComponent<RawImage>();
            image.texture = texture;
            // Crop transparent source margins in UV space; keep the supplied pixels/import settings intact.
            image.uvRect = new Rect(topLeftCrop.x / texture.width, (texture.height - topLeftCrop.yMax) / texture.height,
                topLeftCrop.width / texture.width, topLeftCrop.height / texture.height);
            return image;
        }
        private static RectTransform Rect(string name, Transform parent)
        {
            var owner = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)) { layer = 5 };
            owner.transform.SetParent(parent, false);
            return (RectTransform)owner.transform;
        }
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
