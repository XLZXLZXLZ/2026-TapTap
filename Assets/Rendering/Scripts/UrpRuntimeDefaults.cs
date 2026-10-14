using UnityEngine;
using UnityEngine.Rendering;

public static class UrpRuntimeDefaults
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Keep the existing gameplay input map. URP's optional runtime debug menu otherwise
        // polls legacy debug buttons that this Built-in project has never defined.
        DebugManager.instance.enableRuntimeUI = false;
#endif
    }
}
