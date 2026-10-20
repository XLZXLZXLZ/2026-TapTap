using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Hints
{
    [DisallowMultipleComponent, DefaultExecutionOrder(11000)]
    public sealed class HintReplayPlayer : MonoBehaviour
    {
        public Color Tint = new Color(0.25f, 0.9f, 1f, 0.45f);
        [Tooltip("可选的透明材质；留空使用模块自带的 URP 虚影 Shader。播放时创建材质副本。")]
        public Material GhostMaterial;
        [Min(0f)] public float ConnectionWidth = 0.025f;
        [Range(0.1f, 3f)] public float PlaybackSpeed = 1f;
        public bool IsPlaying => clip != null && ghost != null;
        public float Playhead { get; private set; }
        public GameObject GhostRoot => ghost;
        public HintReplayClip PlayingClip => clip;
        public event Action Finished;

        private HintReplayClip clip;
        private Transform origin;
        private bool hadOrigin;
        private GameObject ghost;
        private Transform[] nodes;
        private readonly List<Renderer> renderers = new List<Renderer>();
        private Material material;
        private LineRenderer connection;

        public bool TryPlay(HintReplayClip recording, Transform referenceFrame, Transform bodyVisual, Transform headVisual)
        {
            Stop();
            if (!isActiveAndEnabled || recording == null || bodyVisual == null || headVisual == null) return false;
            if (recording.UsesWorldSpace) referenceFrame = null;
            if (bodyVisual.gameObject.scene != gameObject.scene || headVisual.gameObject.scene != gameObject.scene
                || referenceFrame != null && referenceFrame.gameObject.scene != gameObject.scene) return false;
            if (!recording.Validate(out string error)) { Debug.LogWarning(error, recording); return false; }
            var source = new HintReplayVisuals(bodyVisual, headVisual);
            if (!source.Matches(recording.Nodes))
            { Debug.LogWarning("提示录像与当前角色显示层级不匹配，请使用当前角色重新录制。", recording); return false; }
            Shader shader = GhostMaterial != null ? GhostMaterial.shader : Resources.Load<Shader>("HintReplayGhost");
            if (shader == null) { Debug.LogWarning("未找到虚影 Shader，请配置 Ghost Material。", this); return false; }
            try
            {
                material = GhostMaterial != null ? new Material(GhostMaterial) : new Material(shader);
                material.name = "Hint Replay (Instance)";
                if (material.HasProperty("_Color")) material.SetColor("_Color", Tint);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Tint);
                ghost = new GameObject("Hint Replay Ghost");
                ghost.SetActive(false);
                SceneManager.MoveGameObjectToScene(ghost, gameObject.scene);
                ghost.transform.SetParent(referenceFrame, false);
                origin = referenceFrame; hadOrigin = referenceFrame != null;
                nodes = new Transform[recording.Nodes.Length];
                for (int i = 0; i < nodes.Length; i++)
                {
                    nodes[i] = new GameObject(recording.Nodes[i].Name).transform;
                    int parent = recording.Nodes[i].Parent;
                    nodes[i].SetParent(parent < 0 ? ghost.transform : nodes[parent], false);
                    nodes[i].gameObject.layer = source.Transforms[i].gameObject.layer;
                }
                source.CopyRenderers(nodes, material, renderers);
                connection = new GameObject("Ghost Connection").AddComponent<LineRenderer>();
                connection.transform.SetParent(ghost.transform, false);
                connection.useWorldSpace = false;
                connection.sharedMaterial = material;
                connection.positionCount = 2;
                connection.widthMultiplier = ConnectionWidth;
                connection.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                connection.receiveShadows = false;
                clip = recording; Playhead = 0f;
                Render(0f);
                ghost.SetActive(true);
                return true;
            }
            catch (Exception exception)
            {
                Stop();
                Debug.LogWarning("无法播放提示录像：" + exception.Message, this);
                return false;
            }
        }

        public void Seek(float time)
        {
            if (!IsPlaying) return;
            Playhead = Mathf.Clamp(time, 0f, clip.Duration);
            Render(Playhead);
        }

        private void LateUpdate()
        {
            if (!IsPlaying) return;
            if (hadOrigin && origin == null || RewindManager.Rewinding || HintReplayContext.Transitioning(gameObject.scene))
            { Stop(); return; }
            if (HintReplayContext.Paused) return;
            if (Playhead >= clip.Duration) { Stop(); Finished?.Invoke(); return; }
            Playhead = Mathf.Min(clip.Duration, Playhead + Time.deltaTime * PlaybackSpeed);
            Render(Playhead);
        }

        private void Render(float time)
        {
            clip.Sample(time, out HintReplayFrame from, out HintReplayFrame to, out float blend);
            HintReplayFrame discrete = blend >= 1f ? to : from;
            for (int i = 0; i < nodes.Length; i++)
            {
                HintReplayPose a = from.Poses[i], b = to.Poses[i];
                nodes[i].localPosition = Vector3.LerpUnclamped(a.Position, b.Position, blend);
                nodes[i].localRotation = Quaternion.SlerpUnclamped(a.Rotation, b.Rotation, blend);
                nodes[i].localScale = Vector3.LerpUnclamped(a.Scale, b.Scale, blend);
                nodes[i].gameObject.SetActive(discrete.Poses[i].Active);
            }
            for (int i = 0; i < renderers.Count; i++) renderers[i].enabled = discrete.Visible[i];
            connection.enabled = discrete.Connected;
            connection.SetPosition(0, Vector3.Lerp(from.ConnectionFrom, to.ConnectionFrom, blend));
            connection.SetPosition(1, Vector3.Lerp(from.ConnectionTo, to.ConnectionTo, blend));
        }

        public void Stop()
        {
            clip = null; nodes = null; origin = null; Playhead = 0f;
            renderers.Clear();
            if (ghost != null) { ghost.SetActive(false); Release(ghost); }
            if (material != null) Release(material);
            ghost = null; material = null; connection = null;
        }

        private static void Release(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
        private void OnDisable() => Stop();
    }
}
