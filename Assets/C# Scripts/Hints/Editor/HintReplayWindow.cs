using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Hints.Editor
{
    public sealed class HintReplayWindow : EditorWindow
    {
        private const string DraftPath = "Library/HintReplay/RecordingDraft.json";
        [SerializeField] private PlayerController player;
        [SerializeField] private Transform referenceFrame;
        [SerializeField] private HintReplayCatalog catalog;
        [SerializeField] private HintReplayClip previewClip, savedClip;
        [SerializeField] private LevelDefinition recordedLevel;
        [SerializeField] private int samplesPerSecond = 30;
        [SerializeField] private float maximumDuration = 180f;
        [SerializeField] private HintReplayRecorder recorder;
        private HintReplayClip draft;
        private HintReplayPlayer preview;
        private int observedRevision;
        private Vector2 scroll;
        private string message;

        [MenuItem("Tools/TapTap/Hints/Recorder")]
        public static void Open() => GetWindow<HintReplayWindow>("虚影提示录制");

        [MenuItem("Tools/TapTap/Hints/Start or Stop Recording _F8")]
        public static void ToggleRecording()
        {
            var window = GetWindow<HintReplayWindow>("虚影提示录制", false);
            if (!EditorApplication.isPlaying) { window.message = "请先进入 Play 模式。"; return; }
            if (window.recorder != null && window.recorder.IsRecording) window.StopRecording();
            else window.StartRecording();
        }

        [MenuItem("Tools/TapTap/Hints/Add Scene Setup")]
        public static void AddSceneSetup()
        {
            Scene scene = SceneManager.GetActiveScene();
            HintReplayDirector existing = HintReplayContext.Find<HintReplayDirector>(scene);
            if (existing != null) { Selection.activeGameObject = existing.gameObject; return; }
            var owner = new GameObject("Hint Replay System");
            Undo.RegisterCreatedObjectUndo(owner, "Add hint replay system");
            Undo.AddComponent<HintReplayDirector>(owner);
            Undo.AddComponent<HintReplayRecorder>(owner);
            Selection.activeGameObject = owner;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        [MenuItem("GameObject/TapTap/Hints/Return Hint Trigger", false, 20)]
        public static void AddTrigger(MenuCommand command)
        {
            var owner = new GameObject("Return Hint Trigger");
            GameObjectUtility.SetParentAndAlign(owner, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(owner, "Add return hint trigger");
            var trigger = Undo.AddComponent<HintReplayReturnTrigger>(owner);
            trigger.GetComponent<BoxCollider2D>().size = new Vector2(2f, 3f);
            Selection.activeGameObject = owner;
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += PlayModeChanged;
            EditorApplication.update += Poll;
            if (recorder != null) { observedRevision = recorder.Revision; AttachRecorder(); }
        }
        private void OnDisable()
        {
            if (recorder != null && recorder.IsRecording) StopRecording();
            if (recorder != null) recorder.Stopped -= RecordingStopped;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            EditorApplication.update -= Poll;
            StopPreview();
            if (draft != null) DestroyImmediate(draft);
        }
        private void PlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode && recorder != null)
            {
                StopRecording();
                StopPreview();
            }
        }
        private void Poll()
        {
            if (!EditorApplication.isPlaying || recorder == null) return;
            if (!recorder.IsRecording && recorder.Revision != observedRevision) StoreDraft();
            if (recorder.IsRecording || preview != null) Repaint();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("进入 Play 后按 F8 开始 / 结束录制。草稿会保留到退出 Play 后，再保存成资源。", MessageType.Info);
            using (new EditorGUI.DisabledScope(recorder != null && recorder.IsRecording))
            {
                player = (PlayerController)EditorGUILayout.ObjectField("玩家（可留空）", player, typeof(PlayerController), true);
                referenceFrame = (Transform)EditorGUILayout.ObjectField("正向 / 旧版预览参考点", referenceFrame, typeof(Transform), true);
                EditorGUILayout.HelpBox("返程自动使用世界坐标。Trigger 只切换录像，移动 Trigger 不会移动新录制的返程录像。", MessageType.None);
                samplesPerSecond = EditorGUILayout.IntSlider("采样上限 / 秒", samplesPerSecond, 10, 60);
                maximumDuration = Mathf.Max(1f, EditorGUILayout.FloatField("最大时长（秒）", maximumDuration));
            }
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                if (GUILayout.Button(recorder != null && recorder.IsRecording ? "结束录制（F8）" : "开始录制（F8）"))
                {
                    if (recorder != null && recorder.IsRecording) StopRecording();
                    else StartRecording();
                }
            if (recorder != null)
                EditorGUILayout.LabelField($"{recorder.Status} · {recorder.Duration:F2} 秒 · {recorder.FrameCount} 帧", EditorStyles.wordWrappedLabel);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);

            EditorGUILayout.Space();
            EnsureDraft();
            if (draft != null) EditorGUILayout.LabelField($"草稿：{draft.Duration:F2} 秒，{draft.Frames.Length} 帧，{draft.Nodes.Length} 个显示节点");
            using (new EditorGUI.DisabledScope(draft == null || recorder != null && recorder.IsRecording))
                if (GUILayout.Button("保存草稿为录像资源…")) SaveClip();
            catalog = (HintReplayCatalog)EditorGUILayout.ObjectField("正向录像目录", catalog, typeof(HintReplayCatalog), false);
            recordedLevel = (LevelDefinition)EditorGUILayout.ObjectField("绑定关卡", recordedLevel, typeof(LevelDefinition), false);
            savedClip = (HintReplayClip)EditorGUILayout.ObjectField("待绑定录像", savedClip, typeof(HintReplayClip), false);
            using (new EditorGUI.DisabledScope(catalog == null || recordedLevel == null || savedClip == null))
                if (GUILayout.Button("将录像绑定到此关卡")) BindClip();

            EditorGUILayout.Space();
            previewClip = (HintReplayClip)EditorGUILayout.ObjectField("预览录像（空则用草稿）", previewClip, typeof(HintReplayClip), false);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || recorder != null && recorder.IsRecording))
            {
                if (GUILayout.Button("在场景中预览")) StartPreview();
                if (preview != null && preview.IsPlaying)
                {
                    float time = EditorGUILayout.Slider("播放进度", preview.Playhead, 0f, preview.PlayingClip.Duration);
                    if (Mathf.Abs(time - preview.Playhead) > 0.001f) preview.Seek(time);
                }
                if (GUILayout.Button("停止预览")) StopPreview();
            }
            EditorGUILayout.EndScrollView();
        }

        private void StartRecording()
        {
            StopPreview();
            Scene scene = player != null ? player.gameObject.scene : SceneManager.GetActiveScene();
            if (recorder == null) recorder = HintReplayContext.Find<HintReplayRecorder>(scene);
            if (recorder == null)
            {
                var owner = new GameObject("Hint Replay Recorder (Play Mode)");
                SceneManager.MoveGameObjectToScene(owner, scene);
                recorder = owner.AddComponent<HintReplayRecorder>();
            }
            AttachRecorder();
            recorder.Player = player;
            recorder.ReferenceFrame = referenceFrame;
            recorder.SamplesPerSecond = samplesPerSecond;
            recorder.MaximumDuration = maximumDuration;
            if (recorder.BeginRecording())
            {
                observedRevision = recorder.Revision;
                LevelRegion region = HintReplayContext.Region(scene, recorder.Player);
                recordedLevel = region != null ? region.Source : null;
                message = recorder.RecordsWorldSpace ? "正在录制返程：使用世界坐标，无需配置 Trigger 参考点。" : "正在录制正向：以当前关卡为参考点。";
            }
            else message = recorder.Status;
        }
        private void StopRecording()
        {
            recorder.StopRecording();
            if (observedRevision != recorder.Revision) StoreDraft();
        }
        private void AttachRecorder()
        {
            recorder.Stopped -= RecordingStopped;
            recorder.Stopped += RecordingStopped;
        }
        private void RecordingStopped(HintReplayRecorder source) => StoreDraft();
        private void StoreDraft()
        {
            observedRevision = recorder.Revision;
            HintReplayClip result = recorder.CreateClip();
            if (result == null) { message = "录制太短，尚不足两个姿态帧；之前的草稿仍保留。"; return; }
            try
            {
                result.Notes = "坐标空间：" + (recorder.RecordsWorldSpace ? "世界坐标" : "相对参考点")
                    + "\n参考点：" + (recorder.RecordingReferenceFrame != null ? recorder.RecordingReferenceFrame.name : "世界原点")
                    + "\n场景：" + recorder.gameObject.scene.path;
                Directory.CreateDirectory(Path.GetDirectoryName(DraftPath));
                File.WriteAllText(DraftPath, EditorJsonUtility.ToJson(result));
                if (draft != null) DestroyImmediate(draft);
                draft = result;
                message = "草稿已保留，可退出 Play 后保存。";
                Repaint();
            }
            catch (Exception error) { DestroyImmediate(result); message = "草稿写入失败：" + error.Message; }
        }
        private void EnsureDraft()
        {
            if (draft != null || !File.Exists(DraftPath)) return;
            draft = CreateInstance<HintReplayClip>();
            try { EditorJsonUtility.FromJsonOverwrite(File.ReadAllText(DraftPath), draft); }
            catch (Exception error) { DestroyImmediate(draft); draft = null; message = "无法读取草稿：" + error.Message; }
        }
        private void SaveClip()
        {
            if (!draft.Validate(out string error)) { message = error; return; }
            string path = EditorUtility.SaveFilePanelInProject("保存提示录像", recordedLevel != null ? recordedLevel.name + "_Hint" : "HintReplay", "asset", "选择录像保存位置");
            if (string.IsNullOrEmpty(path)) return;
            // SaveFilePanel may return an existing asset: preserve its GUID and all bindings.
            var existing = AssetDatabase.LoadAssetAtPath<HintReplayClip>(path);
            if (existing != null)
            {
                Undo.RecordObject(existing, "Replace hint recording");
                EditorUtility.CopySerialized(draft, existing);
                existing.name = Path.GetFileNameWithoutExtension(path);
                EditorUtility.SetDirty(existing);
                savedClip = existing;
            }
            else
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null) { message = "该路径已经存在其他类型的资源。"; return; }
                savedClip = Instantiate(draft); savedClip.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(savedClip, path);
            }
            AssetDatabase.SaveAssets();
            previewClip = savedClip;
            EditorGUIUtility.PingObject(savedClip);
            message = "录像已保存。正向绑定到目录；返程请把录像拖到对应 Trigger 的 Clip。";
        }
        private void BindClip()
        {
            Undo.RecordObject(catalog, "Bind hint replay");
            HintReplayCatalog.Entry entry = catalog.Entries.Find(item => item != null && item.Level == recordedLevel && string.IsNullOrEmpty(item.ModuleId));
            if (entry == null) { entry = new HintReplayCatalog.Entry { Level = recordedLevel }; catalog.Entries.Add(entry); }
            entry.Clip = savedClip;
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            message = "已更新关卡映射。请将此目录指定给场景中的 HintReplayDirector。";
        }
        private void StartPreview()
        {
            StopPreview(); EnsureDraft();
            PlayerController character = player != null ? player : HintReplayContext.Find<PlayerController>(SceneManager.GetActiveScene());
            if (character == null) { message = "未找到玩家。"; return; }
            var owner = new GameObject("Hint Replay Preview") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(owner, character.gameObject.scene);
            preview = owner.AddComponent<HintReplayPlayer>();
            Transform origin = referenceFrame;
            if (origin == null) origin = HintReplayContext.Region(character.gameObject.scene, character)?.transform;
            if (!preview.TryPlay(previewClip != null ? previewClip : draft, origin, character.Body.Visual, character.Head.Visual))
            { message = "预览失败，请检查录像和玩家显示层级。"; StopPreview(); }
        }
        private void StopPreview()
        {
            if (preview == null) return;
            preview.Stop(); DestroyImmediate(preview.gameObject); preview = null;
        }
    }
}
