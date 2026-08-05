using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SarmatVisionHold.Replay.Synchronization;
using SarmatVisionHold.Replay.Telemetry;
using SarmatVisionHold.ReplayAnalyzer.Input;

namespace SarmatVisionHold.ReplayAnalyzer.Output
{
    public sealed class ReplayReportSample
    {
        public double Time, SyncErrorMs, RawMagnitude, CompensatedMagnitude, Residual, ProcessingMs, GyroMagnitude, YawRate;
        public byte Quality;
        public string State, Reason;
        public bool Publishable;
    }
    public sealed class ReplayReportAccumulator
    {
        public readonly List<ReplayReportSample> Samples = new List<ReplayReportSample>();
        public readonly Dictionary<string, int> RejectReasons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> UsedAttitudeSources = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> UsedGyroSources = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> UsedAltitudeSources = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public void Add(ReplayRecord record)
        {
            var c = record.Vision.Compensation; var gyro = record.Telemetry?.BodyRateRadPerSecond;
            Samples.Add(new ReplayReportSample { Time = record.ReplayTimeSeconds, SyncErrorMs = record.SyncErrorSeconds * 1000, RawMagnitude = Hypot(record.Vision.Raw.TranslationX, record.Vision.Raw.TranslationY), CompensatedMagnitude = Hypot(c.CompensatedFlowX, c.CompensatedFlowY), Residual = c.ResidualPixels, ProcessingMs = record.ProcessingMilliseconds, GyroMagnitude = gyro?.Length ?? 0, YawRate = gyro?.Z ?? 0, Quality = record.Vision.MavlinkQuality, State = record.Vision.State.ToString(), Reason = record.Vision.Reason, Publishable = record.OpticalFlowRad?.Publishable == true });
            Count(UsedAttitudeSources, record.Telemetry?.AttitudeSource); Count(UsedGyroSources, record.Telemetry?.GyroSource); Count(UsedAltitudeSources, record.Telemetry?.AltitudeSource);
            if (record.OpticalFlowRad?.Publishable != true) { var reason = record.OpticalFlowRad?.RejectReason ?? "unknown"; int count; RejectReasons.TryGetValue(reason, out count); RejectReasons[reason] = count + 1; }
        }
        static void Count(Dictionary<string, int> values, string source) { if (string.IsNullOrWhiteSpace(source)) return; int count; values.TryGetValue(source, out count); values[source] = count + 1; }
        static double Hypot(double x, double y) => System.Math.Sqrt(x * x + y * y);
    }

