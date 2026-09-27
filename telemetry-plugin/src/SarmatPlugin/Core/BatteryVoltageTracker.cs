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
        public void Voltage(double voltage, DateTime now)
        {
            if (SessionId == null || !armed.HasValue || (now - heartbeatAt).TotalSeconds > 5 ||
                double.IsNaN(voltage) || double.IsInfinity(voltage) || voltage <= 0 || voltage > 1000) return;
            foreach (var trigger in pending)
            {
                if (now < trigger.Value || (now - trigger.Value).TotalSeconds > 30) continue;
                captured.Add(new Dictionary<string, object> {
                    ["id"] = Guid.NewGuid().ToString(), ["sessionId"] = SessionId, ["type"] = trigger.Key,
                    ["totalVoltage"] = Math.Round(voltage, 3),
                    ["occurredAt"] = trigger.Value.ToString("o", CultureInfo.InvariantCulture),
                    ["measuredAt"] = now.ToString("o", CultureInfo.InvariantCulture)
                });
            }
            pending.Clear();
        }
        public Dictionary<string, object>[] Ready() => confirmed ? captured.ToArray() : Array.Empty<Dictionary<string, object>>();
        public void Stored(Dictionary<string, object> item) => captured.Remove(item);
    }
}
