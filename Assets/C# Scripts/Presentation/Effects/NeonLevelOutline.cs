using UnityEngine;
using UnityEngine.UI;

namespace TapTap
{
    // The entire stroke lies inside its own polygon. Shared borders cannot
    // overwrite their neighbour, irrespective of the prefab sibling order.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class NeonLevelOutline : MaskableGraphic
    {
        public Vector2[] Polygon;
        [Min(0.01f)] public float GlowWidth = 0.62f;
        [Range(0f,1f)] public float Reveal = 1f;
        [Range(0f,1f)] public float Emphasis;

        public void SetEmphasis(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(Emphasis,value)) return;
            Emphasis=value; SetVerticesDirty();
        }

        public void SetReveal(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(Reveal,value)) return;
            Reveal=value; SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (Polygon == null || Polygon.Length < 3) return;
            float area = 0f;
            for (int i = 0; i < Polygon.Length; i++)
            {
                Vector2 a = Polygon[i], b = Polygon[(i + 1) % Polygon.Length];
                area += a.x * b.y - b.x * a.y;
            }
            float sign = area >= 0f ? 1f : -1f;
            var insetPoints = new Vector2[Polygon.Length];
            float perimeter = 0f;
            for (int i = 0; i < Polygon.Length; i++)
            {
                Vector2 before = (Polygon[i] - Polygon[(i + Polygon.Length - 1) % Polygon.Length]).normalized;
                Vector2 after = (Polygon[(i + 1) % Polygon.Length] - Polygon[i]).normalized;
                Vector2 inward = (new Vector2(-before.y, before.x) + new Vector2(-after.y, after.x)) * sign;
                Vector2 inset = inward * (GlowWidth / Mathf.Max(0.08f, 1f + Vector2.Dot(before, after)));
                insetPoints[i] = Polygon[i]+inset;
                perimeter += Vector2.Distance(Polygon[i],Polygon[(i+1)%Polygon.Length]);
            }
            float distance=0f;
            for (int i = 0; i < Polygon.Length; i++)
            {
                int next=(i+1)%Polygon.Length;
                float end=distance+Vector2.Distance(Polygon[i],Polygon[next]);
                int a=mesh.currentVertCount;
                AddVertex(mesh,Polygon[i],0f,distance/perimeter);
                AddVertex(mesh,insetPoints[i],1f,distance/perimeter);
                AddVertex(mesh,Polygon[next],0f,end/perimeter);
                AddVertex(mesh,insetPoints[next],1f,end/perimeter);
                mesh.AddTriangle(a,a+2,a+1); mesh.AddTriangle(a+2,a+3,a+1);
                distance=end;
            }
        }

        private void AddVertex(VertexHelper mesh, Vector2 point,float inset,float distance)
        {
            UIVertex vertex=UIVertex.simpleVert;
            vertex.position=point; vertex.color=color;
            vertex.uv0=new Vector2(inset,distance); vertex.uv1=new Vector2(Reveal,Emphasis);
            mesh.AddVert(vertex);
        }

        public bool ContainsLocalPoint(Vector2 point)
        {
            bool inside = false;
            if (Polygon == null) return false;
            for (int i = 0, j = Polygon.Length - 1; i < Polygon.Length; j = i++)
            {
                Vector2 a = Polygon[i], b = Polygon[j];
                if ((a.y > point.y) != (b.y > point.y)
                    && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
