using System;
using System.Collections.Generic;
using System.Globalization;

namespace SarmatPlugin.Core
{
    // Called under the runtime's lock. Only fresh MAVLink packets drive armed edges and samples.
    internal sealed class BatteryVoltageTracker
    {
        public string SessionId { get; private set; }
        private bool confirmed;
        public bool IsConfirmed => confirmed;
        private bool? armed;
        private DateTime heartbeatAt;
        private DateTime sampleAt;
        private DateTime publishedAt;
        private double? previousCurrent;
        private double consumedMah;
        private bool consumptionComplete = true;
        private readonly List<KeyValuePair<string, DateTime>> pending = new List<KeyValuePair<string, DateTime>>();
        private readonly List<Dictionary<string, object>> captured = new List<Dictionary<string, object>>();

        public void Connect(DateTime now, bool recordConnection = true)
        {
            Reset(); SessionId = Guid.NewGuid().ToString();
            if (recordConnection) pending.Add(new KeyValuePair<string, DateTime>("vehicle_connected", now));
        }
        public void Reset()
        {
            SessionId = null; confirmed = false; armed = null; heartbeatAt = default;
            pending.Clear(); captured.Clear();
            sampleAt = default; publishedAt = default; previousCurrent = null;
            consumedMah = 0; consumptionComplete = true;
        }
        public void Confirm(string sessionId) { if (SessionId == sessionId) confirmed = true; }
        public void Heartbeat(bool currentArmed, DateTime now)
        {
            if (SessionId == null) return;
            // A heartbeat gap breaks edge continuity: reconnecting disarmed is not a landing.
            if (armed == true && !currentArmed && (now - heartbeatAt).TotalSeconds <= 5)
                pending.Add(new KeyValuePair<string, DateTime>("vehicle_disarmed", now));
            armed = currentArmed; heartbeatAt = now;
        }
        public void Voltage(double voltage, DateTime now, double? currentAmps = null)
        {
            if (SessionId == null || !armed.HasValue || (now - heartbeatAt).TotalSeconds > 5 ||
                double.IsNaN(voltage) || double.IsInfinity(voltage) || voltage <= 0 || voltage > 1000) return;
            if (sampleAt != default && now <= sampleAt) return;
            var validCurrent = currentAmps.HasValue && !double.IsNaN(currentAmps.Value) &&
                !double.IsInfinity(currentAmps.Value) && currentAmps.Value >= 0 && currentAmps.Value <= 10000;
            if (sampleAt != default)
            {
                var seconds = (now - sampleAt).TotalSeconds;
                if (validCurrent && previousCurrent.HasValue && seconds <= 5)
                    consumedMah += (previousCurrent.Value + currentAmps.Value) / 2 * seconds / 3.6;
                else consumptionComplete = false; // Never integrate across missing telemetry.
            }
            if (!validCurrent) consumptionComplete = false;
            sampleAt = now; previousCurrent = validCurrent ? currentAmps : null;
            // Persist cumulative checkpoints while armed. Missing deliveries are recovered by a later total.
            if (armed == true && publishedAt != default && (now - publishedAt).TotalSeconds >= 15)
                pending.Add(new KeyValuePair<string, DateTime>("consumption_sample", now));
            if (publishedAt == default && pending.Count == 0)
                pending.Add(new KeyValuePair<string, DateTime>("consumption_sample", now));
            foreach (var trigger in pending)
            {
                if (now < trigger.Value || (now - trigger.Value).TotalSeconds > 30) continue;
                captured.Add(new Dictionary<string, object> {
                    ["id"] = Guid.NewGuid().ToString(), ["sessionId"] = SessionId, ["type"] = trigger.Key,
                    ["totalVoltage"] = Math.Round(voltage, 3),
                    ["consumedMah"] = Math.Round(consumedMah, 3),
                    ["consumptionComplete"] = consumptionComplete,
                    ["armed"] = armed.Value,
                    ["currentAmps"] = currentAmps.HasValue && !double.IsNaN(currentAmps.Value) &&
                        !double.IsInfinity(currentAmps.Value) && currentAmps.Value >= 0 && currentAmps.Value <= 10000
                        ? (object)Math.Round(currentAmps.Value, 3) : null,
                    ["occurredAt"] = trigger.Value.ToString("o", CultureInfo.InvariantCulture),
                    ["measuredAt"] = now.ToString("o", CultureInfo.InvariantCulture)
                });
                publishedAt = now;
            }
            pending.Clear();
        }
        public Dictionary<string, object>[] Ready() => confirmed ? captured.ToArray() : Array.Empty<Dictionary<string, object>>();
        public void Stored(Dictionary<string, object> item) => captured.Remove(item);
    }
}
