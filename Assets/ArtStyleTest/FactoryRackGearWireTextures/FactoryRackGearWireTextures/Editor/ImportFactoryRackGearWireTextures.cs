using System.IO;
using UnityEngine;
using UnityEditor;

public static class ImportFactoryRackGearWireTextures
{
    [MenuItem("Tools/Toy Factory/Import Rack Gear Wire Textures")]
    public static void Import()
    {
        string root = null;
        foreach (string guid in AssetDatabase.FindAssets("ImportFactoryRackGearWireTextures t:MonoScript", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileName(path) != "ImportFactoryRackGearWireTextures.cs") continue;
            string parent = Path.GetDirectoryName(Path.GetDirectoryName(path)).Replace('\\', '/');
            if (Directory.Exists(parent + "/Textures")) { root = parent; break; }
        }
        if (root == null) { Debug.LogError("Keep Editor and Textures in the same asset folder."); return; }
        foreach (string file in Directory.GetFiles(root + "/Textures", "*.png"))
        {
            string path = file.Replace('\\', '/');
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 512;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 2048;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.SaveAndReimport();
        }
        Debug.Log("8 sprites imported. Drag PNGs into the scene and change SpriteRenderer Color.");
    }
}
