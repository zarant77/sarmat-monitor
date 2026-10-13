using System;

namespace SarmatAltitude.Core
{
    public sealed class AltitudeTarget
    {
        public double? TargetMeters { get; private set; }
        public double? TargetAzimuth { get; private set; }
        public bool Pending { get; private set; }
        public void Restore(double? altitude, double? azimuth)
        {
            if (altitude.HasValue && (double.IsNaN(altitude.Value) || double.IsInfinity(altitude.Value) || altitude.Value < 0) ||
                azimuth.HasValue && (double.IsNaN(azimuth.Value) || double.IsInfinity(azimuth.Value) || azimuth.Value < 0 || azimuth.Value >= 360 || azimuth.Value != Math.Floor(azimuth.Value)))
                throw new ArgumentOutOfRangeException();
            TargetMeters = altitude;
            TargetAzimuth = azimuth;
            Interrupted();
        }
        public void Adjust(double stepMeters, int direction)
        {
            if (double.IsNaN(stepMeters) || double.IsInfinity(stepMeters) || stepMeters <= 0)
                throw new ArgumentOutOfRangeException();
            TargetMeters = Math.Max(0, (TargetMeters ?? 0) + stepMeters * direction);
            Pending = true;
        }
        public static double Normalize(double angle) => (angle % 360 + 360) % 360;
        public static double AngularDelta(double current, double desired) => Normalize(desired - current + 180) - 180;
        public void EnsureAzimuth(double heading, double step = 5)
        {
            if (double.IsNaN(step) || double.IsInfinity(step) || step < 1 || step > 180 || step != Math.Floor(step))
                throw new ArgumentOutOfRangeException(nameof(step));
            if (!TargetAzimuth.HasValue)
            {
                if (double.IsNaN(heading) || double.IsInfinity(heading)) throw new InvalidOperationException("Live heading required");
                TargetAzimuth = Normalize(heading);
            }
            var rounded = Math.Round(TargetAzimuth.Value / step, MidpointRounding.AwayFromZero) * step;
            TargetAzimuth = rounded >= 360 ? 0 : rounded;
        }
        public void AdjustAzimuth(double step, int direction, double heading)
        {
            EnsureAzimuth(heading, step);
            var next = direction > 0
                ? (Math.Floor(TargetAzimuth.Value / step) + 1) * step
                : (Math.Ceiling(TargetAzimuth.Value / step) - 1) * step;
            TargetAzimuth = next >= 360 ? 0 : next < 0 ? Math.Floor(359 / step) * step : next;
            TargetMeters = TargetMeters ?? 0;
            Pending = true;
        }
        public bool AltitudeInRange(double current) => TargetMeters.HasValue && Math.Abs(current - TargetMeters.Value) <= 10;
        public bool AzimuthInRange(double current) => TargetAzimuth.HasValue && Math.Abs(AngularDelta(current, TargetAzimuth.Value)) <= 5;
        public void Submitted() { Pending = false; }
        public void Interrupted() { Pending = TargetMeters.HasValue; }
        public void Reset() { TargetMeters = null; TargetAzimuth = null; Pending = false; }
        public static bool StickMoved(double initial, double current, double deadzone)
        {
            return current >= 800 && current <= 2200 && Math.Abs(current - initial) > deadzone;
        }
    }
}
