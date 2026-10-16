using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScreenFilters.Editor
{
    public sealed class ScreenFilterPreviewWindow : EditorWindow
    {
        public const string DefaultProfilePath = "Assets/Rendering/ScreenFilters/Profiles/DefaultScreenFilters.asset";
        private static readonly string[] Sources = { "内置测试画面", "导入图片", "场景相机" };
        private static readonly string[] Resolutions = { "640", "960", "1280", "1920" };
        private static readonly int[] Widths = { 640, 960, 1280, 1920 };
        private static readonly string[] AspectNames = { "16:10", "16:9", "4:3" };
        private static readonly float[] Aspects = { 1.6f, 16f / 9f, 4f / 3f };
        [SerializeField] private ScreenFilterProfile asset;
        [SerializeField] private ScreenFilterProfile draft;
        [SerializeField] private string draftJson;
        [SerializeField] private bool dirty;
        [SerializeField] private int sourceMode, resolution = 1, aspect;
        [SerializeField] private Texture2D image;
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private float clock, divider = 0.5f;
        [SerializeField] private bool playing = true, comparison = true;
        private Vector2 scroll;
        private double lastTick;
        private FilterProfileGUI controls;
        private FilterPreviewSource source;
        private ScreenFilterRenderer renderer;
        private RenderTexture raw, filtered;

        [MenuItem("Tools/Rendering/屏幕滤镜工作台")]
        private static void OpenDefault() => Open(AssetDatabase.LoadAssetAtPath<ScreenFilterProfile>(DefaultProfilePath));

        public static void Open(ScreenFilterProfile profile)
        {
            var window = GetWindow<ScreenFilterPreviewWindow>("屏幕滤镜工作台");
            window.minSize = new Vector2(920, 600);
            if (window.asset != profile && window.CanReplaceDraft()) window.LoadProfile(profile);
            window.Show();
        }

        private void OnEnable()
        {
            source = new FilterPreviewSource();
            renderer = new ScreenFilterRenderer();
            lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            Undo.undoRedoPerformed += OnUndo;
            EnsureDraft();
        }

        private void EnsureDraft()
        {
            if (draft == null)
            {
                draft = asset != null ? Instantiate(asset) : CreateInstance<ScreenFilterProfile>();
                if (!string.IsNullOrEmpty(draftJson)) EditorJsonUtility.FromJsonOverwrite(draftJson, draft);
                draft.name = "滤镜预览草稿";
            }
            // HideAndDontSave includes NotEditable, which disables PropertyField controls.
            // Keep the transient draft hidden and unsaved, but allow serialized editing.
            draft.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            draft.Normalize();
            if (controls == null) controls = new FilterProfileGUI(new SerializedObject(draft));
        }

        private void LoadProfile(ScreenFilterProfile profile)
        {
            if (draft != null) { Undo.ClearUndo(draft); DestroyImmediate(draft); }
            asset = profile; draft = null; draftJson = null; controls = null; dirty = false;
            hasUnsavedChanges = false;
            EnsureDraft();
        }

        private bool CanReplaceDraft()
        {
            return !dirty || EditorUtility.DisplayDialog("放弃预览草稿？", "当前试调尚未应用。继续将重新载入配置。", "放弃并载入", "保留草稿");
        }

        private void MarkDirty()
        {
            dirty = true;
            hasUnsavedChanges = asset != null;
            saveChangesMessage = "滤镜试调尚未应用到共享配置。是否保存？";
            Repaint();
        }

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - lastTick < 1d / 30d) return;
            if (playing) clock = Mathf.Repeat(clock + (float)(now - lastTick), 30f);
            lastTick = now;
            if (playing || sourceMode == 2) Repaint();
        }

        private void OnUndo() { if (draft != null) MarkDirty(); }

        private void OnGUI()
        {
            EnsureDraft();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var chosen = (ScreenFilterProfile)EditorGUILayout.ObjectField(asset, typeof(ScreenFilterProfile), false, GUILayout.Width(270));
                if (chosen != asset && CanReplaceDraft()) LoadProfile(chosen);
                using (new EditorGUI.DisabledScope(asset == null || !dirty))
                    if (GUILayout.Button("应用并保存", EditorStyles.toolbarButton, GUILayout.Width(85))) Apply();
                if (GUILayout.Button("另存配置", EditorStyles.toolbarButton, GUILayout.Width(75))) SaveCopy();
                if (GUILayout.Button("重新载入", EditorStyles.toolbarButton, GUILayout.Width(75)) && CanReplaceDraft()) LoadProfile(asset);
                GUILayout.FlexibleSpace();
                GUILayout.Label(dirty ? "有未应用的试调" : "配置已同步", EditorStyles.miniLabel);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(310)))
                {
                    EditorGUILayout.HelpBox("在草稿中试调，点击“应用并保存”后游戏才使用新参数。可同时启用多个滤镜。", MessageType.None);
                    int preset = GUILayout.Toolbar(-1, new[] { "原画", "像素", "笔刷" });
                    if (preset >= 0) { FilterProfileGUI.ApplyPreset(draft, preset); MarkDirty(); }
                    preset = GUILayout.Toolbar(-1, new[] { "撕裂", "色散", "显像管" });
                    if (preset >= 0) { FilterProfileGUI.ApplyPreset(draft, preset + 3); MarkDirty(); }
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    if (controls.Draw()) MarkDirty();
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope()) DrawPreview();
            }
        }

        private void DrawPreview()
        {
            sourceMode = EditorGUILayout.Popup("预览来源", sourceMode, Sources);
            if (sourceMode == 1) image = (Texture2D)EditorGUILayout.ObjectField("图片", image, typeof(Texture2D), false);
            if (sourceMode == 2)
            {
                sceneCamera = (Camera)EditorGUILayout.ObjectField("参考相机", sceneCamera, typeof(Camera), true);
                if (GUILayout.Button("使用当前主相机")) sceneCamera = Camera.main;
                if (sceneCamera == null) EditorGUILayout.HelpBox("选择一个场景相机；未选择时显示内置测试画面。", MessageType.Info);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                resolution = EditorGUILayout.Popup("输出宽度", resolution, Resolutions);
                aspect = EditorGUILayout.Popup("画幅", aspect, AspectNames);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                playing = GUILayout.Toggle(playing, playing ? "暂停动画" : "播放动画", "Button", GUILayout.Width(85));
                clock = EditorGUILayout.Slider("时间（秒）", clock, 0f, 30f);
                if (GUILayout.Button("归零", GUILayout.Width(45))) clock = 0f;
            }
            comparison = EditorGUILayout.Toggle("原画／效果分屏", comparison);
            if (comparison) divider = EditorGUILayout.Slider("分界位置", divider, 0f, 1f);
            if (!renderer.IsSupported)
            {
                EditorGUILayout.HelpBox("滤镜 Shader 尚未导入或当前图形设备不支持，请检查 Console。", MessageType.Error);
                return;
            }
            Rect area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint)
            {
                RenderPreview();
                Rect fitted = Fit(area, (float)raw.width / raw.height);
                EditorGUI.DrawRect(area, new Color(0.035f, 0.035f, 0.035f));
                GUI.DrawTexture(fitted, comparison ? raw : filtered, ScaleMode.StretchToFill, false);
                if (comparison)
                {
                    float cut = fitted.width * divider;
                    GUI.BeginGroup(new Rect(fitted.x + cut, fitted.y, fitted.width - cut, fitted.height));
                    GUI.DrawTexture(new Rect(-cut, 0, fitted.width, fitted.height), filtered, ScaleMode.StretchToFill, false);
                    GUI.EndGroup();
                    EditorGUI.DrawRect(new Rect(fitted.x + cut, fitted.y, 1f, fitted.height), Color.white);
                }
                GUI.Label(new Rect(fitted.x + 8, fitted.y + 8, 200, 22), comparison ? "原画 ←  → 滤镜" : "滤镜效果", EditorStyles.whiteLabel);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("像素尺寸以输出分辨率为准；场景预览使用临时相机，不移动原相机。", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(filtered == null))
                    if (GUILayout.Button("导出效果 PNG", GUILayout.Width(110))) Export();
            }
        }

        private static Rect Fit(Rect area, float ratio)
        {
            float width = Mathf.Min(area.width, area.height * ratio);
            float height = width / ratio;
            return new Rect(area.center.x - width * 0.5f, area.center.y - height * 0.5f, width, height);
        }

        internal void RenderPreview()
        {
            // IMGUI is rendering the editor window here. Offscreen blits must restore
            // its render target, otherwise the remaining GUI is drawn into our texture.
            RenderTexture previousTarget = RenderTexture.active;
            bool previousSRGBWrite = GL.sRGBWrite;
            try { RenderPreviewTextures(); }
            finally
            {
                RenderTexture.active = previousTarget;
                GL.sRGBWrite = previousSRGBWrite;
            }
        }

        private void RenderPreviewTextures()
        {
            int width = Widths[Mathf.Clamp(resolution, 0, Widths.Length - 1)];
            int height = Mathf.RoundToInt(width / Aspects[Mathf.Clamp(aspect, 0, Aspects.Length - 1)]);
            if (raw == null || raw.width != width || raw.height != height)
            {
                ReleaseTextures();
                raw = NewTexture(width, height, "Filter Preview Original", 24);
                filtered = NewTexture(width, height, "Filter Preview Result", 0);
            }
            if (sourceMode == 2 && sceneCamera != null) source.RenderScene(sceneCamera, raw);
            else Graphics.Blit(sourceMode == 1 && image != null ? image : source.Chart, raw);
            var commands = CommandBufferPool.Get("Screen filter preview");
            try
            {
                renderer.Record(commands, raw, filtered, filtered.descriptor, draft, clock);
                Graphics.ExecuteCommandBuffer(commands);
            }
            finally { CommandBufferPool.Release(commands); }
        }

        private static RenderTexture NewTexture(int width, int height, string label, int depth)
        {
            var texture = new RenderTexture(width, height, depth, RenderTextureFormat.ARGB32)
                { name = label, hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1, wrapMode = TextureWrapMode.Clamp };
            texture.Create();
            return texture;
        }

        private void Apply()
        {
            if (asset == null) return;
            Undo.RecordObject(asset, "应用屏幕滤镜配置");
            string name = asset.name;
            HideFlags flags = asset.hideFlags;
            EditorUtility.CopySerialized(draft, asset);
            asset.name = name; asset.hideFlags = flags; asset.Normalize();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            dirty = false; hasUnsavedChanges = false;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        public override void SaveChanges() { Apply(); base.SaveChanges(); }

        private void SaveCopy()
        {
            string path = EditorUtility.SaveFilePanelInProject("另存滤镜配置", "ScreenFilters", "asset", "选择新配置的保存位置");
            if (string.IsNullOrEmpty(path)) return;
            var copy = Instantiate(draft);
            copy.name = Path.GetFileNameWithoutExtension(path); copy.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssetIfDirty(copy);
            LoadProfile(copy);
        }

        private void Export()
        {
            string path = EditorUtility.SaveFilePanel("导出滤镜效果", "", "ScreenFilters.png", "png");
            if (string.IsNullOrEmpty(path)) return;
            RenderTexture previous = RenderTexture.active;
            var image = new Texture2D(filtered.width, filtered.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = filtered;
                image.ReadPixels(new Rect(0, 0, filtered.width, filtered.height), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; DestroyImmediate(image); }
        }

        private void ReleaseTextures()
        {
            if (raw != null) { raw.Release(); DestroyImmediate(raw); }
            if (filtered != null) { filtered.Release(); DestroyImmediate(filtered); }
            raw = null; filtered = null;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            Undo.undoRedoPerformed -= OnUndo;
            ReleaseTextures();
            renderer?.Dispose(); renderer = null;
            source?.Dispose(); source = null;
            if (draft != null)
            {
                draftJson = EditorJsonUtility.ToJson(draft);
                Undo.ClearUndo(draft); DestroyImmediate(draft);
            }
            draft = null; controls = null;
        }
    }
}
