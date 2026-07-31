using System;
using System.Collections.Generic;
using System.Reflection;
using SarmatPlugin.Core;

namespace SarmatPlugin.Integration
{
    public sealed class HudVisibilityAdapter
    {
        private readonly object hud;
        private const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public HudVisibilityAdapter(object hud) { this.hud = hud; }

        public Dictionary<string, bool> Read()
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (hud == null) return result;
            foreach (var item in HudElementCatalog.Elements)
            {
                var property = hud.GetType().GetProperty(item.Key, Flags);
                if (property != null && property.PropertyType == typeof(bool))
                    result[item.Key] = (bool)property.GetValue(hud, null);
            }
            return result;
        }

        public bool Apply(IReadOnlyDictionary<string, bool> values)
        {
            if (hud == null || values == null || values.Count == 0) return false;
            var changed = false;
            foreach (var item in values)
            {
                var property = hud.GetType().GetProperty(item.Key, Flags);
                if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
                {
                    var current = (bool)property.GetValue(hud, null);
                    if (current == item.Value) continue;
                    property.SetValue(hud, item.Value, null);
                    changed = true;
                }
            }
            if (changed) hud.GetType().GetMethod("doResize", Flags)?.Invoke(hud, null);
            return changed;
        }
    }
}
