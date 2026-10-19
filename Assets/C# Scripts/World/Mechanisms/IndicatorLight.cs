using UnityEngine;

namespace TapTap
{
    [ExecuteAlways, DisallowMultipleComponent]
    [DefaultExecutionOrder(-120)]
    [AddComponentMenu("TapTap/World/Indicator Light")]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class IndicatorLight : MonoBehaviour
    {
        [Tooltip("场景首次开始时的状态。第三个触发点进入返程后统一淡入，不再由钥匙控制。")]
        public bool InitiallyOn;
        [Min(0f)] public float StageFadeDuration = 0.65f;
        [Tooltip("梯形短边宽度（格），只能是 2 或 3。短边中心为旋转轴。")]
        [Range(2, 3)] public int ShortWidth = 2;
        [Min(3f)] public float LongWidth = 6f;
        [Min(0.1f)] public float Length = 8f;
        public Color Tint = new Color(1f, 0.88f, 0.58f, 0.3f);
        [Min(0.1f)] public float GradientPower = 1.5f;
        [Tooltip("每格的本地尺寸，可配合 Transform 缩放调整场景中的指示灯。")]
        [Min(0.01f)] public float GridUnit = 1f;
        private Mesh mesh;
        private MeshRenderer visual;
        private bool visualDirty;
        private float opacity, targetOpacity;
        private Color[] baseColors, fadedColors;
        public bool IsOn { get; private set; }

        private void OnEnable()
        {
            RebuildVisual();
            ApplyStage(true);
            if (Application.isPlaying)
            {
                EventManager.Subscribe<LightsStateRequested>(ReceiveState);
                EventManager.Subscribe<GameStageChanged>(ReceiveStage);
            }
        }

        private void OnDisable()
        {
            EventManager.Unsubscribe<LightsStateRequested>(ReceiveState);
            EventManager.Unsubscribe<GameStageChanged>(ReceiveStage);
        }

        public void ApplyStage(bool immediate) => SetOn(InitiallyOn
            || Application.isPlaying && GameStageProgression.HasProgressedIn(gameObject.scene), immediate);

        private void ReceiveStage(GameStageChanged message)
        {
            if (message.Scene == gameObject.scene && message.Previous == GameStage.Outbound) SetOn(true, false);
        }

        private void ReceiveState(LightsStateRequested message)
        {
            if (message.Scene == gameObject.scene) SetOn(message.IsOn);
        }

        public void SetOn(bool value) => SetOn(value, true);

        public void SetOn(bool value, bool immediate)
        {
            IsOn = value;
            targetOpacity = value ? 1f : 0f;
            if (immediate || !Application.isPlaying || StageFadeDuration <= 0f) opacity = targetOpacity;
            if (visual == null) visual = GetComponent<MeshRenderer>();
            // Authoring preview stays visible even when the game starts with this light off.
            visual.enabled = !Application.isPlaying || value || opacity > 0f;
            ApplyOpacity();
        }

        private void ApplyOpacity()
        {
            if (mesh == null || baseColors == null) return;
            float alpha = Application.isPlaying ? opacity : 1f;
            for (int i = 0; i < baseColors.Length; i++)
            {
                fadedColors[i] = baseColors[i];
                fadedColors[i].a *= alpha;
            }
            mesh.colors = fadedColors;
        }

        public void RebuildVisual()
        {
            ShortWidth = Mathf.Clamp(ShortWidth, 2, 3);
            LongWidth = Mathf.Max(ShortWidth, LongWidth);
            Length = Mathf.Max(0.1f, Length);
            GradientPower = Mathf.Max(0.1f, GradientPower);
            GridUnit = Mathf.Max(0.01f, GridUnit);
            if (mesh == null) mesh = new Mesh { name = "Indicator light trapezoid", hideFlags = HideFlags.HideAndDontSave };
            const int rows = 24, columns = 12;
            var vertices = new Vector3[(rows + 1) * (columns + 1)];
            var colors = new Color[vertices.Length];
            var triangles = new int[rows * columns * 6];
            for (int y = 0; y <= rows; y++)
            {
                float t = (float)y / rows;
                float width = Mathf.Lerp(ShortWidth, LongWidth, t) * GridUnit;
                for (int x = 0; x <= columns; x++)
                {
                    float u = (float)x / columns;
                    int index = y * (columns + 1) + x;
                    vertices[index] = new Vector3((u - 0.5f) * width, -t * Length * GridUnit);
                    Color color = Tint;
                    float edge = Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) * 8f);
                    color.a *= Mathf.Pow(1f - t, GradientPower) * edge;
                    colors[index] = color;
                    if (y == rows || x == columns) continue;
                    int tri = (y * columns + x) * 6;
                    triangles[tri] = index; triangles[tri + 1] = index + columns + 1; triangles[tri + 2] = index + 1;
                    triangles[tri + 3] = index + 1; triangles[tri + 4] = index + columns + 1;
                    triangles[tri + 5] = index + columns + 2;
                }
            }
            mesh.Clear(); mesh.vertices = vertices; mesh.colors = colors; mesh.triangles = triangles;
            baseColors = colors;
            fadedColors = new Color[colors.Length];
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            visual = GetComponent<MeshRenderer>();
            if (visual.sharedMaterial == null) visual.sharedMaterial = Resources.Load<Material>("Materials/IndicatorVisual");
            // Draw above terrain (0/1) and mechanisms (2), below the player (10/12).
            visual.sortingOrder = 5;
            ApplyOpacity();
        }

        private void OnValidate() => visualDirty = true;

        private void Update()
        {
            if (Application.isPlaying && Time.timeScale > 0f && opacity != targetOpacity)
            {
                opacity = Mathf.MoveTowards(opacity, targetOpacity, StageFadeDuration > 0f ? Time.unscaledDeltaTime / StageFadeDuration : 1f);
                ApplyOpacity();
                visual.enabled = IsOn || opacity > 0f;
            }
            if (!visualDirty) return;
            visualDirty = false;
            RebuildVisual();
            if (!Application.isPlaying) SetOn(InitiallyOn);
        }

        private void OnDestroy()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        }
    }
}
