using System;

namespace TapTap.Editor
{
    public static class LevelLayoutChanges
    {
        public static event Action<LevelDefinition> Changed;
        public static void Notify(LevelDefinition level)
        {
            if (level == null) return;
            level.InvalidateFootprint();
            Changed?.Invoke(level);
        }
    }
}
