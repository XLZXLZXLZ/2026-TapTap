using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

public static class BuildBackgroundKit
{
    [Serializable] class Indices { public int[] indices; }
    [Serializable] class Model { public string name; public float[] vertices; public Indices[] submeshes; }
    static string FindRoot()
    {
        foreach (string guid in AssetDatabase.FindAssets("BuildBackgroundKit t:MonoScript", new[] { "Assets" }))
        {
            string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileName(scriptPath) != "BuildBackgroundKit.cs") continue;
            string folder = Path.GetDirectoryName(scriptPath).Replace('\\', '/');
            while (!string.IsNullOrEmpty(folder) && folder != "Assets")
            {
                if (File.Exists(folder + "/Models/01_Scaffold.json")) return folder;
                folder = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            }
        }
        return null;
    }
    [MenuItem("Tools/Toy Factory/Build Background Prefabs")]
    public static void Build()
    {
        string Root = FindRoot();
        if (Root == null)
        {
            Debug.LogError("Toy Factory: Models folder not found. Keep Editor/BuildBackgroundKit.cs and Models together under the same asset folder, for example Assets/ArtStyleTest.");
            return;
        }
        Directory.CreateDirectory(Root + "/Generated");
        AssetDatabase.Refresh();
        string[] names = { "steel", "edge", "dark", "shadow", "yellow", "red", "teal", "glass", "black" };
        string[] colors = { "526976", "6d828b", "334955", "263b46", "a99a58", "80575b", "578b8c", "8eb0ac", "29343b" };
        Shader shader = GraphicsSettings.currentRenderPipeline == null ? Shader.Find("Standard") : Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("HDRP/Lit");
        if (shader == null) { Debug.LogError("No supported Lit shader found."); return; }
        var materials = new Material[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            string path = Root + "/Generated/" + names[i] + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = material == null;
            if (create) material = new Material(shader);
            material.shader = shader;
            ColorUtility.TryParseHtmlString("#" + colors[i], out Color color);
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .13f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .13f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (create) AssetDatabase.CreateAsset(material, path); else EditorUtility.SetDirty(material);
            materials[i] = material;
        }
        foreach (string file in Directory.GetFiles(Root + "/Models", "*.json"))
        {
            Model data = JsonUtility.FromJson<Model>(File.ReadAllText(file));
            var vertices = new Vector3[data.vertices.Length / 3];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = new Vector3(data.vertices[i*3], data.vertices[i*3+1], data.vertices[i*3+2]);
            string path = Root + "/Generated/" + data.name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh(); else mesh.Clear();
            mesh.name = data.name;
            mesh.vertices = vertices;
            mesh.subMeshCount = data.submeshes.Length;
            for (int i = 0; i < data.submeshes.Length; i++) mesh.SetTriangles(data.submeshes[i].indices, i);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (create) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            var go = new GameObject(data.name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            PrefabUtility.SaveAsPrefabAsset(go, Root + "/Generated/" + data.name + ".prefab");
            UnityEngine.Object.DestroyImmediate(go);
        }
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Debug.Log("Toy Factory: 5 background prefabs created in " + Root + "/Generated");
    }
}
