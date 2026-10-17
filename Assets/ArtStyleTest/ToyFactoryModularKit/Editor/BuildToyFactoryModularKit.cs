using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

public static class BuildToyFactoryModularKit
{
    [Serializable] class Indices { public int[] indices; }
    [Serializable] class Model { public string name; public float[] vertices; public Indices[] submeshes; }
    static string FindRoot()
    {
        foreach (string guid in AssetDatabase.FindAssets("BuildToyFactoryModularKit t:MonoScript", new[] { "Assets" }))
        {
            string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileName(scriptPath) != "BuildToyFactoryModularKit.cs") continue;
            string folder = Path.GetDirectoryName(scriptPath).Replace('\\', '/');
            while (!string.IsNullOrEmpty(folder) && folder != "Assets")
            {
                if (File.Exists(folder + "/Models/01_EmptyRack_2m.json")) return folder;
                folder = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            }
        }
        return null;
    }
    [MenuItem("Tools/Toy Factory/Build Modular Kit")]
    public static void Build()
    {
        string Root = FindRoot();
        if (Root == null)
        {
            Debug.LogError("Toy Factory: Models folder not found. Keep Editor/BuildToyFactoryModularKit.cs and Models together under the same asset folder, for example Assets/ArtStyleTest.");
            return;
        }
        Directory.CreateDirectory(Root + "/Generated");
        AssetDatabase.Refresh();
        string[] names = { "steel", "edge", "dark", "shadow", "yellow", "red", "teal", "glass", "black" };
        string[] colors = { "526976", "6d828b", "334955", "263b46", "a99a58", "80575b", "578b8c", "8eb0ac", "29343b" };
        var pipeline = GraphicsSettings.currentRenderPipeline;
        Shader shader = pipeline == null ? Shader.Find("Standard") :
            Shader.Find(pipeline.GetType().Name.Contains("HDRenderPipeline") ? "HDRP/Lit" : "Universal Render Pipeline/Lit");
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
        BuildRack(Root, materials, 2);
        BuildRack(Root, materials, 1);
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Debug.Log("Toy Factory: 20 modular prefabs created in " + Root + "/Generated");
    }

    static void Part(GameObject parent, string root, Material[] materials, string meshName, Vector3 position, Vector3 scale)
    {
        var child = new GameObject(meshName);
        child.transform.SetParent(parent.transform, false);
        child.transform.localPosition = position;
        child.transform.localScale = scale;
        child.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(root + "/Generated/" + meshName + ".asset");
        var renderer = child.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }
    static void BuildRack(string root, Material[] materials, int width)
    {
        string name = width == 2 ? "01_EmptyRack_2m" : "02_EmptyRack_1m";
        var rack = new GameObject(name);
        foreach (float x in new[] { -width / 2f, width / 2f })
            foreach (float z in new[] { -.4f, .4f })
                Part(rack, root, materials, "03_Upright_240", new Vector3(x, 0, z), Vector3.one);
        foreach (float y in new[] { .2f, 1.2f, 2.2f })
            Part(rack, root, materials, width == 2 ? "04_Shelf_2m" : "05_Shelf_1m", new Vector3(0, y, 0), Vector3.one);
        foreach (float y in new[] { .7f, 1.7f })
            Part(rack, root, materials, "08_CrossBrace_2x1", new Vector3(0, y, -.43f), new Vector3(width / 2f, 1, 1));
        PrefabUtility.SaveAsPrefabAsset(rack, root + "/Generated/" + name + ".prefab");
        UnityEngine.Object.DestroyImmediate(rack);
    }
}
