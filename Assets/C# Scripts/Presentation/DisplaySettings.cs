using UnityEngine;

namespace TapTap
{
    public static class DisplaySettings
    {
        public const string WindowedPreference = "TapTap.Display.Windowed";
        public static bool IsWindowed => Screen.fullScreenMode == FullScreenMode.Windowed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Restore()
        {
            // Always override Unity's remembered resolution, including on a fresh install.
            Apply(PlayerPrefs.GetInt(WindowedPreference, 0) != 0);
        }

        public static void SetWindowed(bool windowed)
        {
            Apply(windowed);
            PlayerPrefs.SetInt(WindowedPreference, windowed ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static void Apply(bool windowed)
        {
            int width = 1920;
            int height = 1080;
            if (windowed)
            {
                Resolution desktop = Screen.currentResolution;
                // Fit inside 80% of the desktop, using whole 16:9 units to preserve the aspect exactly.
                int units = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(desktop.width * 0.8f / 16f,
                    desktop.height * 0.8f / 9f)), 1, 120);
                width = units * 16;
                height = units * 9;
            }
            Screen.SetResolution(width, height,
                windowed ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow);
        }
    }
}
