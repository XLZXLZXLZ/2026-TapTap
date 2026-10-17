using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class FourCornerPlane : MonoBehaviour
{
    // 从正面看，按顺时针排列
    public Vector3 topLeft     = new Vector3(-1,  1, 0);
    public Vector3 topRight    = new Vector3( 1,  1, 0);
    public Vector3 bottomRight = new Vector3( 1, -1, 0);
    public Vector3 bottomLeft  = new Vector3(-1, -1, 0);

    private Mesh mesh;

    private void OnEnable()
    {
        Rebuild();
    }

    private void OnValidate()
    {
        Rebuild();
    }

    public void Rebuild()
    {
        if (mesh == null)
        {
            mesh = new Mesh
            {
                name = "Four Corner Plane",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        mesh.Clear();

        mesh.vertices = new[]
        {
            topLeft, topRight, bottomRight, bottomLeft
        };

        mesh.uv = new[]
        {
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(1, 0),
            new Vector2(0, 0)
        };

        // 默认正面朝向本地 -Z
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    private void OnDestroy()
    {
        if (mesh == null) return;

        if (Application.isPlaying)
            Destroy(mesh);
        else
            DestroyImmediate(mesh);
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(FourCornerPlane))]
public class FourCornerPlaneEditor : Editor
{
    private void OnSceneGUI()
    {
        var plane = (FourCornerPlane)target;

        DrawCorner(plane, ref plane.topLeft, "左上");
        DrawCorner(plane, ref plane.topRight, "右上");
        DrawCorner(plane, ref plane.bottomRight, "右下");
        DrawCorner(plane, ref plane.bottomLeft, "左下");
    }

    private void DrawCorner(
        FourCornerPlane plane,
        ref Vector3 corner,
        string label)
    {
        Transform t = plane.transform;
        Vector3 worldPosition = t.TransformPoint(corner);

        Handles.Label(worldPosition, label);

        EditorGUI.BeginChangeCheck();

        Vector3 newPosition = Handles.PositionHandle(
            worldPosition,
            Quaternion.identity
        );

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(plane, "移动面片顶点");

            corner = t.InverseTransformPoint(newPosition);
            plane.Rebuild();

            PrefabUtility.RecordPrefabInstancePropertyModifications(plane);
            EditorUtility.SetDirty(plane);
        }
    }
}
#endif