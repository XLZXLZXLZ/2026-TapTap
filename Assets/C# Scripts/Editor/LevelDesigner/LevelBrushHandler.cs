using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public sealed class LevelBuildContext
    {
        public LevelDefinition Definition { get; }
        public LevelRegion Region { get; }
        public float UnitSize => Definition.Palette.UnitSize;

        public LevelBuildContext(LevelDefinition definition, LevelRegion region)
        {
            Definition = definition;
            Region = region;
        }
    }

    // Add a concrete handler with a unique Id to extend the palette without changing the window.
    public abstract class LevelBrushHandler
    {
        public abstract string Id { get; }
        public virtual bool IsCheckpoint => false;
        public virtual bool TestOnly => false;
        public virtual LevelPlacementSettings CreateSettings(LevelBrush brush) => new EmptyPlacementSettings();
        public virtual void DrawSettings(LevelPlacement placement) { }
        public virtual void ConfigureInstance(GameObject instance, LevelPlacement placement, LevelBuildContext context) { }
        public virtual string ValidatePlacement(LevelPlacement placement) => null;
    }

    public static class LevelBrushHandlers
    {
        private static Dictionary<string, LevelBrushHandler> handlers;

        public static LevelBrushHandler Get(string id)
        {
            if (handlers == null)
                Discover();
            return id != null && handlers.TryGetValue(id, out LevelBrushHandler handler) ? handler : null;
        }

        private static void Discover()
        {
            handlers = new Dictionary<string, LevelBrushHandler>(StringComparer.Ordinal);
            var ambiguousIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Type type in TypeCache.GetTypesDerivedFrom<LevelBrushHandler>())
            {
                if (type.IsAbstract || type.ContainsGenericParameters || type.GetConstructor(Type.EmptyTypes) == null)
                    continue;
                try
                {
                    var handler = (LevelBrushHandler)Activator.CreateInstance(type);
                    if (string.IsNullOrWhiteSpace(handler.Id))
                        throw new InvalidOperationException("The handler Id is empty.");
                    if (handlers.ContainsKey(handler.Id) || ambiguousIds.Contains(handler.Id))
                    {
                        handlers.Remove(handler.Id);
                        ambiguousIds.Add(handler.Id);
                        throw new InvalidOperationException("Duplicate level brush handler Id: " + handler.Id);
                    }
                    handlers.Add(handler.Id, handler);
                }
                catch (Exception exception)
                {
                    Debug.LogError("Cannot register level brush handler " + type.FullName + ": " + exception.Message);
                }
            }
        }
    }
}
