using System;

namespace SarmatAltitude.Core
{
    public sealed class AltitudeTarget
    {
        public double? TargetMeters { get; private set; }
        public bool Pending { get; private set; }
        public void Adjust(double stepMeters, int direction)
        {
            if (double.IsNaN(stepMeters) || double.IsInfinity(stepMeters) || stepMeters <= 0)
                throw new ArgumentOutOfRangeException();
            TargetMeters = Math.Max(0, (TargetMeters ?? 0) + stepMeters * direction);
            Pending = true;
        }
        public void Submitted() { Pending = false; }
        public void Interrupted() { Pending = TargetMeters.HasValue; }
        public void Reset() { TargetMeters = null; Pending = false; }
        public static bool StickMoved(double initial, double current, double deadzone)
        {
            return current >= 800 && current <= 2200 && Math.Abs(current - initial) > deadzone;
        }
    }
}
