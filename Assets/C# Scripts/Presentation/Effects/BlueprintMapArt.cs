using System;
using UnityEngine;

namespace TapTap
{
    // Shared artwork only. Each scene owns its own screen, layout and animation settings.
    [CreateAssetMenu(menuName = "TapTap/Blueprint Map Art")]
    public sealed class BlueprintMapArt : ScriptableObject
    {
        public WorldMapDefinition Source;
        public Texture2D Background, Shade, Title;
        public Vector2 CanvasSize = new Vector2(1920, 2160);
        public Rect ContentBounds;
        public Piece[] Pieces;

        [Serializable]
        public sealed class Piece
        {
            public int Sequence;
            public string ModuleId;
            public Texture2D Normal, Glow;
            public Rect NormalBounds, GlowBounds;
            public Vector2 TravelPoint;
            public int MaskWidth, MaskHeight;
            public byte[] HitMask;
            public Vector2 Center => NormalBounds.center;

            // Baked from the normal layer's outer silhouette, with internal cutouts filled.
            // Textures do not need Read/Write enabled at runtime.
            public bool Contains(Vector2 canvasPoint)
            {
                if (!NormalBounds.Contains(canvasPoint) || HitMask == null || MaskWidth <= 0 || MaskHeight <= 0) return false;
                int x = Mathf.Clamp((int)((canvasPoint.x - NormalBounds.x) / NormalBounds.width * MaskWidth), 0, MaskWidth - 1);
                int y = Mathf.Clamp((int)((canvasPoint.y - NormalBounds.y) / NormalBounds.height * MaskHeight), 0, MaskHeight - 1);
                int bit = y * MaskWidth + x;
                return bit / 8 < HitMask.Length && (HitMask[bit / 8] & (1 << (bit % 8))) != 0;
            }
        }
    }
}
