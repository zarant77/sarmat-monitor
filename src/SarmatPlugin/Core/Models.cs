using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SarmatPlugin.Core
{
    public enum Severity { Inactive = 0, Ok = 1, Warning = 2, Critical = 3 }
    public enum AlertKind { Obs, Satellites, Hdop, Battery, Ruijie }

    public sealed class TelemetrySnapshot
    {
        public bool Armed { get; set; }
        public double BatteryVoltage { get; set; }
        public int Satellites { get; set; }
        public double Hdop { get; set; }
        public double DistanceToHomeMeters { get; set; }
        public double BatteryUsedMah { get; set; }
        public DateTime TimestampUtc { get; set; }
    }

    public sealed class ObsStatus
    {
        public bool Connected { get; set; }
        public bool? Recording { get; set; }
        public string Error { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    public sealed class RuijieStatus
    {
        public bool Connected { get; set; }
        public bool Stale { get; set; }
        public int? Rssi { get; set; }
        public string SignalQuality { get; set; }
        public string Error { get; set; }
        public DateTime LastSuccessUtc { get; set; }
    }

    public sealed class AlertReason
    {
        public AlertKind Kind { get; set; }
        public Severity Severity { get; set; }
        public string Text { get; set; }
    }

    public sealed class SafetySnapshot
    {
        public Severity Severity { get; set; }
        public IReadOnlyList<AlertReason> Reasons { get; set; }
        public bool Restored { get; set; }
    }

    [DataContract]
    public sealed class PluginSettings
    {
        [DataMember] public bool AlertsEnabled { get; set; } = true;
        [DataMember] public int MinimumSatellites { get; set; } = 20;
        [DataMember] public double MaximumHdop { get; set; } = 0.8;
        [DataMember] public double MinimumBatteryVoltage { get; set; } = 44.0;
        [DataMember] public double SafeDistanceToHomeMeters { get; set; } = 50.0;
        [DataMember] public double ActivationDebounceSeconds { get; set; } = 2;
        [DataMember] public double RecoveryDebounceSeconds { get; set; } = 2;
        [DataMember] public double RepeatIntervalSeconds { get; set; } = 10;
        [DataMember] public double ArmedGracePeriodSeconds { get; set; } = 3;
        [DataMember] public string ObsEndpoint { get; set; } = "ws://127.0.0.1:4455";
        [DataMember] public string ObsPassword { get; set; } = "";
        [DataMember] public double ObsReconnectSeconds { get; set; } = 2;
        [DataMember] public string RuijieAddress { get; set; } = "https://192.168.69.252";
        [DataMember] public string RuijieUsername { get; set; } = "admin";
        [DataMember] public string RuijiePassword { get; set; } = "";
        [DataMember] public double RuijiePollSeconds { get; set; } = 2;
        [DataMember] public double RuijieRequestTimeoutSeconds { get; set; } = 12;
        [DataMember] public double RuijieStaleSeconds { get; set; } = 8;
        [DataMember] public bool RuijieAllowInsecureTls { get; set; } = true;
        [DataMember] public bool AudioEnabled { get; set; } = true;
        [DataMember] public bool AudioMuted { get; set; }
        [DataMember] public double AudioVolume { get; set; } = 0.8;
        [DataMember] public bool ShowPanel { get; set; } = true;
        [DataMember] public bool StartAutomatically { get; set; } = true;
        [DataMember] public bool DebugLogging { get; set; }
        [DataMember] public double HeaderFontSize { get; set; } = 10.0;
        [DataMember] public double ValueFontSize { get; set; } = 15.0;

        public void Normalize()
        {
            MinimumSatellites = Math.Max(1, MinimumSatellites);
            MaximumHdop = Math.Max(0.05, MaximumHdop);
            MinimumBatteryVoltage = Math.Max(1, MinimumBatteryVoltage);
            if (SafeDistanceToHomeMeters <= 0) SafeDistanceToHomeMeters = 50.0;
            ActivationDebounceSeconds = Math.Max(0, ActivationDebounceSeconds);
            RecoveryDebounceSeconds = Math.Max(0, RecoveryDebounceSeconds);
            RepeatIntervalSeconds = Math.Max(1, RepeatIntervalSeconds);
            ArmedGracePeriodSeconds = Math.Max(0, ArmedGracePeriodSeconds);
            ObsReconnectSeconds = Math.Max(1, ObsReconnectSeconds);
            RuijiePollSeconds = Math.Max(0.5, RuijiePollSeconds);
            RuijieRequestTimeoutSeconds = Math.Max(1, RuijieRequestTimeoutSeconds);
            RuijieStaleSeconds = Math.Max(RuijiePollSeconds, RuijieStaleSeconds);
            AudioVolume = Math.Max(0, Math.Min(1, AudioVolume));
            if (HeaderFontSize <= 0) HeaderFontSize = 10.0;
            if (ValueFontSize <= 0) ValueFontSize = 15.0;
            HeaderFontSize = Math.Max(6, Math.Min(24, HeaderFontSize));
            ValueFontSize = Math.Max(8, Math.Min(40, ValueFontSize));
            RuijieUsername = string.IsNullOrWhiteSpace(RuijieUsername) ? "admin" : RuijieUsername.Trim();
        }
    }
}
