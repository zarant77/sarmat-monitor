using System;

namespace SarmatPlugin.Core
{
    internal sealed class BatteryDialogConnection
    {
        private DateTime? connectedSince, disconnectedSince;
        private bool offered;
        public bool Disconnected { get; private set; }
        public bool Update(bool connected, DateTime now)
        {
            if (!connected)
            {
                connectedSince = null;
                if (!disconnectedSince.HasValue) disconnectedSince = now;
                Disconnected = (now - disconnectedSince.Value).TotalSeconds >= 3;
                if (Disconnected) offered = false;
                return false;
            }
            disconnectedSince = null; Disconnected = false;
            if (!connectedSince.HasValue) connectedSince = now;
            if (offered || (now - connectedSince.Value).TotalSeconds < 2) return false;
            offered = true;
            return true;
        }
        public void MarkOffered() => offered = true;
        public void Reset() { connectedSince = null; disconnectedSince = null; offered = false; Disconnected = false; }
    }
}
