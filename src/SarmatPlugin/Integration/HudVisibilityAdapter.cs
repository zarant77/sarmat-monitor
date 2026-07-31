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

        public void Apply(IReadOnlyDictionary<string, bool> values)
        {
            if (hud == null || values == null) return;
            foreach (var item in values)
            {
                var property = hud.GetType().GetProperty(item.Key, Flags);
                if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
                    property.SetValue(hud, item.Value, null);
            }
            hud.GetType().GetMethod("doResize", Flags)?.Invoke(hud, null);
        }
    }
}
