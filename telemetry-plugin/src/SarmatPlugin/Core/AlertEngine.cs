using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SarmatPlugin.Core
{
    public sealed class AlertEngine
    {
        private const double ActivationDebounceSeconds = 2;
        private const double RecoveryDebounceSeconds = 2;
        private const double ArmedGracePeriodSeconds = 3;

        private sealed class Gate
        {
            public bool Active;
            public bool Candidate;
            public DateTime ChangedUtc;
        }

        private readonly Dictionary<AlertKind, Gate> gates =
            Enum.GetValues(typeof(AlertKind)).Cast<AlertKind>().ToDictionary(x => x, x => new Gate());
        private bool wasArmed;
        private bool hadAlertWhileArmed;
        private DateTime armedSinceUtc;

        public SafetySnapshot Update(TelemetrySnapshot telemetry, ObsStatus obs, RuijieStatus ruijie,
            PluginSettings settings, DateTime nowUtc)
        {
            if (!telemetry.Armed)
            {
                Reset();
                wasArmed = telemetry.Armed;
                return Snapshot(Severity.Inactive, new List<AlertReason>(), false);
            }

            if (!wasArmed)
            {
                armedSinceUtc = nowUtc;
                wasArmed = true;
            }
            if ((nowUtc - armedSinceUtc).TotalSeconds < ArmedGracePeriodSeconds)
                return Snapshot(Severity.Ok, new List<AlertReason>(), false);

            var thresholds = TelemetryThresholds.Current;
            var conditions = new Dictionary<AlertKind, bool>
            {
                [AlertKind.Obs] = !obs.Connected || obs.Recording != true,
                [AlertKind.Satellites] = telemetry.Satellites < thresholds.Satellites.Normal,
                [AlertKind.Hdop] = telemetry.Hdop > thresholds.Hdop.Normal,
                [AlertKind.Battery] = telemetry.BatteryVoltage > 0 &&
                    telemetry.BatteryVoltage < thresholds.Voltage.Normal,
                [AlertKind.Current] = telemetry.CurrentAmps > thresholds.Current.Normal,
                [AlertKind.Ruijie] = !ruijie.Connected || ruijie.Stale ||
                    (ruijie.Rssi.HasValue && ruijie.Rssi.Value < thresholds.LinkRssi.Normal)
            };

            foreach (var pair in conditions)
                Debounce(gates[pair.Key], pair.Value, nowUtc);

            var reasons = new List<AlertReason>();
            if (gates[AlertKind.Ruijie].Active)
                reasons.Add(Reason(AlertKind.Ruijie, Severity.Critical,
                    !ruijie.Connected ? "RUIJIE DISCONNECTED" : ruijie.Stale ? "RUIJIE STALE" :
                    $"RUIJIE RSSI: {ruijie.Rssi} dBm < {thresholds.LinkRssi.Normal:0} dBm"));
            if (gates[AlertKind.Battery].Active)
                reasons.Add(Reason(AlertKind.Battery, Severity.Critical,
                    string.Format(CultureInfo.InvariantCulture, "BATTERY: {0:0.0} V < {1:0.0} V",
                        telemetry.BatteryVoltage, thresholds.Voltage.Normal)));
            if (gates[AlertKind.Current].Active)
                reasons.Add(Reason(AlertKind.Current, Severity.Critical,
                    string.Format(CultureInfo.InvariantCulture, "CURRENT: {0:0.0} A > {1:0.0} A",
                        telemetry.CurrentAmps, thresholds.Current.Normal)));
            if (gates[AlertKind.Obs].Active)
                reasons.Add(Reason(AlertKind.Obs, Severity.Warning,
                    obs.Connected ? "OBS NOT RECORDING" : "OBS DISCONNECTED"));
            if (gates[AlertKind.Satellites].Active)
                reasons.Add(Reason(AlertKind.Satellites, Severity.Warning,
                    $"GPS SATELLITES: {telemetry.Satellites} < {thresholds.Satellites.Normal:0}"));
            if (gates[AlertKind.Hdop].Active)
                reasons.Add(Reason(AlertKind.Hdop, Severity.Warning,
                    string.Format(CultureInfo.InvariantCulture, "HDOP: {0:0.00} > {1:0.00}",
                        telemetry.Hdop, thresholds.Hdop.Normal)));

            var severity = reasons.Count == 0 ? Severity.Ok : reasons.Max(x => x.Severity);
            var restored = hadAlertWhileArmed && reasons.Count == 0;
            hadAlertWhileArmed = reasons.Count > 0;
            return Snapshot(severity, reasons, restored);
        }

        private static void Debounce(Gate gate, bool candidate, DateTime nowUtc)
        {
            if (candidate != gate.Candidate)
            {
                gate.Candidate = candidate;
                gate.ChangedUtc = nowUtc;
            }
            var delay = candidate ? ActivationDebounceSeconds : RecoveryDebounceSeconds;
            if (gate.Active != candidate && (nowUtc - gate.ChangedUtc).TotalSeconds >= delay)
                gate.Active = candidate;
        }

        public void Reset()
        {
            foreach (var gate in gates.Values)
            {
                gate.Active = false;
                gate.Candidate = false;
                gate.ChangedUtc = default;
            }
            armedSinceUtc = default;
            hadAlertWhileArmed = false;
        }

        private static AlertReason Reason(AlertKind kind, Severity severity, string text) =>
            new AlertReason { Kind = kind, Severity = severity, Text = text };
        private static SafetySnapshot Snapshot(Severity severity, IReadOnlyList<AlertReason> reasons, bool restored) =>
            new SafetySnapshot { Severity = severity, Reasons = reasons, Restored = restored };
    }
}
