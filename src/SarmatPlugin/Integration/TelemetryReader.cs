using System;
using System.Globalization;
using System.Reflection;
using SarmatPlugin.Core;

namespace SarmatPlugin.Integration
{
    public sealed class TelemetryReader
    {
        private readonly Func<object> currentState;
        public TelemetryReader(Func<object> currentState) { this.currentState = currentState; }

        public TelemetrySnapshot Read()
        {
            var cs = currentState?.Invoke();
            return new TelemetrySnapshot
            {
                Armed = ReadBool(cs, "armed"),
                BatteryVoltage = ReadDouble(cs, "battery_voltage"),
                Satellites = (int)ReadDouble(cs, "satcount"),
                Hdop = ReadDouble(cs, "gpshdop"),
                DistanceToHomeMeters = ReadDouble(cs, "DistToHome"),
                BatteryUsedMah = ReadDouble(cs, "battery_usedmah"),
                TimestampUtc = DateTime.UtcNow
            };
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
    }
}
