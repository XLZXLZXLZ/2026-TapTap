using System;
using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    public static class WorldMapProgressStore
    {
        [Serializable]
        private sealed class SaveData
        {
            public List<string> Completed = new List<string>();
            public List<string> Unlocked = new List<string>();
            public bool UnlockAll;
        }

        private static string Key(WorldMapDefinition world) => "TapTap.WorldProgress."
            + (string.IsNullOrEmpty(world.ProgressId) ? world.name : world.ProgressId);
        private static SaveData Read(WorldMapDefinition world)
        {
            string json = PlayerPrefs.GetString(Key(world), "");
            if (string.IsNullOrEmpty(json)) return new SaveData();
            try
            {
                SaveData data = JsonUtility.FromJson<SaveData>(json) ?? new SaveData();
                if (data.Completed == null) data.Completed = new List<string>();
                if (data.Unlocked == null) data.Unlocked = new List<string>();
                return data;
            }
            catch (ArgumentException) { return new SaveData(); }
        }
        private static void Write(WorldMapDefinition world, SaveData data)
        {
            PlayerPrefs.SetString(Key(world), JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }
        public static HashSet<string> Completed(WorldMapDefinition world) => new HashSet<string>(Read(world).Completed);
        public static bool IsUnlocked(WorldMapDefinition world, WorldMapModule module)
        {
            var ordered = world.OrderedModules();
            int index = ordered.IndexOf(module);
            if (index < 0) return false;
            SaveData data = Read(world);
            return index == 0 || data.UnlockAll || data.Unlocked.Contains(module.Id) || data.Completed.Contains(module.Id)
                || data.Completed.Contains(ordered[index - 1].Id);
        }
        public static void Unlock(WorldMapDefinition world, WorldMapModule module)
        {
            SaveData data = Read(world);
            if (data.Unlocked.Contains(module.Id)) return;
            data.Unlocked.Add(module.Id);
            Write(world, data);
        }
        public static void Complete(WorldMapDefinition world, WorldMapModule module)
        {
            SaveData data = Read(world);
            if (!data.Completed.Contains(module.Id)) data.Completed.Add(module.Id);
            var ordered = world.OrderedModules();
            int next = ordered.IndexOf(module) + 1;
            if (next > 0 && next < ordered.Count && !data.Unlocked.Contains(ordered[next].Id))
                data.Unlocked.Add(ordered[next].Id);
            Write(world, data);
        }
        public static void UnlockAll(WorldMapDefinition world)
        {
            SaveData data = Read(world);
            data.UnlockAll = true;
            Write(world, data);
        }
        public static void Clear(WorldMapDefinition world) { PlayerPrefs.DeleteKey(Key(world)); PlayerPrefs.Save(); }
    }
}
