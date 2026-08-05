using System;
using System.Collections.Generic;
using System.Linq;

namespace SarmatVisionHold.Replay.Synchronization
{
    public sealed class TimedScalar
    {
        public double TimeSeconds;
        public double Value;
        public TimedScalar() { }
        public TimedScalar(double time, double value) { TimeSeconds = time; Value = value; }
    }
    public sealed class SyncPeak { public double OffsetSeconds, Correlation; }
    public sealed class ClockAlignmentResult
    {
        public double OffsetSeconds, Correlation, Confidence;
        public bool Applied, Automatic;
        public string Reason;
        public readonly List<SyncPeak> Alternatives = new List<SyncPeak>();
    }
    public sealed class ReplayTimeline
    {
        public double VideoOffsetSeconds { get; }
        public ReplayTimeline(double videoOffsetSeconds) { if (!Finite(videoOffsetSeconds)) throw new ArgumentException("Offset must be finite."); VideoOffsetSeconds = videoOffsetSeconds; }
        // A video timestamp maps to the tlog receive-time axis by adding this offset.
        public double TelemetryTime(double videoTimestampSeconds) => videoTimestampSeconds + VideoOffsetSeconds;
        public double ReplayTime(double videoTimestampSeconds, double replayStartSeconds) => videoTimestampSeconds - replayStartSeconds;
        static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }

    public sealed class ClockAlignmentEstimator
    {
        public ClockAlignmentResult Estimate(IList<TimedScalar> videoRotationRate, IList<TimedScalar> telemetryAngularRate,
            double minimumOffsetSeconds = -10, double maximumOffsetSeconds = 10, double stepSeconds = .01, double confidenceThreshold = .35)
        {
            if (videoRotationRate == null || telemetryAngularRate == null || videoRotationRate.Count < 8 || telemetryAngularRate.Count < 8)
                return new ClockAlignmentResult { Reason = "insufficient_samples", Automatic = true };
            if (!Finite(minimumOffsetSeconds) || !Finite(maximumOffsetSeconds) || !Finite(stepSeconds) || stepSeconds <= 0 || minimumOffsetSeconds > maximumOffsetSeconds)
                throw new ArgumentException("Invalid auto-sync search range.");
            var peaks = new List<SyncPeak>();
            for (var offset = minimumOffsetSeconds; offset <= maximumOffsetSeconds + stepSeconds / 2; offset += stepSeconds)
            {
                var pairsA = new List<double>(); var pairsB = new List<double>();
                foreach (var image in videoRotationRate)
                {
                    double telemetry; if (!TryInterpolate(telemetryAngularRate, image.TimeSeconds + offset, out telemetry)) continue;
                    if (!Finite(image.Value) || !Finite(telemetry)) continue; pairsA.Add(image.Value); pairsB.Add(telemetry);
                }
                if (pairsA.Count >= 8) peaks.Add(new SyncPeak { OffsetSeconds = offset, Correlation = Correlation(pairsA, pairsB) });
            }
            if (peaks.Count == 0) return new ClockAlignmentResult { Reason = "no_overlap", Automatic = true };
            var ordered = peaks.OrderByDescending(x => System.Math.Abs(x.Correlation)).ToList(); var best = ordered[0];
            var separated = ordered.Where(x => System.Math.Abs(x.OffsetSeconds - best.OffsetSeconds) >= System.Math.Max(.1, stepSeconds * 3)).Take(5).ToList();
            var second = separated.Count == 0 ? 0 : System.Math.Abs(separated[0].Correlation);
            var uniqueness = Clamp((System.Math.Abs(best.Correlation) - second) / .12, 0, 1);
            var confidence = System.Math.Abs(best.Correlation) * (.35 + .65 * uniqueness);
            var result = new ClockAlignmentResult { OffsetSeconds = best.OffsetSeconds, Correlation = best.Correlation, Confidence = confidence, Applied = confidence >= confidenceThreshold, Automatic = true, Reason = confidence >= confidenceThreshold ? "automatic" : "low_confidence" };
            result.Alternatives.AddRange(ordered.Skip(1).Where(x => System.Math.Abs(x.OffsetSeconds - best.OffsetSeconds) >= stepSeconds * 2).Take(5));
            return result;
        }

        static bool TryInterpolate(IList<TimedScalar> values, double time, out double value)
        {
            value = 0; if (values.Count == 0 || time < values[0].TimeSeconds || time > values[values.Count - 1].TimeSeconds) return false;
            var lo = 0; var hi = values.Count - 1;
            while (lo <= hi) { var mid = lo + (hi - lo) / 2; if (values[mid].TimeSeconds < time) lo = mid + 1; else hi = mid - 1; }
            var upper = System.Math.Min(values.Count - 1, lo); var lower = System.Math.Max(0, upper - 1); var a = values[lower]; var b = values[upper];
            if (b.TimeSeconds - a.TimeSeconds > .25) return false;
            var t = System.Math.Abs(b.TimeSeconds - a.TimeSeconds) < 1e-12 ? 0 : (time - a.TimeSeconds) / (b.TimeSeconds - a.TimeSeconds);
            value = a.Value + (b.Value - a.Value) * t; return Finite(value);
        }
        static double Correlation(IList<double> a, IList<double> b)
        {
            var ma = a.Average(); var mb = b.Average(); double ab = 0, aa = 0, bb = 0;
            for (var i = 0; i < a.Count; i++) { var da = a[i] - ma; var db = b[i] - mb; ab += da * db; aa += da * da; bb += db * db; }
            var denominator = System.Math.Sqrt(aa * bb); return denominator > 1e-12 ? ab / denominator : 0;
        }
        static double Clamp(double v, double a, double b) => System.Math.Max(a, System.Math.Min(b, v));
        static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
