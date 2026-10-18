using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public static class LevelOutlineBuilder
    {
        public static LevelOutlineData Save(LevelDefinition level)
        {
            LevelOutlineData outline = level.Outline;
            if (outline == null)
            {
                LevelPrefabBuilder.EnsureFolder("Assets/Runtime/LevelOutlines");
                outline = ScriptableObject.CreateInstance<LevelOutlineData>();
                string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Runtime/LevelOutlines/"
                    + LevelPrefabBuilder.SafeName(level.name) + "_Outline.asset");
                AssetDatabase.CreateAsset(outline, path);
                level.Outline = outline;
            }
            outline.Build(level.Footprint);
            EditorUtility.SetDirty(outline);
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssetIfDirty(outline);
            return outline;
        }
    }
}
