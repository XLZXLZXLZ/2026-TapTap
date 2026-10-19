using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap
{
    [Flags]
    public enum GameplayAction { Rewind = 1, Input = 2, DebugCamera = 4, PlayerControls = 8 }

    // Scene-scoped leases. Systems depend on actions, never on the sequence requesting them.
    public static class GameplayActionLocks
    {
        private struct Entry { public int Scene; public GameplayAction Actions; }
        private static readonly Dictionary<Component, Entry> entries = new Dictionary<Component, Entry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => entries.Clear();

        public static void Acquire(Component owner, GameplayAction actions)
        {
            if (owner != null) entries[owner] = new Entry { Scene = owner.gameObject.scene.handle, Actions = actions };
        }

        public static void Release(Component owner) { if (owner != null) entries.Remove(owner); }

        public static bool IsLocked(GameplayAction action, Scene scene, Component exceptOwner = null)
        {
            foreach (var entry in entries)
                if (entry.Key != null && entry.Key != exceptOwner && entry.Value.Scene == scene.handle && (entry.Value.Actions & action) != 0)
                    return true;
            return false;
        }
    }
}
