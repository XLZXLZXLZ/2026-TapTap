using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public static class EndpointEntryAssets
    {
        [MenuItem("TapTap/维护/素材装配/更新终点入门锚点")]
        public static void EnsureAnchors()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(GameplayVisualAssets.EndpointPrefabPath);
            try
            {
                LevelEndpoint endpoint = root.GetComponent<LevelEndpoint>();
                Transform front = Anchor(root.transform, "Door Front", Vector3.zero);
                Transform inside = Anchor(root.transform, "Door Inside", Vector3.forward);
                endpoint.ConfigureEntryAnchors(front, inside);
                PrefabUtility.SaveAsPrefabAsset(root, GameplayVisualAssets.EndpointPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Transform Anchor(Transform parent, string name, Vector3 position)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing;
            var anchor = new GameObject(name).transform;
            anchor.SetParent(parent, false);
            anchor.localPosition = position;
            return anchor;
        }
    }
}
