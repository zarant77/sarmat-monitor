using System;
using System.Collections.Generic;
using SarmatVisionHold.Replay.Math;

namespace SarmatVisionHold.Replay.Telemetry
{
    public sealed class TimedAttitude
    {
        public double TimeSeconds, BootTimeSeconds;
        public DateTime ReceiveTimeUtc;
        public Quaterniond BodyToNed = Quaterniond.Identity;
        public bool IsQuaternion;
        public string Source;
    }
    public sealed class TimedGyro
    {
        public double TimeSeconds, BootTimeSeconds;
        public DateTime ReceiveTimeUtc;
        public Vector3d BodyRateRadPerSecond;
        public string Source;
    }
    public sealed class TimedAltitude
    {
        public double TimeSeconds;
        public DateTime ReceiveTimeUtc;
        public double Meters;
        public bool Valid;
        public string Source;
    }
    public sealed class TimedVelocity
    {
        public double TimeSeconds;
        public Vector3d NedMetersPerSecond;
        public string Source;
    }
    public sealed class TimedVehicleState
    {
        public double TimeSeconds;
        public bool Armed;
        public uint CustomMode;
        public ushort[] RcChannels;
    }

    public sealed class ReplayTelemetryArchive
    {
        public readonly List<TimedAttitude> Attitudes = new List<TimedAttitude>();
        public readonly List<TimedGyro> Gyros = new List<TimedGyro>();
        public readonly List<TimedAltitude> Altitudes = new List<TimedAltitude>();
        public readonly List<TimedVelocity> Velocities = new List<TimedVelocity>();
        public readonly List<TimedVehicleState> States = new List<TimedVehicleState>();
        public readonly Dictionary<string, int> MessageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public string Path;
        public long Bytes;
        public double DurationSeconds;
        public int BadPackets;
        public void Count(string name) { int value; MessageCounts.TryGetValue(name ?? "UNKNOWN", out value); MessageCounts[name ?? "UNKNOWN"] = value + 1; }
        public void Sort()
        {
            Attitudes.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
            Gyros.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
            Altitudes.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
            Velocities.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
            States.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
            var max = 0d;
            if (Attitudes.Count > 0) max = System.Math.Max(max, Attitudes[Attitudes.Count - 1].TimeSeconds);
            if (Gyros.Count > 0) max = System.Math.Max(max, Gyros[Gyros.Count - 1].TimeSeconds);
            if (Altitudes.Count > 0) max = System.Math.Max(max, Altitudes[Altitudes.Count - 1].TimeSeconds);
            DurationSeconds = max;
        }
    }

    public sealed class ReplayTelemetrySample
    {
        public double RequestedTimeSeconds;
        public Quaterniond BodyToNed = Quaterniond.Identity;
        public Vector3d EulerRad;
        public Vector3d BodyRateRadPerSecond;
        public double AltitudeMeters;
        public bool AttitudeValid, GyroValid, AltitudeValid;
        public double AttitudeAgeSeconds, GyroAgeSeconds, AltitudeAgeSeconds;
        public double AttitudeSpanSeconds, GyroSpanSeconds, AltitudeSpanSeconds;
        public string AttitudeSource, GyroSource, AltitudeSource;
        public double MaxAgeSeconds => System.Math.Max(AttitudeAgeSeconds, System.Math.Max(GyroAgeSeconds, AltitudeAgeSeconds));
    }
}
