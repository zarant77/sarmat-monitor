using System.Collections.Generic;

namespace SarmatPlugin.Core
{
    public sealed class PerFlightAlertTracker
    {
        private readonly HashSet<AlertKind> announced = new HashSet<AlertKind>();

        public IReadOnlyList<AlertReason> SelectNew(IReadOnlyList<AlertReason> reasons, bool armed)
        {
            if (!armed)
            {
                announced.Clear();
                return new AlertReason[0];
            }
            var result = new List<AlertReason>();
            if (reasons == null) return result;
            foreach (var reason in reasons)
                if (announced.Add(reason.Kind)) result.Add(reason);
            return result;
        }

        public void Reset() { announced.Clear(); }
    }
}