    public static class ReplayReportWriter
    {
        public static void Write(string path, ReplayOptions options, VideoReplayMetadata video, ReplayTelemetryArchive telemetry, ClockAlignmentResult sync, ReplayReportAccumulator data)
        {
            var rows = data.Samples; var count = System.Math.Max(1, rows.Count);
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                w.WriteLine("# Sarmat Vision Hold replay analysis\n");
                w.WriteLine("**Diagnostics only. No MAVLink data was transmitted.**\n");
                w.WriteLine("## Inputs and timeline\n");
                w.WriteLine($"- Video: `{options.Video}` ({video.Width}x{video.Height}, nominal {video.NominalFps:F3} FPS, {video.DecodedFrames} decoded frames)");
                w.WriteLine($"- TLOG: `{options.Tlog}` ({telemetry.Bytes} bytes, {telemetry.DurationSeconds:F3} s, {telemetry.BadPackets} rejected packets)");
                w.WriteLine($"- Replay duration: {(rows.Count == 0 ? 0 : rows.Last().Time - rows.First().Time):F3} s");
                w.WriteLine($"- Selected offset: {sync.OffsetSeconds * 1000:F3} ms ({(sync.Automatic ? "automatic" : "manual")})");
                w.WriteLine($"- Correlation/confidence: {sync.Correlation:F4}/{sync.Confidence:F4}; applied={sync.Applied}; reason={sync.Reason}");
                w.WriteLine($"- Mean/max synchronization error: {Mean(rows.Select(x => x.SyncErrorMs)):F3}/{Max(rows.Select(x => x.SyncErrorMs)):F3} ms\n");
                w.WriteLine($"- Frames without finite synchronized attitude+gyro age: {rows.Count(x => !Finite(x.SyncErrorMs))}\n");
                w.WriteLine("### Alternative synchronization peaks\n");
                w.WriteLine("| Offset ms | Correlation |"); w.WriteLine("|---:|---:|"); foreach (var peak in sync.Alternatives) w.WriteLine($"| {peak.OffsetSeconds * 1000:F3} | {peak.Correlation:F4} |");
                w.WriteLine("\n## Telemetry sources\n");
                w.WriteLine("| Message | Count |"); w.WriteLine("|---|---:|"); foreach (var pair in telemetry.MessageCounts.OrderByDescending(x => x.Value)) w.WriteLine($"| {pair.Key} | {pair.Value} |");
                w.WriteLine($"\n- Attitude sources actually used: {SourceSummary(data.UsedAttitudeSources)}");
                w.WriteLine($"- Gyro sources actually used: {SourceSummary(data.UsedGyroSources)}");
                w.WriteLine($"- Altitude sources actually used: {SourceSummary(data.UsedAltitudeSources)}");
                w.WriteLine($"- Armed HEARTBEAT samples: {telemetry.States.Count(x => x.Armed)}; observed custom modes: {string.Join(", ", telemetry.States.Select(x => x.CustomMode).Distinct())}\n");
                w.WriteLine("## Timing and decoder health\n");
                w.WriteLine($"- Duplicate frames: {video.DuplicateFrames}"); w.WriteLine($"- Gapped frames: {video.GapFrames}"); w.WriteLine($"- Timestamp rollbacks: {video.TimestampRollbacks}"); w.WriteLine($"- Decoder errors: {video.DecoderErrors}");
                w.WriteLine($"- Processing FPS: {(Mean(rows.Select(x => x.ProcessingMs)) > 0 ? 1000 / Mean(rows.Select(x => x.ProcessingMs)) : 0):F2}");
                w.WriteLine($"- Processing p50/p95: {Percentile(rows.Select(x => x.ProcessingMs), .5):F3}/{Percentile(rows.Select(x => x.ProcessingMs), .95):F3} ms\n");
                w.WriteLine("## Tracking and compensation\n");
                w.WriteLine($"- Mean raw/compensated flow: {Mean(rows.Select(x => x.RawMagnitude)):F4}/{Mean(rows.Select(x => x.CompensatedMagnitude)):F4} px/frame");
                w.WriteLine($"- Mean/p95 compensation residual: {Mean(rows.Select(x => x.Residual)):F4}/{Percentile(rows.Select(x => x.Residual), .95):F4} px");
                w.WriteLine($"- Mean MAVLink-compatible quality: {Mean(rows.Select(x => (double)x.Quality)):F2}/255");
                foreach (var state in new[] { "OK", "DEGRADED", "LOST" }) w.WriteLine($"- {state}: {rows.Count(x => x.State == state) * 100d / count:F2}%");
                w.WriteLine($"- Publishable diagnostics samples: {rows.Count(x => x.Publishable)}/{rows.Count}\n");
                w.WriteLine("### Rejected sample reasons\n"); foreach (var pair in data.RejectReasons.OrderByDescending(x => x.Value)) w.WriteLine($"- {pair.Key}: {pair.Value}");
                Segment(w, "Hover candidates", rows.Where(x => x.GyroMagnitude < .05 && x.RawMagnitude < 5).ToList());
                Segment(w, "Yaw/rotation candidates", rows.Where(x => System.Math.Abs(x.YawRate) > .2).ToList());
                Segment(w, "Translational candidates", rows.Where(x => x.CompensatedMagnitude > 1 && System.Math.Abs(x.YawRate) < .1).ToList());
                w.WriteLine("\n## Automatic validation\n");
                var yaw = rows.Where(x => System.Math.Abs(x.YawRate) > .2).ToList(); var hover = rows.Where(x => x.GyroMagnitude < .05).ToList();
                Check(w, "Yaw compensation reduces apparent translation", yaw.Count >= 5 && Mean(yaw.Select(x => x.CompensatedMagnitude)) < Mean(yaw.Select(x => x.RawMagnitude)) * .7, yaw.Count < 5 ? "insufficient yaw segment" : null);
                Check(w, "Hover compensation does not increase flow", hover.Count >= 5 && Mean(hover.Select(x => x.CompensatedMagnitude)) <= Mean(hover.Select(x => x.RawMagnitude)) * 1.2, hover.Count < 5 ? "insufficient hover segment" : null);
                Check(w, "Synchronization is trusted", !sync.Automatic || sync.Applied, sync.Reason);
                Check(w, "No confident samples on LOST", rows.All(x => x.State != "LOST" || !x.Publishable), null);
                w.WriteLine("\n## Largest anomalies\n");
                w.WriteLine("| Replay time | Sync ms | Residual px | Processing ms | State/reason |"); w.WriteLine("|---:|---:|---:|---:|---|");
                foreach (var row in rows.OrderByDescending(x => x.SyncErrorMs + x.Residual * 10 + x.ProcessingMs / 10).Take(15)) w.WriteLine($"| {row.Time:F3} | {row.SyncErrorMs:F2} | {row.Residual:F2} | {row.ProcessingMs:F2} | {row.State}/{row.Reason} |");
                w.WriteLine("\n## Flight-readiness limitation\n");
                w.WriteLine("This replay validates offline timing, coordinate signs, IMU compensation and OPTICAL_FLOW_RAD field construction. It does not validate a real lens calibration, vibration, rolling shutter, transport latency, EKF fusion, FlowHold, or any live autopilot connection.");
            }
        }

