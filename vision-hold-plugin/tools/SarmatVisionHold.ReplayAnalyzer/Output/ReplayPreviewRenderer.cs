using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace SarmatVisionHold.ReplayAnalyzer.Output
{
    public sealed class ReplayPreviewRenderer : IDisposable
    {
        readonly List<Mat> history = new List<Mat>(); int historyIndex = -1; int mode = 4;
        public bool Paused { get; private set; }
        public bool ExitRequested { get; private set; }
        public bool ResetRequested { get; private set; }
        public bool SnapshotRequested { get; private set; }

        public Mat Annotate(ReplayRecord record)
        {
            var frame = record.Frame.Image.Clone(); var vision = record.Vision;
            foreach (var track in vision.Raw.Tracks)
            {
                if (!track.Accepted) continue; var from = (Point)track.From;
                if (mode == 1 || mode == 4) Cv2.ArrowedLine(frame, from, (Point)track.To, Scalar.Yellow, 1, LineTypes.AntiAlias, 0, .2);
            }
            foreach (var vector in vision.Compensation.Vectors)
            {
                var from = new Point((int)vector.FromX, (int)vector.FromY);
                if (mode == 2 || mode == 4) Cv2.ArrowedLine(frame, from, new Point((int)vector.PredictedToX, (int)vector.PredictedToY), Scalar.Magenta, 1, LineTypes.AntiAlias, 0, .2);
                if (mode == 3 || mode == 4) Cv2.ArrowedLine(frame, from, new Point((int)vector.CompensatedToX, (int)vector.CompensatedToY), Scalar.LimeGreen, 1, LineTypes.AntiAlias, 0, .2);
            }
            Cv2.Rectangle(frame, new Rect(0, 0, System.Math.Min(frame.Width, 1250), 188), Scalar.Black, -1);
            var t = record.Telemetry; var rad = record.OpticalFlowRad; var color = vision.State == Vision.FlowTrackingStatus.OK ? Scalar.LimeGreen : vision.State == Vision.FlowTrackingStatus.DEGRADED ? Scalar.Orange : Scalar.Red;
            Put(frame, $"frame={record.Frame.Index} video={record.Frame.TimestampSeconds:F3}s tlog={record.TlogTimeSeconds:F3}s sync={record.SyncErrorSeconds * 1000:F1}ms dt={record.Frame.DeltaSeconds * 1000:F1}ms", 22, Scalar.White);
            Put(frame, $"raw=({vision.Raw.TranslationX:F2},{vision.Raw.TranslationY:F2}) rotPred=({vision.Compensation.PredictedRotationX:F2},{vision.Compensation.PredictedRotationY:F2}) compensated=({vision.Compensation.CompensatedFlowX:F2},{vision.Compensation.CompensatedFlowY:F2})", 46, color);
            Put(frame, $"att=({t?.EulerRad.X:F3},{t?.EulerRad.Y:F3},{t?.EulerRad.Z:F3}) gyro=({t?.BodyRateRadPerSecond.X:F3},{t?.BodyRateRadPerSecond.Y:F3},{t?.BodyRateRadPerSecond.Z:F3}) int=({vision.IntegratedCameraGyro.X:F5},{vision.IntegratedCameraGyro.Y:F5},{vision.IntegratedCameraGyro.Z:F5})", 70, Scalar.Cyan);
            Put(frame, $"alt={t?.AltitudeMeters:F2}m source={t?.AltitudeSource ?? "-"} gyroSource={t?.GyroSource ?? "-"} telemetryAge={t?.MaxAgeSeconds * 1000:F1}ms", 94, Scalar.Cyan);
            Put(frame, $"flowRad=({rad?.IntegratedX:F6},{rad?.IntegratedY:F6}) gyroRad=({rad?.IntegratedXgyro:F6},{rad?.IntegratedYgyro:F6},{rad?.IntegratedZgyro:F6}) integration={rad?.IntegrationTimeUs ?? 0}us", 118, Scalar.Yellow);
            Put(frame, $"quality={vision.MavlinkQuality} state={vision.State} reason={vision.Reason} mode={vision.Compensation.SelectedMode} residual={vision.Compensation.ResidualPixels:F2}px publishable={rad?.Publishable ?? false}", 142, color);
            Put(frame, "Space pause | Left/Right inspect history | R reset | S snapshot | 1 raw | 2 rotation | 3 compensated | 4 all | Q/Esc exit", 172, Scalar.White);
            return frame;
        }

        public void Show(Mat annotated)
        {
            history.Add(annotated.Clone()); if (history.Count > 120) { history[0].Dispose(); history.RemoveAt(0); } historyIndex = history.Count - 1;
            Cv2.ImShow("Sarmat Vision Hold - Replay Analyzer", annotated); HandleKey(Cv2.WaitKey(Paused ? 30 : 1));
            while (Paused && !ExitRequested)
            {
                var key = Cv2.WaitKey(30); if (key >= 0) HandleKey(key);
            }
        }
        void HandleKey(int key)
        {
            if (key < 0) return; ResetRequested = SnapshotRequested = false;
            if (key == 27 || key == 'q' || key == 'Q') { ExitRequested = true; return; }
            if (key == ' ') { Paused = !Paused; return; }
            if (key == 'r' || key == 'R') { ResetRequested = true; return; }
            if (key == 's' || key == 'S') { SnapshotRequested = true; return; }
            if (key >= '1' && key <= '4') { mode = key - '0'; return; }
            if ((key == 2424832 || key == 81) && history.Count > 0) historyIndex = System.Math.Max(0, historyIndex - 1);
            if ((key == 2555904 || key == 83) && history.Count > 0) historyIndex = System.Math.Min(history.Count - 1, historyIndex + 1);
            if (historyIndex >= 0 && historyIndex < history.Count) Cv2.ImShow("Sarmat Vision Hold - Replay Analyzer", history[historyIndex]);
        }
        public void ClearCommands() { ResetRequested = SnapshotRequested = false; }
        public void Dispose() { foreach (var frame in history) frame.Dispose(); history.Clear(); Cv2.DestroyAllWindows(); }
        static void Put(Mat frame, string text, int y, Scalar color) => Cv2.PutText(frame, text, new Point(8, y), HersheyFonts.HersheySimplex, .5, color, 1, LineTypes.AntiAlias);
    }
}
