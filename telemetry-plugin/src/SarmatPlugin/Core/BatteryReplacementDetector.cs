using System;

namespace SarmatPlugin.Core
{
    // Use fresh packets, not Mission Planner's cached connection/voltage state.
    internal sealed class BatteryReplacementDetector
    {
        private DateTime? heartbeatAt, riseSince;
        private bool armed;
        private double? idleVoltage;
        private int risingSamples;
        public bool SuspectedReplacement => riseSince.HasValue;

        public bool Heartbeat(bool currentArmed, DateTime now)
        {
            var reconnected = heartbeatAt.HasValue && (now - heartbeatAt.Value).TotalSeconds > 5 && !currentArmed;
            heartbeatAt = now; armed = currentArmed;
            if (currentArmed) { riseSince = null; risingSamples = 0; }
            return reconnected;
        }

        public bool Voltage(double voltage, double? currentAmps, DateTime now)
        {
            if (!heartbeatAt.HasValue || (now - heartbeatAt.Value).TotalSeconds > 5 || armed ||
                double.IsNaN(voltage) || double.IsInfinity(voltage) || voltage <= 0 || voltage > 1000 ||
                !currentAmps.HasValue || double.IsNaN(currentAmps.Value) || double.IsInfinity(currentAmps.Value) ||
                currentAmps.Value < 0 || currentAmps.Value > 5)
            { riseSince = null; risingSamples = 0; return false; }
            if (!idleVoltage.HasValue) { idleVoltage = voltage; return false; }
            if (voltage - idleVoltage.Value < 3)
            { idleVoltage = voltage; riseSince = null; risingSamples = 0; return false; }
            if (!riseSince.HasValue) { riseSince = now; risingSamples = 0; }
            risingSamples++;
            if (risingSamples < 2 || (now - riseSince.Value).TotalSeconds < 2) return false;
            idleVoltage = voltage; riseSince = null; risingSamples = 0;
            return true;
        }

        public void Reset() { heartbeatAt = null; riseSince = null; idleVoltage = null; armed = false; risingSamples = 0; }
    }
}
