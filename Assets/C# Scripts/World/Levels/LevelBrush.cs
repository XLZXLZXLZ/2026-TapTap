using UnityEngine;

namespace TapTap
{
    [CreateAssetMenu(menuName = "TapTap/Levels/Brush")]
    public sealed class LevelBrush : ScriptableObject
    {
        public string DisplayName;
        public string HandlerId = "default";
        // Retain old serialized brushes so legacy layouts load, but never edit or generate them.
        public bool IsEditorIgnored => HandlerId == "annotation" || HandlerId == "indicator-light";
        public string LayerId = "geometry";
        public GameObject Prefab;
        public Texture2D Thumbnail;
        public Vector2 Anchor = new Vector2(0.5f, 0.5f);
        public Rect PreviewRect = new Rect(0f, 0f, 1f, 1f);
        public Color Color = UnityEngine.Color.white;
    }
}
