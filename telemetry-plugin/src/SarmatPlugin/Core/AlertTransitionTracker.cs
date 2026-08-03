using System.Collections.Generic;
using System.Linq;

namespace SarmatPlugin.Core
{
    public sealed class AlertTransitionTracker
    {
        private HashSet<AlertKind> active = new HashSet<AlertKind>();

        public IReadOnlyList<AlertReason> SelectNew(IReadOnlyList<AlertReason> reasons, bool armed)
        {
            if (!armed)
            {
                active.Clear();
                return new AlertReason[0];
            }
            var current = new HashSet<AlertKind>((reasons ?? new AlertReason[0]).Select(x => x.Kind));
            var result = (reasons ?? new AlertReason[0]).Where(x => !active.Contains(x.Kind)).ToList();
            active = current;
            return result;
        }

        public void Reset() { active.Clear(); }
    }
}
