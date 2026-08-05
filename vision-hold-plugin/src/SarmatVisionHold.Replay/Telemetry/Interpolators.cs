using System;
using System.Collections.Generic;
using System.Linq;
using SarmatVisionHold.Replay.Math;

namespace SarmatVisionHold.Replay.Telemetry
{
    static class InterpolationSearch
    {
        public static bool Bracket<T>(IList<T> values, double time, Func<T, double> selector, out T before, out T after)
        {
            before = default(T); after = default(T); if (values == null || values.Count == 0) return false;
            var lo = 0; var hi = values.Count - 1;
            while (lo <= hi) { var mid = lo + (hi - lo) / 2; if (selector(values[mid]) < time) lo = mid + 1; else hi = mid - 1; }
            var upper = System.Math.Min(values.Count - 1, lo); var lower = System.Math.Max(0, upper - 1);
            if (selector(values[upper]) < time) lower = upper;
            before = values[lower]; after = values[upper]; return true;
        }
        public static double Age(double time, double a, double b) => System.Math.Min(System.Math.Abs(time - a), System.Math.Abs(time - b));
        public static double Factor(double time, double a, double b) => System.Math.Abs(b - a) < 1e-12 ? 0 : System.Math.Max(0, System.Math.Min(1, (time - a) / (b - a)));
    }

    public sealed class AttitudeInterpolator
    {
        readonly List<TimedAttitude> quaternion;
        readonly List<TimedAttitude> euler;
        readonly double staleSeconds;
        public AttitudeInterpolator(IEnumerable<TimedAttitude> values, double staleSeconds = .25)
        {
            var all = (values ?? Enumerable.Empty<TimedAttitude>()).OrderBy(x => x.TimeSeconds).ToList();
            quaternion = all.Where(x => x.IsQuaternion).ToList(); euler = all.Where(x => !x.IsQuaternion).ToList(); this.staleSeconds = staleSeconds;
        }
        public bool TrySample(double time, out Quaterniond value, out double age, out double span, out string source)
        {
            value = Quaterniond.Identity; age = span = double.PositiveInfinity; source = null;
            if (TryList(quaternion, time, "ATTITUDE_QUATERNION", out value, out age, out span, out source)) return true;
            return TryList(euler, time, "ATTITUDE", out value, out age, out span, out source);
        }
        bool TryList(List<TimedAttitude> selected, double time, string selectedSource, out Quaterniond value, out double age, out double span, out string source)
        {
            value = Quaterniond.Identity; age = span = double.PositiveInfinity; source = selectedSource; TimedAttitude a, b;
            if (!InterpolationSearch.Bracket(selected, time, x => x.TimeSeconds, out a, out b)) return false;
            source = string.Equals(a.Source, b.Source, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(a.Source) ? a.Source : selectedSource;
            age = InterpolationSearch.Age(time, a.TimeSeconds, b.TimeSeconds); span = System.Math.Abs(b.TimeSeconds - a.TimeSeconds);
            if (age > staleSeconds || span > staleSeconds * 4) return false;
            value = Quaterniond.Slerp(a.BodyToNed, b.BodyToNed, InterpolationSearch.Factor(time, a.TimeSeconds, b.TimeSeconds));
            return value.IsFinite;
        }
    }

    public sealed class GyroInterpolator
    {
        static readonly string[] Priority = { "HIGHRES_IMU", "SCALED_IMU", "SCALED_IMU2", "SCALED_IMU3", "ATTITUDE", "RAW_IMU" };
        readonly Dictionary<string, List<TimedGyro>> bySource;
        readonly double staleSeconds;
        public GyroInterpolator(IEnumerable<TimedGyro> values, double staleSeconds = .15)
        {
            bySource = (values ?? Enumerable.Empty<TimedGyro>()).GroupBy(x => x.Source ?? "UNKNOWN", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.TimeSeconds).ToList(), StringComparer.OrdinalIgnoreCase);
            this.staleSeconds = staleSeconds;
        }
        public bool TrySample(double time, out Vector3d value, out double age, out double span, out string source)
        {
            value = new Vector3d(); age = span = double.PositiveInfinity; source = null;
            foreach (var candidate in Priority)
            {
                List<TimedGyro> list; if (!bySource.TryGetValue(candidate, out list)) continue; TimedGyro a, b;
                if (!InterpolationSearch.Bracket(list, time, x => x.TimeSeconds, out a, out b)) continue;
                var localAge = InterpolationSearch.Age(time, a.TimeSeconds, b.TimeSeconds); var localSpan = System.Math.Abs(b.TimeSeconds - a.TimeSeconds);
                if (localAge > staleSeconds || localSpan > staleSeconds * 4) continue;
                var t = InterpolationSearch.Factor(time, a.TimeSeconds, b.TimeSeconds);
                value = a.BodyRateRadPerSecond + (b.BodyRateRadPerSecond - a.BodyRateRadPerSecond) * t;
                age = localAge; span = localSpan; source = candidate; return value.IsFinite;
            }
            return false;
        }
    }

