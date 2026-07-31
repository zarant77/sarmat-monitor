using System;
using System.Text;

namespace SarmatPlugin.Core
{
    public sealed class TakeoffModeWarningTracker
    {
        private bool initialized;
        private bool wasArmed;
        private bool resolvedForFlight;

        public bool Update(bool armed, string mode)
        {
            if (!initialized)
            {
                initialized = true;
                wasArmed = armed;
                resolvedForFlight = armed && IsPostHold(mode);
                return armed && !resolvedForFlight;
            }
            if (!armed)
            {
                wasArmed = false;
                resolvedForFlight = false;
                return false;
            }
            if (!wasArmed)
            {
                wasArmed = true;
                resolvedForFlight = IsPostHold(mode);
                return !resolvedForFlight;
            }
            if (!resolvedForFlight && IsPostHold(mode))
                resolvedForFlight = true;
            return !resolvedForFlight;
        }

        public static bool IsPostHold(string mode)
        {
            var normalized = new StringBuilder();
            foreach (var c in mode ?? "")
                if (char.IsLetterOrDigit(c)) normalized.Append(char.ToLowerInvariant(c));
            var value = normalized.ToString();
            return value == "posthold" || value == "poshold";
        }
    }
}
