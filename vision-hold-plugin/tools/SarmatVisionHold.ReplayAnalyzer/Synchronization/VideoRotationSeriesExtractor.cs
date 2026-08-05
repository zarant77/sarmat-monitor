using System;
using System.Collections.Generic;
using OpenCvSharp;
using SarmatVisionHold.Replay.Synchronization;
using SarmatVisionHold.Replay.Telemetry;
using SarmatVisionHold.ReplayAnalyzer.Input;
using SarmatVisionHold.Vision;

namespace SarmatVisionHold.ReplayAnalyzer.Synchronization
{
    public static class VideoRotationSeriesExtractor
    {
        public static List<TimedScalar> Extract(ReplayOptions options)
        {
            var result = new List<TimedScalar>();
            using (var reader = new VideoReplayReader(options.Video, options.StartSeconds, options.DurationSeconds, options.GapResetSeconds))
            using (var tracker = new SparseOpticalFlowProcessor(new SparseOpticalFlowOptions { MaxFeatures = 350, QualityLevel = .003, MinimumDistance = 8, MinimumAcceptedPoints = 10 }))
            {
                VideoReplayFrame frame;
                while (reader.TryRead(out frame)) using (frame)
                {
                    if (frame.Gap || frame.TimestampRollback) tracker.Reset();
                    using (var gray = new Mat())
                    {
                        Cv2.CvtColor(frame.Image, gray, ColorConversionCodes.BGR2GRAY); var flow = tracker.Process(gray);
                        if (frame.DeltaSeconds > 0 && flow.Status != FlowTrackingStatus.LOST && flow.Quality > .08)
                            result.Add(new TimedScalar(frame.TimestampSeconds, flow.RotationDegrees * System.Math.PI / 180 / frame.DeltaSeconds));
                    }
                }
            }
            return result;
        }

        public static List<TimedScalar> TelemetryYawRate(ReplayTelemetryArchive archive)
        {
            var result = new List<TimedScalar>();
            var priority = new[] { "HIGHRES_IMU", "SCALED_IMU", "SCALED_IMU2", "SCALED_IMU3", "ATTITUDE", "RAW_IMU" };
            foreach (var source in priority)
            {
                foreach (var value in archive.Gyros.FindAll(x => string.Equals(x.Source, source, StringComparison.OrdinalIgnoreCase))) result.Add(new TimedScalar(value.TimeSeconds, value.BodyRateRadPerSecond.Z));
                if (result.Count >= 8) break;
            }
            result.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds)); return result;
        }
    }
}
