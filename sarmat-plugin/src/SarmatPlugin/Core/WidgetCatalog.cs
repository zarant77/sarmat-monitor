using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SarmatPlugin.Core
{
    public sealed class WidgetDefinition
    {
        public WidgetDefinition(string id, string title, string memberName = null)
        { Id = id; Title = title; MemberName = memberName; }
        public string Id { get; }
        public string Title { get; }
        public string MemberName { get; }
        public override string ToString() => Title;
    }

    public static class WidgetCatalog
    {
        private static readonly List<WidgetDefinition> definitions = new[]
        {
            new WidgetDefinition("sat_count", "Sat Count"),
            new WidgetDefinition("gps_hdop", "GPS HDOP"),
            new WidgetDefinition("dist_home", "Dist to Home"),
            new WidgetDefinition("bat_used", "Bat used"),
            new WidgetDefinition("ruijie", "Ruijie"),
            new WidgetDefinition("obs", "OBS"),
            new WidgetDefinition("ground_speed", "Ground Speed"),
            new WidgetDefinition("vertical_speed", "Vertical Speed"),
            new WidgetDefinition("air_speed", "Air Speed"),
            new WidgetDefinition("altitude", "Altitude"),
            new WidgetDefinition("battery_voltage", "Battery Voltage"),
            new WidgetDefinition("current", "Current")
        }.ToList();
        private static readonly object sync = new object();

        public static readonly IReadOnlyList<string> DefaultIds =
            definitions.Select(x => x.Id).ToArray();

        public static IReadOnlyList<WidgetDefinition> Definitions
        {
            get { lock (sync) return definitions.ToArray(); }
        }

        public static void Discover(object currentState)
        {
            if (currentState == null) return;
            var excluded = new HashSet<string>(new[]
            {
                "armed", "battery_voltage", "satcount", "gpshdop", "DistToHome",
                "battery_usedmah", "groundspeed", "climbrate", "verticalspeed",
                "airspeed", "alt", "current"
            }, StringComparer.OrdinalIgnoreCase);
            var found = new List<WidgetDefinition>();
            foreach (var property in currentState.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                if (property.CanRead && property.GetIndexParameters().Length == 0 &&
                    IsScalar(property.PropertyType) && !excluded.Contains(property.Name))
                    found.Add(new WidgetDefinition("telemetry:" + property.Name,
                        Humanize(property.Name), property.Name));
            foreach (var field in currentState.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (IsScalar(field.FieldType) && !excluded.Contains(field.Name))
                    found.Add(new WidgetDefinition("telemetry:" + field.Name,
                        Humanize(field.Name), field.Name));
            lock (sync)
            {
                foreach (var item in found.OrderBy(x => x.Title))
                    if (!definitions.Any(x => string.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase)))
                        definitions.Add(item);
            }
        }

        private static bool IsScalar(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type.IsEnum || type == typeof(string) || type == typeof(bool) ||
                type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
                type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
                type == typeof(long) || type == typeof(ulong) || type == typeof(float) ||
                type == typeof(double) || type == typeof(decimal) || type == typeof(DateTime);
        }

        private static string Humanize(string name)
        {
            var text = name.Replace('_', ' ').Trim();
            var result = new StringBuilder();
            for (var i = 0; i < text.Length; i++)
            {
                if (i > 0 && char.IsUpper(text[i]) && char.IsLower(text[i - 1])) result.Append(' ');
                result.Append(text[i]);
            }
            return result.ToString();
        }

        public static bool IsKnown(string id) =>
            !string.IsNullOrWhiteSpace(id) &&
            Definitions.Any(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
