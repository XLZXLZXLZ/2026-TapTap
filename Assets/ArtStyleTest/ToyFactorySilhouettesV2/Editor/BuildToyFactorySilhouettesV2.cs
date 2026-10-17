using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildToyFactorySilhouettesV2
{
    static string FindRoot()
    {
        foreach (string guid in AssetDatabase.FindAssets("BuildToyFactorySilhouettesV2 t:MonoScript", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileName(path) != "BuildToyFactorySilhouettesV2.cs") continue;
            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
            while (!string.IsNullOrEmpty(folder) && folder != "Assets")
            {
                if (File.Exists(folder + "/Textures/01_WarningSymbol.png")) return folder;
                folder = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            }
        }
        return null;
    }

    [MenuItem("Tools/Toy Factory/Build Silhouettes V2")]
    public static void Build()
    {
        string root = FindRoot();
        if (root == null)
        {
            Debug.LogError("Wall decals not found. Keep Editor and Textures together inside the same folder under Assets.");
            return;
        }
        string generated = root + "/Prefabs";
        Directory.CreateDirectory(generated);
        AssetDatabase.Refresh();
        foreach (string file in Directory.GetFiles(root + "/Textures", "*.png"))
        {
            string texturePath = file.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(texturePath);
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null) { Debug.LogError("Texture missing: " + texturePath); continue; }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 512;
            importer.spritePivot = new Vector2(.5f, .5f);
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
            if (sprite == null) { Debug.LogError("Sprite import failed: " + texturePath); continue; }
            var go = new GameObject(name);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.sortingOrder = 0;
            PrefabUtility.SaveAsPrefabAsset(go, generated + "/" + name + ".prefab");
            Object.DestroyImmediate(go);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Wall decal prefabs saved to " + generated + ". Change SpriteRenderer Color to tint each instance.");
    }
}
