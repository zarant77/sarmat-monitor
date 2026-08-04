using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace SarmatPlugin.Core
{
    public enum Severity { Inactive = 0, Ok = 1, Warning = 2, Critical = 3 }
    public enum AlertKind { Obs, Satellites, Hdop, Battery, Current, Ruijie }
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
        public double Heading { get; set; }
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

    public static class WidgetStatusPolicy
    {
        public static WidgetStatus Obs(bool armed, ObsStatus status)
        {
            if (status == null || !status.Connected || !status.Recording.HasValue)
                return WidgetStatus.Bad;
            if (armed)
                return status.Recording.Value ? WidgetStatus.Good : WidgetStatus.Bad;
            return status.Recording.Value ? WidgetStatus.Normal : WidgetStatus.Good;
        }
    }

    public sealed class RuijieStatus
    {
        public bool Connected { get; set; }
        public bool Stale { get; set; }
        public int? Rssi { get; set; }
        public int? QualityPercent { get; set; }
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
        [DataMember] public string ObsEndpoint { get; set; } = "ws://127.0.0.1:4455";
        [DataMember] public string ObsPassword { get; set; } = "";
        [DataMember] public double ObsReconnectSeconds { get; set; } = 2;
        [DataMember] public string RuijieAddress { get; set; } = "10.44.77.254";
        [DataMember] public string RuijieUsername { get; set; } = "admin";
        [DataMember] public string RuijiePassword { get; set; } = "";
        [DataMember] public double RuijiePollSeconds { get; set; } = 2;
        [DataMember] public double RuijieRequestTimeoutSeconds { get; set; } = 12;
        [DataMember] public double RuijieStaleSeconds { get; set; } = 8;
        [DataMember] public bool AggregatorEnabled { get; set; }
        [DataMember] public string AggregatorUrl { get; set; } = "ws://127.0.0.1:8080/ws/station";
        [DataMember] public string AggregatorSecret { get; set; } = "";
        [DataMember] public double AggregatorReconnectSeconds { get; set; } = 5;
        [DataMember] public bool AudioEnabled { get; set; } = true;
        [DataMember] public double AudioVolume { get; set; } = 0.8;
        [DataMember] public double AudioAlertCooldownSeconds { get; set; } = 10;
        [DataMember] public string AudioWarningSoundPath { get; set; } = "";
        [DataMember] public bool DebugLogging { get; set; }
        [DataMember] public bool VehicleAutoReconnectEnabled { get; set; } = true;
        [DataMember] public double VehicleReconnectTimeoutSeconds { get; set; } = 10;
        [DataMember] public List<string> EnabledWidgets { get; set; } = WidgetCatalog.DefaultIds.ToList();
        [DataMember] public Dictionary<string, bool> HudElements { get; set; } =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        [DataMember] public bool GStreamerWasStarted { get; set; }
        [DataMember] public string CameraUrl { get; set; } = "rtsp://192.168.69.5:554/stream=0";
        [DataMember] public string CameraProtocol { get; set; } = "tcp";
        [DataMember] public int CameraLatencyMs { get; set; } = 150;
        [DataMember] public bool CameraDropOnLatency { get; set; } = true;
        [DataMember] public string CameraDepayloader { get; set; } = "rtph264depay";
        [DataMember] public string CameraParser { get; set; } = "h264parse";
        [DataMember] public string CameraDecoder { get; set; } = "avdec_h264";
        [DataMember] public int CameraQueueMaxBuffers { get; set; } = 1;
        [DataMember] public string CameraQueueLeaky { get; set; } = "downstream";
        [DataMember] public string CameraConverter { get; set; } = "videoconvert";
        [DataMember] public string CameraRawFormat { get; set; } = "BGRA";
        [DataMember] public string CameraAppSinkName { get; set; } = "outsink";
        [DataMember] public bool CameraSync { get; set; }

        public void Normalize()
        {
            ObsReconnectSeconds = Math.Max(1, ObsReconnectSeconds);
            RuijiePollSeconds = Math.Max(0.5, RuijiePollSeconds);
            RuijieRequestTimeoutSeconds = Math.Max(1, RuijieRequestTimeoutSeconds);
            RuijieStaleSeconds = Math.Max(RuijiePollSeconds, RuijieStaleSeconds);
            RuijieAddress = NormalizeRouterIp(RuijieAddress);
            AggregatorUrl = string.IsNullOrWhiteSpace(AggregatorUrl)
                ? "ws://127.0.0.1:8080/ws/station" : AggregatorUrl.Trim();
            AggregatorSecret = (AggregatorSecret ?? "").Trim();
            AggregatorReconnectSeconds = Math.Max(1, Math.Min(300, AggregatorReconnectSeconds));
            AudioVolume = Math.Max(0, Math.Min(1, AudioVolume));
            if (AudioAlertCooldownSeconds <= 0) AudioAlertCooldownSeconds = 10;
            AudioAlertCooldownSeconds = Math.Max(1, Math.Min(300, AudioAlertCooldownSeconds));
            AudioWarningSoundPath = (AudioWarningSoundPath ?? "").Trim();
            if (VehicleReconnectTimeoutSeconds <= 0) VehicleReconnectTimeoutSeconds = 10;
            VehicleReconnectTimeoutSeconds = Math.Max(3, Math.Min(300, VehicleReconnectTimeoutSeconds));
            CameraUrl = string.IsNullOrWhiteSpace(CameraUrl)
                ? "rtsp://192.168.69.5:554/stream=0" : CameraUrl.Trim();
            CameraProtocol = string.IsNullOrWhiteSpace(CameraProtocol) ? "tcp" : CameraProtocol.Trim();
            CameraLatencyMs = Math.Max(0, Math.Min(10000, CameraLatencyMs));
            CameraDepayloader = Default(CameraDepayloader, "rtph264depay");
            CameraParser = Default(CameraParser, "h264parse");
            CameraDecoder = Default(CameraDecoder, "avdec_h264");
            CameraQueueMaxBuffers = Math.Max(1, Math.Min(1000, CameraQueueMaxBuffers));
            CameraQueueLeaky = Default(CameraQueueLeaky, "downstream");
            CameraConverter = Default(CameraConverter, "videoconvert");
            CameraRawFormat = Default(CameraRawFormat, "BGRA");
            CameraAppSinkName = Default(CameraAppSinkName, "outsink");
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
        }

        internal static string NormalizeRouterIp(string value)
        {
            var text = string.IsNullOrWhiteSpace(value) ? "10.44.77.254" : value.Trim();
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                return uri.IsDefaultPort ? uri.Host : uri.Authority;
            var slash = text.IndexOfAny(new[] {'/', '\\'});
            return (slash >= 0 ? text.Substring(0, slash) : text).Trim().TrimEnd('/');
        }

        private static string Default(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
