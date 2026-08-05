using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using SarmatVisionHold.Replay.Camera;
using SarmatVisionHold.Replay.Math;
using SarmatVisionHold.Replay.Processing;
using SarmatVisionHold.Replay.Telemetry;
using SarmatVisionHold.Vision;

namespace SarmatVisionHold.ReplayAnalyzer.Processing
{
    public sealed class ReplayVisionResult
    {
        public SparseOpticalFlowResult Raw = new SparseOpticalFlowResult { Status = FlowTrackingStatus.LOST, Reason = "not_processed" };
        public RotationCompensationResult Compensation = new RotationCompensationResult { Reason = "not_processed" };
        public AngularFlowSample AngularFlow;
        public Vector3d IntegratedCameraGyro;
        public FlowTrackingStatus State = FlowTrackingStatus.LOST;
        public string Reason = "not_processed";
        public byte MavlinkQuality;
    }

    public sealed class ReplayVisionProcessor : IDisposable
    {
        readonly SparseOpticalFlowProcessor tracker;
        readonly RotationCompensator compensator = new RotationCompensator();
        readonly CameraIntrinsics intrinsics;
        readonly CameraMount mount;
        readonly RotationCompensationMode mode;
        ReplayTelemetrySample previousTelemetry;

        public ReplayVisionProcessor(CameraIntrinsics intrinsics, CameraMount mount, RotationCompensationMode mode)
        {
            this.intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics)); this.mount = mount ?? throw new ArgumentNullException(nameof(mount)); this.mode = mode;
            tracker = new SparseOpticalFlowProcessor(new SparseOpticalFlowOptions { MaxFeatures = 800, QualityLevel = .001, MinimumDistance = 5, MinimumAcceptedPoints = 12, ForwardBackwardErrorThreshold = 2.5, RansacReprojectionThreshold = 2.5 });
        }

        public ReplayVisionResult Process(Mat image, double dt, ReplayTelemetrySample telemetry, bool largeGap, double syncConfidence, double syncErrorSeconds, bool duplicate)
        {
            var output = new ReplayVisionResult();
            if (image == null || image.Empty()) { output.Reason = "empty_frame"; return output; }
            if (largeGap) Reset();
            SparseOpticalFlowResult flow;
            using (var gray = new Mat())
            {
                if (image.Channels() == 1) image.CopyTo(gray); else Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                using (var enhanced = new Mat()) { Cv2.EqualizeHist(gray, enhanced); flow = tracker.Process(enhanced); }
            }
            output.Raw = flow;
            // The first frame after start/gap still primes the shared tracker.
            if (largeGap || !Finite(dt) || dt <= 0 || dt > .5) { output.Reason = largeGap ? "timeline_gap" : "initializing_timeline"; previousTelemetry = telemetry; return output; }
            if (duplicate) { output.State = FlowTrackingStatus.DEGRADED; output.Reason = "duplicate_frame"; previousTelemetry = telemetry; return output; }
            if (flow.Status == FlowTrackingStatus.LOST) { output.Reason = flow.Reason ?? "tracking_lost"; previousTelemetry = telemetry; return output; }
            if (telemetry == null || previousTelemetry == null || !telemetry.GyroValid || !previousTelemetry.GyroValid || !telemetry.AttitudeValid || !previousTelemetry.AttitudeValid)
            {
                output.State = FlowTrackingStatus.DEGRADED; output.Reason = "missing_or_stale_imu"; previousTelemetry = telemetry; return output;
            }
            var previousRate = mount.BodyRateToCamera(previousTelemetry.BodyRateRadPerSecond);
            var currentRate = mount.BodyRateToCamera(telemetry.BodyRateRadPerSecond);
            output.IntegratedCameraGyro = (previousRate + currentRate) * (.5 * dt);
            var previousCameraToWorld = mount.CameraToWorld(previousTelemetry.BodyToNed);
            var currentCameraToWorld = mount.CameraToWorld(telemetry.BodyToNed);
            var tracks = flow.Tracks.Select(t => new ReplayFlowTrack { FromX = t.From.X, FromY = t.From.Y, ToX = t.To.X, ToY = t.To.Y, Accepted = t.Accepted }).ToList();
            output.Compensation = compensator.Compensate(tracks, intrinsics, previousCameraToWorld, currentCameraToWorld, output.IntegratedCameraGyro, mode);
            try { output.AngularFlow = FlowRadConverter.Convert(output.Compensation.CompensatedFlowX, output.Compensation.CompensatedFlowY, output.IntegratedCameraGyro, dt, intrinsics); }
            catch { output.State = FlowTrackingStatus.LOST; output.Reason = "invalid_flow_radians"; previousTelemetry = telemetry; return output; }
            output.MavlinkQuality = ReplayQualityMapper.Map(new QualityInput { TrackedPoints = flow.TrackedPoints, InlierCount = flow.InlierCount, ForwardBackwardError = MedianForwardBackward(flow), CompensationResidualPixels = output.Compensation.ResidualPixels, FrameAgeSeconds = 0, TelemetryAgeSeconds = telemetry.MaxAgeSeconds, SyncConfidence = syncConfidence, BlurTextureScore = flow.Quality, AltitudeValid = telemetry.AltitudeValid, ImuValid = telemetry.GyroValid && telemetry.AttitudeValid, LargeFrameGap = largeGap });
            var desync = syncErrorSeconds > .03;
            output.State = flow.Status == FlowTrackingStatus.DEGRADED || output.Compensation.Confidence < .25 || desync || output.MavlinkQuality < 32 ? FlowTrackingStatus.DEGRADED : FlowTrackingStatus.OK;
            output.Reason = desync ? "synchronization_error" : output.Compensation.Confidence < .25 ? "rotation_residual" : output.MavlinkQuality < 32 ? "low_quality" : flow.Reason ?? "ok";
            previousTelemetry = telemetry; return output;
        }

        public void PrimeTelemetry(ReplayTelemetrySample telemetry) { previousTelemetry = telemetry; }
        public void Reset() { tracker.Reset(); previousTelemetry = null; }
        public void Dispose() { tracker.Dispose(); }
        static double MedianForwardBackward(SparseOpticalFlowResult flow) => ReplayStatistics.Median(flow.Tracks.Where(x => x.Accepted).Select(x => x.ForwardBackwardError).ToArray());
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
