using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace SarmatPlugin.Core
{
    public enum Severity { Inactive = 0, Ok = 1, Warning = 2, Critical = 3 }
    public enum AlertKind { Obs, Satellites, Hdop, Battery, Ruijie }
    public enum WidgetStatus { Good = 0, Normal = 1, Bad = 2 }

    public sealed class TelemetrySnapshot
    {
        public bool Armed { get; set; }
        public bool Connected { get; set; }
        public string FlightMode { get; set; }
        public double BatteryVoltage { get; set; }
        public int Satellites { get; set; }
        public double Hdop { get; set; }
        public double DistanceToHomeMeters { get; set; }
        public double BatteryUsedMah { get; set; }
        public double GroundSpeed { get; set; }
        public double VerticalSpeed { get; set; }
        public double AirSpeed { get; set; }
        public double Altitude { get; set; }
        public double CurrentAmps { get; set; }
        public Dictionary<string, string> AdditionalTelemetry { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
        [DataMember] public List<string> EnabledWidgets { get; set; } = WidgetCatalog.DefaultIds.ToList();
        [DataMember] public Dictionary<string, bool> HudElements { get; set; } =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        [DataMember] public bool GStreamerWasStarted { get; set; }
        [DataMember] public bool LimaEnabled { get; set; }
        [DataMember] public int LimaRcChannel { get; set; } = 7;
        [DataMember] public int LimaPwmThreshold { get; set; } = 1800;
        [DataMember] public bool LimaPressedWhenHigh { get; set; } = true;
        [DataMember] public string LimaFlightMode { get; set; } = "AltHold";
        [DataMember] public bool LimaSettingsInitialized { get; set; } = true;

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
            RuijieUsername = string.IsNullOrWhiteSpace(RuijieUsername) ? "admin" : RuijieUsername.Trim();
            if (EnabledWidgets == null)
                EnabledWidgets = WidgetCatalog.DefaultIds.ToList();
            else
                EnabledWidgets = EnabledWidgets.Where(WidgetCatalog.IsKnown)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (HudElements == null)
                HudElements = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            else
                HudElements = HudElements.Where(x => HudElementCatalog.Elements.ContainsKey(x.Key))
                    .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            if (!LimaSettingsInitialized)
            {
                LimaRcChannel = 7;
                LimaPwmThreshold = 1800;
                LimaPressedWhenHigh = true;
                LimaFlightMode = "AltHold";
                LimaSettingsInitialized = true;
            }
            LimaRcChannel = Math.Max(1, Math.Min(16, LimaRcChannel));
            LimaPwmThreshold = Math.Max(800, Math.Min(2200, LimaPwmThreshold));
            if (string.IsNullOrWhiteSpace(LimaFlightMode)) LimaFlightMode = "AltHold";
            else LimaFlightMode = LimaFlightMode.Trim();
        }
    }
}
