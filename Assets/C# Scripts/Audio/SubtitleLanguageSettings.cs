using System;
using UnityEngine;

namespace TapTap
{
    public enum SubtitleLanguage { Chinese, English }

    public static class SubtitleLanguageSettings
    {
        public static SubtitleLanguage Language { get; private set; } = SubtitleLanguage.Chinese;
        public static event Action<SubtitleLanguage> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Language = SubtitleLanguage.Chinese;
            Changed = null;
        }

        // No menu or saved preference yet. A future settings screen can call this directly.
        public static void SetLanguage(SubtitleLanguage language)
        {
            if (language != SubtitleLanguage.Chinese && language != SubtitleLanguage.English)
                throw new ArgumentOutOfRangeException(nameof(language));
            if (Language == language) return;
            Language = language;
            Changed?.Invoke(language);
        }
    }
}
