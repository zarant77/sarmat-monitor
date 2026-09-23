using System;
using System.Linq;
using System.Text;

namespace SarmatPlugin.Core
{
    public sealed class TakeoffModeWarningTracker
    {
        private bool initialized;
        private bool wasArmed;
        private bool resolvedForFlight;

        public bool Update(bool armed, string mode, bool enabled, string safeModes)
        {
            if (!enabled)
            {
                initialized = true;
                wasArmed = armed;
                resolvedForFlight = armed;
                return false;
            }
            if (!initialized)
            {
                initialized = true;
                wasArmed = armed;
                resolvedForFlight = armed && IsSafeMode(mode, safeModes);
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
                resolvedForFlight = IsSafeMode(mode, safeModes);
                return !resolvedForFlight;
            }
            if (!resolvedForFlight && IsSafeMode(mode, safeModes))
                resolvedForFlight = true;
            return !resolvedForFlight;
        }

        public static bool IsSafeMode(string mode, string safeModes)
        {
            var actual = NormalizeMode(mode);
            return !string.IsNullOrEmpty(actual) && (safeModes ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeMode)
                .Any(expected => expected == actual);
        }

        private static string NormalizeMode(string mode)
        {
            var normalized = new StringBuilder();
            foreach (var c in mode ?? "")
                if (char.IsLetterOrDigit(c)) normalized.Append(char.ToLowerInvariant(c));
            return normalized.ToString();
        }
    }
}
