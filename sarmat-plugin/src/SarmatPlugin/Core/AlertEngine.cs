using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SarmatPlugin.Core
{
    public sealed class AlertEngine
    {
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
            if (!telemetry.Armed || !settings.AlertsEnabled)
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
            if ((nowUtc - armedSinceUtc).TotalSeconds < settings.ArmedGracePeriodSeconds)
                return Snapshot(Severity.Ok, new List<AlertReason>(), false);

            var conditions = new Dictionary<AlertKind, bool>
            {
                [AlertKind.Obs] = !obs.Connected || obs.Recording != true,
                [AlertKind.Satellites] = Hysteresis(AlertKind.Satellites,
                    telemetry.Satellites < settings.MinimumSatellites,
                    telemetry.Satellites >= settings.MinimumSatellites + 1),
                [AlertKind.Hdop] = Hysteresis(AlertKind.Hdop,
                    telemetry.Hdop > settings.MaximumHdop,
                    telemetry.Hdop < settings.MaximumHdop - 0.05),
                [AlertKind.Battery] = Hysteresis(AlertKind.Battery,
                    telemetry.BatteryVoltage > 0 && telemetry.BatteryVoltage < settings.MinimumBatteryVoltage,
                    telemetry.BatteryVoltage >= settings.MinimumBatteryVoltage + 0.5),
                [AlertKind.Ruijie] = !ruijie.Connected || ruijie.Stale
            };

            foreach (var pair in conditions)
                Debounce(gates[pair.Key], pair.Value, settings, nowUtc);

            var reasons = new List<AlertReason>();
            if (gates[AlertKind.Ruijie].Active)
                reasons.Add(Reason(AlertKind.Ruijie, Severity.Critical, ruijie.Stale ? "RUIJIE STALE" : "RUIJIE DISCONNECTED"));
            if (gates[AlertKind.Battery].Active)
                reasons.Add(Reason(AlertKind.Battery, Severity.Critical,
                    string.Format(CultureInfo.InvariantCulture, "BATTERY: {0:0.0} V < {1:0.0} V", telemetry.BatteryVoltage, settings.MinimumBatteryVoltage)));
            if (gates[AlertKind.Obs].Active)
                reasons.Add(Reason(AlertKind.Obs, Severity.Warning,
                    obs.Connected ? "OBS NOT RECORDING" : "OBS DISCONNECTED"));
            if (gates[AlertKind.Satellites].Active)
                reasons.Add(Reason(AlertKind.Satellites, Severity.Warning,
                    $"GPS SATELLITES: {telemetry.Satellites} < {settings.MinimumSatellites}"));
            if (gates[AlertKind.Hdop].Active)
                reasons.Add(Reason(AlertKind.Hdop, Severity.Warning,
                    string.Format(CultureInfo.InvariantCulture, "HDOP: {0:0.00} > {1:0.00}", telemetry.Hdop, settings.MaximumHdop)));

            var severity = reasons.Count == 0 ? Severity.Ok : reasons.Max(x => x.Severity);
            var restored = hadAlertWhileArmed && reasons.Count == 0;
            hadAlertWhileArmed = reasons.Count > 0;
            return Snapshot(severity, reasons, restored);
        }

        private bool Hysteresis(AlertKind kind, bool activate, bool clear)
        {
            return gates[kind].Active ? !clear : activate;
        }

        private static void Debounce(Gate gate, bool candidate, PluginSettings settings, DateTime nowUtc)
        {
            if (candidate != gate.Candidate)
            {
                gate.Candidate = candidate;
                gate.ChangedUtc = nowUtc;
            }
            var delay = candidate ? settings.ActivationDebounceSeconds : settings.RecoveryDebounceSeconds;
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
