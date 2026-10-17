using UnityEngine;
using UnityEngine.UI;

namespace TapTap
{
    // Mark Outline's duplicate vertices so their shader uses the sprite's alpha only.
    public sealed class PauseIconOutline : Outline
    {
        protected override void OnEnable()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null) canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            base.OnEnable();
        }

        public override void ModifyMesh(VertexHelper vertices)
        {
            if (!IsActive()) return;
            base.ModifyMesh(vertices);
            int outlineVertices = vertices.currentVertCount * 4 / 5;
            UIVertex vertex = default;
            for (int i = 0; i < vertices.currentVertCount; i++)
            {
                vertices.PopulateUIVertex(ref vertex, i);
                vertex.uv1 = new Vector4(i < outlineVertices ? 1f : 0f, 0f, 0f, 0f);
                vertices.SetUIVertex(vertex, i);
            }
        }
    }
}
