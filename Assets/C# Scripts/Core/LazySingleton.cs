using UnityEngine;

namespace TapTap
{
    public abstract class LazySingleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T instance;
        public static T Existing => instance;
        public static T Instance
        {
            get
            {
                if (instance != null) return instance;
                instance = FindObjectOfType<T>();
                if (instance == null) instance = new GameObject(typeof(T).Name).AddComponent<T>();
                return instance;
            }
        }

        protected virtual void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
