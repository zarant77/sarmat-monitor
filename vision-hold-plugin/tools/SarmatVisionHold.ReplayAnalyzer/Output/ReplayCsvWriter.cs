using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SarmatVisionHold.ReplayAnalyzer.Output
{
    public sealed class ReplayCsvWriter : IDisposable
    {
        readonly StreamWriter writer;
        public ReplayCsvWriter(string path)
        {
            writer = new StreamWriter(path, false, new UTF8Encoding(false));
            writer.WriteLine("replay_time,video_timestamp,tlog_timestamp,sync_error_ms,frame_index,dt,roll,pitch,yaw,gyro_x,gyro_y,gyro_z,gyro_source,altitude,altitude_source,telemetry_age_ms,raw_flow_x_px,raw_flow_y_px,raw_flow_x_px_s,raw_flow_y_px_s,predicted_rotation_x_px,predicted_rotation_y_px,compensated_flow_x_px,compensated_flow_y_px,residual_px,tracked_points,inliers,quality,state,reason,processing_ms,compensation_mode,gyro_residual_px,attitude_residual_px");
        }
        public void Write(ReplayRecord r)
        {
            var t = r.Telemetry; var v = r.Vision; var dt = r.Frame.DeltaSeconds;
            writer.WriteLine(string.Join(",", F(r.ReplayTimeSeconds), F(r.Frame.TimestampSeconds), F(r.TlogTimeSeconds), F(r.SyncErrorSeconds * 1000), r.Frame.Index, F(dt), F(t?.EulerRad.X ?? 0), F(t?.EulerRad.Y ?? 0), F(t?.EulerRad.Z ?? 0), F(t?.BodyRateRadPerSecond.X ?? 0), F(t?.BodyRateRadPerSecond.Y ?? 0), F(t?.BodyRateRadPerSecond.Z ?? 0), E(t?.GyroSource), F(t?.AltitudeMeters ?? 0), E(t?.AltitudeSource), F((t?.MaxAgeSeconds ?? 0) * 1000), F(v.Raw.TranslationX), F(v.Raw.TranslationY), F(dt > 0 ? v.Raw.TranslationX / dt : 0), F(dt > 0 ? v.Raw.TranslationY / dt : 0), F(v.Compensation.PredictedRotationX), F(v.Compensation.PredictedRotationY), F(v.Compensation.CompensatedFlowX), F(v.Compensation.CompensatedFlowY), F(v.Compensation.ResidualPixels), v.Raw.TrackedPoints, v.Raw.InlierCount, v.MavlinkQuality, v.State, E(v.Reason), F(r.ProcessingMilliseconds), E(v.Compensation.SelectedMode), F(v.Compensation.GyroResidualPixels), F(v.Compensation.AttitudeResidualPixels)));
            writer.Flush();
        }
        public void Dispose() { writer.Dispose(); }
        static string F(double value) => double.IsNaN(value) || double.IsInfinity(value) ? "" : value.ToString("0.#########", CultureInfo.InvariantCulture);
        static string E(string value) => string.IsNullOrWhiteSpace(value) ? "" : "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
