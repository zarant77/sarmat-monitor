using System;

namespace SarmatPlugin.Core
{
    public sealed class MavlinkSilenceWatchdog
    {
        private long? lastPacketCount;
        private DateTime lastPacketUtc;

        public bool Update(bool connected, long? packetCount, DateTime nowUtc, double timeoutSeconds)
        {
            if (!connected || !packetCount.HasValue || timeoutSeconds <= 0)
            {
                Reset();
                return false;
            }

            if (!lastPacketCount.HasValue || packetCount.Value != lastPacketCount.Value)
            {
                lastPacketCount = packetCount.Value;
                lastPacketUtc = nowUtc;
                return false;
            }

            if ((nowUtc - lastPacketUtc).TotalSeconds < timeoutSeconds) return false;

            // Start a new timeout window immediately. This prevents a reconnect request on every
            // plugin tick while still allowing another attempt if MAVLink traffic does not resume.
            lastPacketUtc = nowUtc;
            return true;
        }

        public void Reset()
        {
            lastPacketCount = null;
            lastPacketUtc = default(DateTime);
        }
    }
}
