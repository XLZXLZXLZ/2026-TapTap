using UnityEngine;

namespace TapTap
{
    public enum PlatformPart { Single, Left, Middle, Right }

    [DisallowMultipleComponent]
    public sealed class PlatformTileVisual : MonoBehaviour
    {
        public string ConnectionGroup = "platform";
        public GameObject SingleModel;
        public GameObject LeftModel;
        public GameObject MiddleModel;
        public GameObject RightModel;
        public GameObject[] LegacyVisuals;
        [SerializeField] private PlatformPart part;
        [SerializeField] private Transform visual;
        public PlatformPart Part => part;

        public void ApplyPart(PlatformPart value)
        {
            part = value;
            GameObject model = value == PlatformPart.Single ? SingleModel : value == PlatformPart.Left ? LeftModel
                : value == PlatformPart.Right ? RightModel : MiddleModel;
            model = LevelStyle.ModelFor(transform, model);
            if (model == null) return;
            if (visual != null)
                TileModelUtility.Release(visual);
            if (LegacyVisuals != null)
                foreach (GameObject legacy in LegacyVisuals) if (legacy != null) legacy.SetActive(false);
            visual = Instantiate(model, transform, false).transform;
            visual.name = "Platform Visual (" + value + ")";
        }
    }

    internal static class TileModelUtility
    {
        public static void Release(Transform previous)
        {
            previous.gameObject.SetActive(false);
            if (Application.isPlaying) Object.Destroy(previous.gameObject);
            else
            {
#if UNITY_EDITOR
                // Inherited prefab children cannot be destroyed; retain an inactive template override.
                if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(previous.gameObject) &&
                    !UnityEditor.PrefabUtility.IsAddedGameObjectOverride(previous.gameObject))
                { previous.name += " (Template)"; return; }
#endif
                Object.DestroyImmediate(previous.gameObject);
            }
        }
    }
}
