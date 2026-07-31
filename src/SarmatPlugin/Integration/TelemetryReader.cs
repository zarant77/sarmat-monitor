using System;
using System.Globalization;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using SarmatPlugin.Core;

namespace SarmatPlugin.Integration
{
    public sealed class TelemetryReader
    {
        private readonly Func<object> currentState;
        public TelemetryReader(Func<object> currentState) { this.currentState = currentState; }

        public TelemetrySnapshot Read(IEnumerable<string> enabledWidgets = null)
        {
            var cs = currentState?.Invoke();
            var snapshot = new TelemetrySnapshot
            {
                Armed = ReadBool(cs, "armed"),
                Connected = ReadBool(cs, "connected"),
                FlightMode = Convert.ToString(Member(cs, "mode"), CultureInfo.InvariantCulture) ?? "",
                BatteryVoltage = ReadDouble(cs, "battery_voltage"),
                Satellites = (int)ReadDouble(cs, "satcount"),
                Hdop = ReadDouble(cs, "gpshdop"),
                DistanceToHomeMeters = ReadDouble(cs, "DistToHome"),
                BatteryUsedMah = ReadDouble(cs, "battery_usedmah"),
                GroundSpeed = ReadDouble(cs, "groundspeed"),
                VerticalSpeed = ReadDouble(cs, "climbrate"),
                AirSpeed = ReadDouble(cs, "airspeed"),
                Altitude = ReadDouble(cs, "alt"),
                CurrentAmps = ReadDouble(cs, "current"),
                TimestampUtc = DateTime.UtcNow
            };
            if (cs != null && enabledWidgets != null)
            {
                var enabled = new HashSet<string>(enabledWidgets, StringComparer.OrdinalIgnoreCase);
                foreach (var definition in WidgetCatalog.Definitions.Where(x =>
                    x.MemberName != null && enabled.Contains(x.Id)))
                {
                    try
                    {
                        var value = Member(cs, definition.MemberName);
                        snapshot.AdditionalTelemetry[definition.Id] = Format(value);
                    }
                    catch
                    {
                        snapshot.AdditionalTelemetry[definition.Id] = "N/A";
                    }
                }
            }
            return snapshot;
        }
        public double ReadRcInput(int channel)
        {
            if (channel < 1 || channel > 16) return 0;
            return ReadDouble(currentState?.Invoke(), "ch" + channel + "in");
        }
        private static object Member(object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (p != null) return p.GetValue(target, null);
            var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            return f?.GetValue(target);
        }
        private static bool ReadBool(object target, string name)
        {
            var value = Member(target, name);
            return value != null && Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        private static double ReadDouble(object target, string name)
        {
            var value = Member(target, name);
            return value == null ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        private static string Format(object value)
        {
            if (value == null) return "N/A";
            if (value is bool) return (bool)value ? "Yes" : "No";
            if (value is DateTime) return ((DateTime)value).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            if (value is float || value is double || value is decimal)
                return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("0.###",
                    CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
