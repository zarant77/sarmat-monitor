using System;
using System.Collections.Generic;
using System.Globalization;

namespace SarmatPlugin.Core
{
    // Converts fresh MAVLink ARMED edges into durable, idempotent flight events.
    internal sealed class FlightTracker
    {
        private string sessionId;
        private string flightId;
        private bool? armed;
        private DateTime heartbeatAt;
        private readonly List<Dictionary<string, object>> captured = new List<Dictionary<string, object>>();

        public void Confirm(string value) { sessionId = value; }
        public void Reset() { sessionId = null; flightId = null; armed = null; heartbeatAt = default; captured.Clear(); }
        public void Heartbeat(bool currentArmed, DateTime now)
        {
            var continuous = armed.HasValue && (now - heartbeatAt).TotalSeconds <= 5;
            if (sessionId != null && continuous && armed == false && currentArmed)
            {
                flightId = Guid.NewGuid().ToString();
                Capture("armed", flightId, now);
            }
            else if (sessionId != null && continuous && armed == true && !currentArmed && flightId != null)
            {
                Capture("disarmed", flightId, now);
                flightId = null;
            }
            else if (!continuous && !currentArmed) flightId = null;
            armed = currentArmed; heartbeatAt = now;
        }
        private void Capture(string type, string currentFlightId, DateTime occurredAt)
        {
            captured.Add(new Dictionary<string, object> {
                ["id"] = Guid.NewGuid().ToString(), ["flightId"] = currentFlightId, ["sessionId"] = sessionId,
                ["type"] = type, ["occurredAt"] = occurredAt.ToString("o", CultureInfo.InvariantCulture)
            });
        }
        public Dictionary<string, object>[] Ready() => captured.ToArray();
        public void Stored(Dictionary<string, object> item) => captured.Remove(item);
    }
}
