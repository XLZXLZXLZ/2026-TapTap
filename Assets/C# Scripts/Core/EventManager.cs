using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap
{
    // Typed events keep publishers independent of the scene objects that receive them.
    public static class EventManager
    {
        private static readonly Dictionary<Type, Delegate> listeners = new Dictionary<Type, Delegate>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => listeners.Clear();

        public static void Subscribe<T>(Action<T> listener)
        {
            listeners.TryGetValue(typeof(T), out Delegate existing);
            listeners[typeof(T)] = Delegate.Combine(existing, listener);
        }

        public static void Unsubscribe<T>(Action<T> listener)
        {
            if (!listeners.TryGetValue(typeof(T), out Delegate existing)) return;
            Delegate remaining = Delegate.Remove(existing, listener);
            if (remaining == null) listeners.Remove(typeof(T));
            else listeners[typeof(T)] = remaining;
        }

        public static void Publish<T>(T message)
        {
            if (listeners.TryGetValue(typeof(T), out Delegate listener)) ((Action<T>)listener).Invoke(message);
        }
    }

    public readonly struct LightsStateRequested
    {
        public readonly Scene Scene;
        public readonly bool IsOn;
        public LightsStateRequested(Scene scene, bool isOn) { Scene = scene; IsOn = isOn; }
    }
}
