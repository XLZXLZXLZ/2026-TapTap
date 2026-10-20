using System;
using UnityEngine;

namespace TapTap
{
    /// <summary>Lifetime completion record, independent of a world's level progress.</summary>
    public static class GameCompletionStore
    {
        public const string PreferenceKey = "TapTap.GameCompletion.HasCompletedGame";
        public static bool HasCompletedGame => PlayerPrefs.GetInt(PreferenceKey, 0) == 1;
        public static event Action<bool> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetListeners() => Changed = null;

        public static void MarkCompleted()
        {
            if (HasCompletedGame) return;
            PlayerPrefs.SetInt(PreferenceKey, 1);
            // Flush before invoking listeners or starting the ending's scene transition.
            PlayerPrefs.Save();
            Changed?.Invoke(true);
        }

        /// <summary>Explicit reset only. Starting a new run and clearing level progress never call this.</summary>
        public static void Clear()
        {
            if (!PlayerPrefs.HasKey(PreferenceKey)) return;
            PlayerPrefs.DeleteKey(PreferenceKey);
            PlayerPrefs.Save();
            Changed?.Invoke(false);
        }
    }
}
