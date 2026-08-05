using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OpenCvSharp;
using SarmatVisionHold.Replay.Camera;
using SarmatVisionHold.Replay.Output;
using SarmatVisionHold.Replay.Processing;
using SarmatVisionHold.Replay.Synchronization;
using SarmatVisionHold.Replay.Telemetry;
using SarmatVisionHold.ReplayAnalyzer.Input;
using SarmatVisionHold.ReplayAnalyzer.Output;
using SarmatVisionHold.ReplayAnalyzer.Processing;
using SarmatVisionHold.ReplayAnalyzer.Synchronization;
using SarmatVisionHold.Vision;

namespace SarmatVisionHold.ReplayAnalyzer
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            try
            {
                var options = ReplayOptions.Parse(args); if (options.Help) { Usage(); return 0; } options.ValidateFiles();
                if (!ReplaySafety.DiagnosticsOnly) throw new InvalidOperationException("DiagnosticsOnly guard is disabled.");
                return Run(options);
            }
            catch (ArgumentException e) { Console.Error.WriteLine("ERROR: " + e.Message); Usage(); return 2; }
            catch (FileNotFoundException e) { Console.Error.WriteLine("ERROR: " + e.Message + ": " + e.FileName); return 3; }
            catch (Exception e) { Console.Error.WriteLine("FATAL: " + e); return 1; }
        }

        static int Run(ReplayOptions options)
        {
            Directory.CreateDirectory(options.Output); Directory.CreateDirectory(Path.Combine(options.Output, "snapshots"));
            Console.WriteLine("DiagnosticsOnly=true. No serial, UDP, Mission Planner, EKF, mode or RC connection will be opened.");
            Console.WriteLine("Reading TLOG..."); var archive = new TlogReplayReader().Read(options.Tlog);
            FilterAltitude(archive, options.AltitudeSource);
            if (archive.Attitudes.Count == 0 || archive.Gyros.Count == 0) throw new InvalidOperationException("TLOG has no usable attitude/gyro telemetry.");

            var manualOffset = options.VideoOffsetMilliseconds / 1000d;
            var sync = new ClockAlignmentResult { OffsetSeconds = manualOffset, Confidence = 1, Correlation = 0, Applied = true, Automatic = false, Reason = "manual" };
            if (options.AutoSync)
            {
                Console.WriteLine("Estimating video/TLOG clock offset...");
                var imageRotation = VideoRotationSeriesExtractor.Extract(options); var gyro = VideoRotationSeriesExtractor.TelemetryYawRate(archive);
                var automatic = new ClockAlignmentEstimator().Estimate(imageRotation, gyro, options.AutoSyncMinimumOffsetSeconds, options.AutoSyncMaximumOffsetSeconds, options.AutoSyncStepSeconds, options.AutoSyncConfidenceThreshold);
                if (automatic.Applied) sync = automatic; else { automatic.OffsetSeconds = manualOffset; sync = automatic; sync.Reason += "; manual_fallback"; }
            }
            WriteSynchronization(Path.Combine(options.Output, "synchronization.json"), sync);
            var timeline = new ReplayTimeline(sync.OffsetSeconds); var interpolator = new ReplayTelemetryInterpolator(archive);

            using (var reader = new VideoReplayReader(options.Video, options.StartSeconds, options.DurationSeconds, options.GapResetSeconds))
            {
                if (options.CameraWidth > 0 && options.CameraWidth != reader.Metadata.Width || options.CameraHeight > 0 && options.CameraHeight != reader.Metadata.Height) throw new InvalidOperationException("Configured camera resolution does not match decoded video.");
                options.CameraWidth = reader.Metadata.Width; options.CameraHeight = reader.Metadata.Height;
                var intrinsics = CameraIntrinsics.FromFov(options.CameraWidth, options.CameraHeight, options.HorizontalFovDegrees, options.VerticalFovDegrees);
                var mount = new CameraMount(Deg(options.CameraMountRollDegrees), Deg(options.CameraMountPitchDegrees), Deg(options.CameraMountYawDegrees));
                options.SaveResolved(Path.Combine(options.Output, "config-resolved.json"));
                var report = new ReplayReportAccumulator(); var builder = new OpticalFlowRadBuilder(new OpticalFlowRadBuilderOptions { EmitDegraded = options.EmitDegraded });
                IOpticalFlowRadPublisher publisher = new CsvOpticalFlowRadPublisher(Path.Combine(options.Output, "optical-flow-rad.csv")); ReplaySafety.EnsureDiagnosticsPublisher(publisher);
                using (publisher)
                using (var csv = new ReplayCsvWriter(Path.Combine(options.Output, "replay.csv")))
                using (var processor = new ReplayVisionProcessor(intrinsics, mount, options.RotationMode))
                using (var preview = options.Preview ? new ReplayPreviewRenderer() : null)
                {
                    VideoWriter videoWriter = null; long snapshots = 0; var firstVideoTime = double.NaN;
                    try
                    {
                        VideoReplayFrame frame;
                        while (reader.TryRead(out frame)) using (frame)
                        {
                            if (!Finite(firstVideoTime)) firstVideoTime = frame.TimestampSeconds;
                            if (frame.Gap || frame.TimestampRollback) { processor.Reset(); builder.Reset(); }
                            var tlogTime = timeline.TelemetryTime(frame.TimestampSeconds); var telemetry = interpolator.Sample(tlogTime);
                            var syncError = telemetry.AttitudeValid && telemetry.GyroValid ? System.Math.Max(telemetry.AttitudeAgeSeconds, telemetry.GyroAgeSeconds) : double.PositiveInfinity;
                            var sw = Stopwatch.StartNew(); var vision = processor.Process(frame.Image, frame.DeltaSeconds, telemetry, frame.Gap || frame.TimestampRollback, sync.Confidence, syncError, frame.Duplicate); sw.Stop();
                            if (syncError * 1000 > options.MaximumSyncErrorMilliseconds && vision.State == FlowTrackingStatus.OK) { vision.State = FlowTrackingStatus.DEGRADED; vision.Reason = "synchronization_error"; vision.MavlinkQuality = (byte)System.Math.Min((int)vision.MavlinkQuality, 31); }
                            var rad = builder.Build(new OpticalFlowRadBuildInput { TimeUsec = (ulong)System.Math.Max(1, System.Math.Round(frame.TimestampSeconds * 1e6)), SensorId = 0, Flow = vision.AngularFlow, TrackingStatus = vision.State, Quality = vision.MavlinkQuality, DistanceMeters = telemetry.AltitudeMeters, DistanceAgeSeconds = telemetry.AltitudeAgeSeconds, MaximumDistanceAgeSeconds = .5, SourceFrame = frame.Index, VideoTimestampSeconds = frame.TimestampSeconds, TelemetryTimestampSeconds = tlogTime });
                            publisher.Publish(rad);
                            var record = new ReplayRecord { Frame = frame, ReplayTimeSeconds = timeline.ReplayTime(frame.TimestampSeconds, firstVideoTime), TlogTimeSeconds = tlogTime, SyncErrorSeconds = syncError, ProcessingMilliseconds = sw.Elapsed.TotalMilliseconds, Telemetry = telemetry, Vision = vision, OpticalFlowRad = rad };
                            csv.Write(record); report.Add(record);
                            if (options.Preview || options.SaveAnnotatedVideo)
                            {
                                using (var annotated = preview != null ? preview.Annotate(record) : AnnotateMinimal(record))
                                {
                                    if (options.SaveAnnotatedVideo) { if (videoWriter == null) videoWriter = OpenVideo(options.Output, reader.Metadata.NominalFps, annotated.Size()); videoWriter?.Write(annotated); }
                                    if (preview != null)
                                    {
                                        preview.Show(annotated); if (preview.ResetRequested) { processor.Reset(); builder.Reset(); preview.ClearCommands(); }
                                        if (preview.SnapshotRequested) { Cv2.ImWrite(Path.Combine(options.Output, "snapshots", $"frame-{frame.Index:D8}-{snapshots++:D3}.png"), annotated); preview.ClearCommands(); }
                                        if (preview.ExitRequested) break;
                                    }
                                }
                            }
                            if (frame.Index % 250 == 0) Console.WriteLine($"frame {frame.Index} t={frame.TimestampSeconds:F2}s state={vision.State} q={vision.MavlinkQuality} sync={syncError * 1000:F1}ms");
                        }
                    }
                    finally { videoWriter?.Dispose(); }
                }
                ReplayReportWriter.Write(Path.Combine(options.Output, "report.md"), options, reader.Metadata, archive, sync, report);
                Console.WriteLine($"Replay complete: {reader.Metadata.DecodedFrames} frames, {report.Samples.Count(x => x.Publishable)} publishable diagnostics samples.");
                Console.WriteLine("No MAVLink messages were transmitted. Output: " + options.Output);
            }
            return 0;
        }

        static void FilterAltitude(ReplayTelemetryArchive archive, string source)
        {
            if (string.IsNullOrWhiteSpace(source) || source.Equals("auto", StringComparison.OrdinalIgnoreCase)) return;
            archive.Altitudes.RemoveAll(x => !x.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
        }
        static VideoWriter OpenVideo(string output, double fps, Size size)
        {
            var writer = new VideoWriter(Path.Combine(output, "annotated.mkv"), FourCC.XVID, fps > 0 ? fps : 25, size); if (writer.IsOpened()) return writer; writer.Dispose(); return null;
        }
        static Mat AnnotateMinimal(ReplayRecord record)
        {
            var frame = record.Frame.Image.Clone(); Cv2.PutText(frame, $"{record.Vision.State} q={record.Vision.MavlinkQuality} flow=({record.Vision.Compensation.CompensatedFlowX:F2},{record.Vision.Compensation.CompensatedFlowY:F2})", new Point(8, 28), HersheyFonts.HersheySimplex, .65, Scalar.LimeGreen, 2); return frame;
        }
        static void WriteSynchronization(string path, ClockAlignmentResult value)
        {
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                w.WriteLine("{"); w.WriteLine($"  \"offsetSeconds\": {F(value.OffsetSeconds)},"); w.WriteLine($"  \"correlation\": {F(value.Correlation)},"); w.WriteLine($"  \"confidence\": {F(value.Confidence)},"); w.WriteLine($"  \"automatic\": {value.Automatic.ToString().ToLowerInvariant()},"); w.WriteLine($"  \"applied\": {value.Applied.ToString().ToLowerInvariant()},"); w.WriteLine($"  \"reason\": \"{(value.Reason ?? "").Replace("\"", "\\\"")}\","); w.WriteLine("  \"alternatives\": ["); for (var i = 0; i < value.Alternatives.Count; i++) { var p = value.Alternatives[i]; w.Write($"    {{ \"offsetSeconds\": {F(p.OffsetSeconds)}, \"correlation\": {F(p.Correlation)} }}"); w.WriteLine(i + 1 < value.Alternatives.Count ? "," : ""); } w.WriteLine("  ]"); w.WriteLine("}");
            }
        }
        static double Deg(double degrees) => degrees * System.Math.PI / 180;
        static string F(double value) => (Finite(value) ? value : 0).ToString("0.#########", CultureInfo.InvariantCulture);
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static void Usage() => Console.WriteLine("SarmatVisionHold.ReplayAnalyzer --video <mkv/mp4> --tlog <Mission Planner .tlog> --output <dir> [--config file] [--preview|--headless] [--start sec] [--duration sec] [--video-offset-ms N] [--auto-sync] [--camera-width N --camera-height N] [--horizontal-fov deg] [--vertical-fov deg] [--camera-mount-roll deg --camera-mount-pitch deg --camera-mount-yaw deg] [--altitude-source auto|DISTANCE_SENSOR|RANGEFINDER|TERRAIN|RELATIVE|BARO_DELTA] [--max-sync-error-ms N] [--save-annotated-video] [--log-level level]");
    }
}
