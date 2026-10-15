using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    // A disabled temporary camera provides a real render without changing scene cameras.
    public sealed class LevelCameraPreviewWindow : EditorWindow
    {
        [SerializeField] private LevelDefinition level;
        [SerializeField] private LevelRegion region;
        [SerializeField] private int aspectIndex;
        private Camera preview;
        private RenderTexture texture;
        private static readonly string[] AspectNames = { "16:10", "16:9", "4:3" };
        private static readonly float[] Aspects = { 1.6f, 16f / 9f, 4f / 3f };

        public static void Open(LevelDefinition source, LevelRegion context)
        {
            var window = GetWindow<LevelCameraPreviewWindow>("关卡镜头预览");
            window.level = source;
            window.region = context;
            window.minSize = new Vector2(420, 300);
            window.Show();
            window.Repaint();
        }

        private void OnInspectorUpdate() { if (!EditorApplication.isPlaying) Repaint(); }

        private void OnGUI()
        {
            if (level == null || region == null || region.Source != level)
            {
                EditorGUILayout.HelpBox("请从关卡镜头面板选择场景参照后打开预览。", MessageType.Info);
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("编辑器镜头预览仅用于非 Play 状态。", MessageType.Info);
                return;
            }
            EditorGUILayout.LabelField(level.name + " · 固定透视镜头", EditorStyles.boldLabel);
            aspectIndex = EditorGUILayout.Popup("预览画幅", aspectIndex, AspectNames);
            float aspect = Aspects[Mathf.Clamp(aspectIndex, 0, Aspects.Length - 1)];
            if (Event.current.type == EventType.Repaint)
            {
                EnsureCamera(aspect);
                var shot = LevelCameraShot.Evaluate(region, level.CameraSettings, aspect);
                preview.transform.SetPositionAndRotation(shot.Position, shot.Rotation);
                shot.ApplyLens(preview);
                preview.Render();
            }
            if (texture == null) return;
            Rect area = new Rect(8, 58, Mathf.Max(1, position.width - 16), Mathf.Max(1, position.height - 88));
            GUI.DrawTexture(area, texture, ScaleMode.ScaleToFit, false);
            GUI.Label(new Rect(8, position.height - 24, position.width - 16, 20),
                "仅预览，不移动或保存场景主相机；配置完成后在镜头面板保存。", EditorStyles.miniLabel);
        }

        private void EnsureCamera(float aspect)
        {
            if (preview == null)
            {
                var owner = new GameObject("Level Camera Preview") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(owner, region.gameObject.scene);
                preview = owner.AddComponent<Camera>();
                Camera main = Camera.main;
                if (main != null) preview.CopyFrom(main);
                preview.enabled = false;
                preview.clearFlags = CameraClearFlags.SolidColor;
                preview.backgroundColor = new Color(0.045f, 0.075f, 0.12f);
            }
            int height = Mathf.RoundToInt(1024 / aspect);
            if (texture == null || texture.height != height)
            {
                ReleaseTexture();
                texture = new RenderTexture(1024, height, 24) { hideFlags = HideFlags.HideAndDontSave };
                texture.Create();
            }
            preview.targetTexture = texture;
            preview.aspect = aspect;
        }

        private void ReleaseTexture()
        {
            if (preview != null) preview.targetTexture = null;
            if (texture == null) return;
            texture.Release();
            DestroyImmediate(texture);
            texture = null;
        }

        private void OnDisable()
        {
            ReleaseTexture();
            if (preview != null) DestroyImmediate(preview.gameObject);
        }
    }
}
