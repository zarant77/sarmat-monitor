using System;

namespace SarmatPlugin.Core
{
    public sealed class CameraConnectionGate
    {
        private DateTime? stableSince, lastPacket;
        private long? previousCount;
        public bool Update(bool connected, bool busy, long count, DateTime now)
        {
            if (!connected || busy || previousCount.HasValue && count < previousCount.Value)
            { Reset(); previousCount = count; return false; }
            if (previousCount.HasValue && count > previousCount.Value) lastPacket = now;
            previousCount = count;
            if (!lastPacket.HasValue || now - lastPacket.Value > TimeSpan.FromSeconds(2))
            { stableSince = null; return false; }
            if (!stableSince.HasValue) stableSince = now;
            return now - stableSince.Value >= TimeSpan.FromSeconds(3);
        }
        public void Reset() { stableSince = lastPacket = null; previousCount = null; }
    }
}
