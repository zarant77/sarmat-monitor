using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SarmatPlugin.Core
{
    [DataContract]
    public sealed class MinimumThreshold
    {
        [DataMember(Name = "goodMin")] public double Good { get; set; }
        [DataMember(Name = "normalMin")] public double Normal { get; set; }
    }

    [DataContract]
    public sealed class MaximumThreshold
    {
        [DataMember(Name = "goodMax")] public double Good { get; set; }
        [DataMember(Name = "normalMax")] public double Normal { get; set; }
    }

    [DataContract]
    public sealed class TelemetryThresholdConfiguration
    {
        [DataMember(Name = "voltage")] public MinimumThreshold Voltage { get; set; }
        [DataMember(Name = "current")] public MaximumThreshold Current { get; set; }
        [DataMember(Name = "satellites")] public MinimumThreshold Satellites { get; set; }
        [DataMember(Name = "hdop")] public MaximumThreshold Hdop { get; set; }
        [DataMember(Name = "linkRssi")] public MinimumThreshold LinkRssi { get; set; }
        [DataMember(Name = "distanceToHome")] public MaximumThreshold DistanceToHome { get; set; }
    }

    public static class TelemetryThresholds
    {
        public static readonly TelemetryThresholdConfiguration Current = Load();

        private static TelemetryThresholdConfiguration Load()
        {
            const string resourceName = "SarmatPlugin.Config.telemetry-thresholds.json";
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null) throw new InvalidOperationException("Embedded telemetry thresholds are missing");
                var serializer = new DataContractJsonSerializer(typeof(TelemetryThresholdConfiguration));
                var value = (TelemetryThresholdConfiguration)serializer.ReadObject(stream);
                Validate(value);
                return value;
            }
        }

        private static void Validate(TelemetryThresholdConfiguration value)
        {
            if (value == null) throw new InvalidDataException("Telemetry thresholds are empty");
            ValidateMinimum(value.Voltage, "voltage");
            ValidateMaximum(value.Current, "current");
            ValidateMinimum(value.Satellites, "satellites");
            ValidateMaximum(value.Hdop, "hdop");
            ValidateMinimum(value.LinkRssi, "linkRssi");
            ValidateMaximum(value.DistanceToHome, "distanceToHome");
        }

        private static void ValidateMinimum(MinimumThreshold value, string name)
        {
            if (value == null || value.Normal >= value.Good)
                throw new InvalidDataException(name + ".normalMin must be less than goodMin");
        }

        private static void ValidateMaximum(MaximumThreshold value, string name)
        {
            if (value == null || value.Good >= value.Normal)
                throw new InvalidDataException(name + ".goodMax must be less than normalMax");
        }
    }
}
