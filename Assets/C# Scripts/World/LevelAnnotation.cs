using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TapTap
{
    // Instantiated by the test scene builder only; ordinary region prefabs omit annotations.
    [RequireComponent(typeof(BoxCollider2D))]
    [DefaultExecutionOrder(-1000)]
    public sealed class LevelAnnotation : MonoBehaviour
    {
        public string Text = "";
        public Font TextFont;
        public PlayerController Player;
        public LevelDefinition Source;
        public string PlacementId;
        private static readonly List<LevelAnnotation> annotations = new List<LevelAnnotation>();
        private static LevelAnnotation activeFeedback;
        public static bool IsFeedbackOpen => activeFeedback != null;
        private GameObject feedbackCanvas;
        private GameObject ownedEventSystem;
        private InputField experienceInput, ideasInput, designInput;
        private Text saveStatus;
        private float previousTimeScale;
        private bool feedbackDirty;
        private float saveAt;
        private string savedFeedback;
        private string feedbackTimestamp;
        private BoxCollider2D trigger;
        private GUIStyle textStyle;
        private Vector2 scroll;
        public bool IsShowing { get; private set; }
        public string DisplayText => string.IsNullOrWhiteSpace(Text) ? "尚未编辑该批注" : Text;

        private void Awake()
        {
            trigger = GetComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            ResolveSource();
            RefreshText();
        }
        private void OnEnable() => annotations.Add(this);
        private void OnDisable()
        {
            CloseFeedback();
            annotations.Remove(this);
            IsShowing = false;
        }

        // Older generated scenes lack the source ID. Recover it from the region and grid position.
        private void ResolveSource()
        {
            LevelRegion region = GetComponentInParent<LevelRegion>();
            if (Source == null && region != null) Source = region.Source;
            if (Source == null || Source.Placements == null || !string.IsNullOrEmpty(PlacementId) || region == null) return;
            Vector2 position = region.transform.InverseTransformPoint(transform.position);
            foreach (LevelPlacement placement in Source.Placements)
            {
                if (placement?.Brush == null || !(placement.Settings is AnnotationPlacementSettings)) continue;
                Vector2 expected = ((Vector2)placement.Cell + placement.Brush.Anchor) * region.UnitSize;
                if ((expected - position).sqrMagnitude > 0.0001f) continue;
                PlacementId = placement.Id;
                break;
            }
        }

        private AnnotationPlacementSettings SourceSettings => Source != null && Source.Placements != null
            ? Source.Placements.Find(item => item != null && item.Id == PlacementId)?.Settings as AnnotationPlacementSettings
            : null;

        private void RefreshText()
        {
            AnnotationPlacementSettings settings = SourceSettings;
            if (settings != null) Text = settings.Text;
        }

        private void Update()
        {
            if (activeFeedback == this)
            {
                if (feedbackDirty && Time.unscaledTime >= saveAt) SaveFeedback();
                if (Input.GetKeyDown(KeyCode.Escape)) CloseFeedback();
                return;
            }
            if (IsFeedbackOpen || !Input.GetKeyDown(KeyCode.Space)) return;
            LevelAnnotation nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (LevelAnnotation annotation in annotations)
            {
                if (annotation.Player == null || !annotation.trigger.enabled ||
                    !(annotation.Overlaps(annotation.Player.Body) || annotation.Overlaps(annotation.Player.Head))) continue;
                float distance = (annotation.transform.position - (Vector3)annotation.Player.PresentationPosition).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearest = annotation;
                nearestDistance = distance;
            }
            if (nearest == this) OpenFeedback();
        }

        private void OpenFeedback()
        {
            RefreshText();
            activeFeedback = this;
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            savedFeedback = null;
            feedbackTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (EventSystem.current == null)
                ownedEventSystem = new GameObject("Annotation EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            feedbackCanvas = new GameObject("Annotation Feedback", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = feedbackCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            CanvasScaler scaler = feedbackCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960f, 720f);
            scaler.matchWidthOrHeight = 1f;
            Image shade = CreateUI<Image>("Backdrop", feedbackCanvas.transform);
            Place(shade.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            shade.color = new Color(0f, 0f, 0f, 0.7f);
            Image panel = CreateUI<Image>("Panel", shade.transform);
            Place(panel.rectTransform, new Vector2(0.08f, 0.04f), new Vector2(0.92f, 0.96f), Vector2.zero, Vector2.zero);
            panel.color = new Color(0.12f, 0.15f, 0.2f);
            AddText(panel.transform, "测试批注 · 体验评价", 26, 0.91f, 0.98f);
            AddText(panel.transform, "对应体验是否感受到？有没有更好的想法？整体设计是否合理？", 17, 0.86f, 0.92f);
            AddText(panel.transform, DisplayText, 17, 0.72f, 0.86f, true);
            experienceInput = AddInput(panel.transform, "体验感受", "例如：感受到了／没感受到，原因是……", 0.52f, 0.69f);
            ideasInput = AddInput(panel.transform, "改进想法", "这里还可以尝试……", 0.32f, 0.49f);
            designInput = AddInput(panel.transform, "整体设计评价", "合理／不合理的地方，以及理由……", 0.12f, 0.29f);
            saveStatus = AddText(panel.transform, "输入后自动保存到原始批注；Esc 返回体验", 16, 0.06f, 0.12f);
#if !UNITY_EDITOR
            saveStatus.text = "评价回写需要在 Unity 编辑器的 Play 模式中进行。";
            experienceInput.interactable = ideasInput.interactable = designInput.interactable = false;
#else
            if (SourceSettings == null || !UnityEditor.AssetDatabase.Contains(Source))
            {
                saveStatus.text = "无法找到原始批注，请从关卡编辑器重新生成测试场景。";
                experienceInput.interactable = ideasInput.interactable = designInput.interactable = false;
            }
#endif
            Image buttonImage = CreateUI<Image>("Close", panel.transform);
            Place(buttonImage.rectTransform, new Vector2(0.7f, 0.01f), new Vector2(0.96f, 0.06f), Vector2.zero, Vector2.zero);
            buttonImage.color = new Color(0.22f, 0.38f, 0.5f);
            Button button = buttonImage.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(CloseFeedback);
            AddText(button.transform, "保存并继续体验", 17, 0f, 1f).alignment = TextAnchor.MiddleCenter;
            experienceInput.ActivateInputField();
        }

        private T CreateUI<T>(string name, Transform parent) where T : Component
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            return obj.AddComponent<T>();
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 insetMin, Vector2 insetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = insetMin;
            rect.offsetMax = insetMax;
        }

        private Text AddText(Transform parent, string content, int size, float bottom, float top, bool scrollable = false)
        {
            Text label = CreateUI<Text>("Text", parent);
            label.font = TextFont != null ? TextFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = size;
            label.color = Color.white;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.text = content;
            Place(label.rectTransform, new Vector2(0.04f, bottom), new Vector2(0.96f, top), Vector2.zero, Vector2.zero);
            if (scrollable)
            {
                Image viewport = CreateUI<Image>("Annotation Scroll", parent);
                Place(viewport.rectTransform, label.rectTransform.anchorMin, label.rectTransform.anchorMax, Vector2.zero, Vector2.zero);
                viewport.color = new Color(0.08f, 0.1f, 0.14f);
                viewport.gameObject.AddComponent<RectMask2D>();
                label.transform.SetParent(viewport.transform, false);
                Place(label.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f));
                label.rectTransform.pivot = new Vector2(0.5f, 1f);
                ContentSizeFitter fitter = label.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                ScrollRect scrollRect = viewport.gameObject.AddComponent<ScrollRect>();
                scrollRect.viewport = viewport.rectTransform;
                scrollRect.content = label.rectTransform;
                scrollRect.horizontal = false;
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
                scrollRect.scrollSensitivity = 25f;
            }
            return label;
        }

        private InputField AddInput(Transform parent, string title, string placeholder, float bottom, float top)
        {
            AddText(parent, title, 18, top - 0.045f, top);
            Image background = CreateUI<Image>(title, parent);
            Place(background.rectTransform, new Vector2(0.04f, bottom), new Vector2(0.96f, top - 0.05f), Vector2.zero, Vector2.zero);
            background.color = new Color(0.95f, 0.96f, 0.98f);
            background.gameObject.AddComponent<RectMask2D>();
            InputField input = background.gameObject.AddComponent<InputField>();
            Text value = AddText(background.transform, "", 18, 0f, 1f);
            value.color = new Color(0.08f, 0.1f, 0.14f);
            value.alignment = TextAnchor.UpperLeft;
            Text hint = AddText(background.transform, placeholder, 18, 0f, 1f);
            hint.color = Color.gray;
            Place(value.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 7f), new Vector2(-10f, -7f));
            Place(hint.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 7f), new Vector2(-10f, -7f));
            input.textComponent = value;
            input.placeholder = hint;
            input.targetGraphic = background;
            input.lineType = InputField.LineType.MultiLineNewline;
            input.onValueChanged.AddListener(_ =>
            {
                feedbackDirty = true;
                saveAt = Time.unscaledTime + 0.5f;
                saveStatus.text = "正在输入，稍后自动保存……";
            });
            input.onEndEdit.AddListener(_ => SaveFeedback());
            return input;
        }

        private void SaveFeedback()
        {
            if (!feedbackDirty || experienceInput == null) return;
#if UNITY_EDITOR
            AnnotationPlacementSettings settings = SourceSettings;
            if (settings == null || !UnityEditor.AssetDatabase.Contains(Source))
            {
                saveStatus.text = "保存失败：找不到原始批注。";
                return;
            }
            string current = settings.Text ?? "";
            // Replace only this window's last saved entry; preserve original text and earlier reviews.
            if (!string.IsNullOrEmpty(savedFeedback))
            {
                int index = current.LastIndexOf(savedFeedback, StringComparison.Ordinal);
                if (index >= 0) current = current.Remove(index, savedFeedback.Length);
            }
            string entry = "";
            if (!string.IsNullOrWhiteSpace(experienceInput.text) || !string.IsNullOrWhiteSpace(ideasInput.text) ||
                !string.IsNullOrWhiteSpace(designInput.text))
                entry = "\n\n【体验评价 " + feedbackTimestamp + "】\n体验感受：" + experienceInput.text.Trim() +
                    "\n改进想法：" + ideasInput.text.Trim() + "\n整体设计：" + designInput.text.Trim();
            try
            {
                if (current + entry != settings.Text)
                {
                    UnityEditor.Undo.RegisterCompleteObjectUndo(Source, "保存批注体验评价");
                    settings.Text = current + entry;
                    UnityEditor.EditorUtility.SetDirty(Source);
                }
                savedFeedback = entry;
                Text = settings.Text;
                UnityEditor.AssetDatabase.SaveAssetIfDirty(Source);
                feedbackDirty = false;
                saveStatus.text = "已保存到原始批注；Esc 返回体验";
            }
            catch (Exception exception)
            {
                saveStatus.text = "保存失败，请保留窗口并重试。";
                saveAt = Time.unscaledTime + 1f;
                Debug.LogException(exception, this);
            }
#endif
        }

        private void CloseFeedback()
        {
            if (activeFeedback != this) return;
            SaveFeedback();
            // Keep failed drafts visible when the user closes the window normally.
            if (feedbackDirty && isActiveAndEnabled) return;
            activeFeedback = null;
            Time.timeScale = previousTimeScale;
            if (feedbackCanvas != null) { feedbackCanvas.SetActive(false); Destroy(feedbackCanvas); }
            if (ownedEventSystem != null) Destroy(ownedEventSystem);
        }
        private void LateUpdate() => IsShowing = Player != null && trigger.enabled &&
            (Overlaps(Player.Body) || Overlaps(Player.Head));

        // The player uses kinematic bodies: query geometry directly, including teleport/respawn.
        private bool Overlaps(MovableEntity entity)
        {
            if (entity == null || !entity.gameObject.activeInHierarchy) return false;
            Collider2D collider = entity.GetComponent<Collider2D>();
            if (collider == null || !collider.enabled) return false;
            ColliderDistance2D distance = trigger.Distance(collider);
            return distance.isValid && distance.distance <= 0f;
        }

        private void OnGUI()
        {
            if (!IsShowing || IsFeedbackOpen || Camera.main == null) return;
            RefreshText();
            Vector3 screen = Camera.main.WorldToScreenPoint(transform.position);
            if (screen.z <= 0f) return;
            if (textStyle == null)
                textStyle = new GUIStyle(GUI.skin.label)
                {
                    font = TextFont, fontSize = 18, wordWrap = true, richText = false,
                    normal = { textColor = Color.white }
                };
            float width = Mathf.Min(360f, Screen.width - 20f);
            float height = Mathf.Min(220f, Mathf.Max(85f, textStyle.CalcHeight(new GUIContent(DisplayText), width - 40f) + 48f));
            height = Mathf.Min(height, Screen.height - 20f);
            Rect rect = new Rect(Mathf.Clamp(screen.x - width * 0.5f, 10f, Screen.width - width - 10f),
                Mathf.Clamp(Screen.height - screen.y - height - 24f, 10f, Screen.height - height - 10f), width, height);
            Color previous = GUI.color;
            GUI.color = new Color(0.12f, 0.04f, 0.04f, 0.96f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, rect.height - 16f));
            GUILayout.Label("! 测试批注 · [空格] 输入评价", textStyle);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(DisplayText, textStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.color = previous;
        }
    }
}