        static void Segment(StreamWriter w, string title, IList<ReplayReportSample> rows)
        {
            w.WriteLine($"\n### {title}\n"); if (rows.Count == 0) { w.WriteLine("No candidate segment was found."); return; }
            w.WriteLine($"- Samples: {rows.Count}"); w.WriteLine($"- Raw/compensated magnitude: {Mean(rows.Select(x => x.RawMagnitude)):F4}/{Mean(rows.Select(x => x.CompensatedMagnitude)):F4} px/frame"); w.WriteLine($"- Residual: {Mean(rows.Select(x => x.Residual)):F4} px"); w.WriteLine($"- Mean quality: {Mean(rows.Select(x => (double)x.Quality)):F2}/255");
        }
        static void Check(StreamWriter w, string name, bool pass, string note) => w.WriteLine($"- {(pass ? "PASS" : "WARN")}: {name}{(string.IsNullOrWhiteSpace(note) ? "" : " — " + note)}");
        static string SourceSummary(Dictionary<string, int> values) => values.Count == 0 ? "none" : string.Join(", ", values.OrderByDescending(x => x.Value).Select(x => x.Key + "=" + x.Value));
        static double Mean(IEnumerable<double> values) { var a = values.Where(Finite).ToArray(); return a.Length == 0 ? 0 : a.Average(); }
        static double Max(IEnumerable<double> values) { var a = values.Where(Finite).ToArray(); return a.Length == 0 ? 0 : a.Max(); }
        static double Percentile(IEnumerable<double> values, double p) { var a = values.Where(Finite).OrderBy(x => x).ToArray(); if (a.Length == 0) return 0; var at = (a.Length - 1) * p; var lo = (int)at; var hi = (int)System.Math.Ceiling(at); return a[lo] + (a[hi] - a[lo]) * (at - lo); }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