    public sealed class AltitudeInterpolator
    {
        static readonly string[] Priority = { "DISTANCE_SENSOR", "RANGEFINDER", "TERRAIN", "RELATIVE", "BARO_DELTA" };
        readonly Dictionary<string, List<TimedAltitude>> bySource;
        readonly double staleSeconds, minMeters, maxMeters;
        public AltitudeInterpolator(IEnumerable<TimedAltitude> values, double staleSeconds = .5, double minMeters = .05, double maxMeters = 200)
        {
            bySource = (values ?? Enumerable.Empty<TimedAltitude>()).Where(x => x.Valid).GroupBy(x => x.Source ?? "UNKNOWN", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.TimeSeconds).ToList(), StringComparer.OrdinalIgnoreCase);
            this.staleSeconds = staleSeconds; this.minMeters = minMeters; this.maxMeters = maxMeters;
        }
        public bool TrySample(double time, out double value, out double age, out double span, out string source)
        {
            value = 0; age = span = double.PositiveInfinity; source = null;
            foreach (var candidate in Priority)
            {
                List<TimedAltitude> list; if (!bySource.TryGetValue(candidate, out list)) continue; TimedAltitude a, b;
                if (!InterpolationSearch.Bracket(list, time, x => x.TimeSeconds, out a, out b)) continue;
                var localAge = InterpolationSearch.Age(time, a.TimeSeconds, b.TimeSeconds); var localSpan = System.Math.Abs(b.TimeSeconds - a.TimeSeconds);
                if (localAge > staleSeconds || localSpan > staleSeconds * 4) continue;
                var t = InterpolationSearch.Factor(time, a.TimeSeconds, b.TimeSeconds); var local = a.Meters + (b.Meters - a.Meters) * t;
                if (double.IsNaN(local) || double.IsInfinity(local) || local < minMeters || local > maxMeters) continue;
                value = local; age = localAge; span = localSpan; source = candidate; return true;
            }
            return false;
        }
    }

    public sealed class ReplayTelemetryInterpolator
    {
        readonly AttitudeInterpolator attitude; readonly GyroInterpolator gyro; readonly AltitudeInterpolator altitude;
        public ReplayTelemetryInterpolator(ReplayTelemetryArchive archive)
        {
            if (archive == null) throw new ArgumentNullException(nameof(archive));
            attitude = new AttitudeInterpolator(archive.Attitudes); gyro = new GyroInterpolator(archive.Gyros); altitude = new AltitudeInterpolator(archive.Altitudes);
        }
        public ReplayTelemetrySample Sample(double time)
        {
            var result = new ReplayTelemetrySample { RequestedTimeSeconds = time };
            result.AttitudeValid = attitude.TrySample(time, out result.BodyToNed, out result.AttitudeAgeSeconds, out result.AttitudeSpanSeconds, out result.AttitudeSource);
            result.EulerRad = result.BodyToNed.ToEuler();
            result.GyroValid = gyro.TrySample(time, out result.BodyRateRadPerSecond, out result.GyroAgeSeconds, out result.GyroSpanSeconds, out result.GyroSource);
            result.AltitudeValid = altitude.TrySample(time, out result.AltitudeMeters, out result.AltitudeAgeSeconds, out result.AltitudeSpanSeconds, out result.AltitudeSource);
            return result;
        }
    }
}
