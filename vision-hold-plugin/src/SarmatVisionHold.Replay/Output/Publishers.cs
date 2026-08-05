using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SarmatVisionHold.Replay.Processing;

namespace SarmatVisionHold.Replay.Output
{
    public interface IOpticalFlowRadPublisher : IDisposable { void Publish(OpticalFlowRadModel sample); }
    public static class ReplaySafety
    {
        public const bool DiagnosticsOnly = true;
        public static void EnsureDiagnosticsPublisher(IOpticalFlowRadPublisher publisher)
        {
            if (!DiagnosticsOnly) throw new InvalidOperationException("Replay safety build is not diagnostics-only.");
            if (publisher == null || publisher is NullOpticalFlowRadPublisher || publisher is CsvOpticalFlowRadPublisher || publisher is MockOpticalFlowRadPublisher) return;
            throw new InvalidOperationException("DiagnosticsOnly=true forbids real MAVLink/serial/UDP publishers.");
        }
    }
    public sealed class NullOpticalFlowRadPublisher : IOpticalFlowRadPublisher { public void Publish(OpticalFlowRadModel sample) { } public void Dispose() { } }
    public sealed class MockOpticalFlowRadPublisher : IOpticalFlowRadPublisher
    {
        public readonly List<OpticalFlowRadModel> Samples = new List<OpticalFlowRadModel>();
        public void Publish(OpticalFlowRadModel sample) { if (sample != null) Samples.Add(sample); }
        public void Dispose() { }
    }
    public sealed class CsvOpticalFlowRadPublisher : IOpticalFlowRadPublisher
    {
        readonly StreamWriter writer;
        public CsvOpticalFlowRadPublisher(string path)
        {
            writer = new StreamWriter(path, false, new UTF8Encoding(false));
            writer.WriteLine("time_usec,sensor_id,integration_time_us,integrated_x,integrated_y,integrated_xgyro,integrated_ygyro,integrated_zgyro,temperature,quality,time_delta_distance_us,distance,source_frame,video_timestamp,tlog_timestamp,publishable,reject_reason");
        }
        public void Publish(OpticalFlowRadModel s)
        {
            if (s == null) return;
            writer.WriteLine(string.Join(",", s.TimeUsec, s.SensorId, s.IntegrationTimeUs, F(s.IntegratedX), F(s.IntegratedY), F(s.IntegratedXgyro), F(s.IntegratedYgyro), F(s.IntegratedZgyro), s.Temperature, s.Quality, s.TimeDeltaDistanceUs, F(s.Distance), s.SourceFrame, F(s.VideoTimestampSeconds), F(s.TelemetryTimestampSeconds), s.Publishable ? "true" : "false", Escape(s.RejectReason)));
            writer.Flush();
        }
        public void Dispose() { writer.Dispose(); }
        static string F(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
        static string Escape(string value) => string.IsNullOrEmpty(value) ? "" : "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
