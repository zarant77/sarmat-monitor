using System;
using SarmatVisionHold.Replay.Camera;
using SarmatVisionHold.Replay.Math;
using SarmatVisionHold.Vision;

namespace SarmatVisionHold.Replay.Processing
{
    public sealed class AngularFlowSample
    {
        public double IntegratedFlowXRad, IntegratedFlowYRad;
        public double IntegratedGyroXRad, IntegratedGyroYRad, IntegratedGyroZRad;
        public uint IntegrationTimeUs;
    }

    public static class FlowRadConverter
    {
        // OpenCV image X is right and image Y is down. MAVLink integrated_x/y are
        // angular flow around sensor X/Y: +image Y maps to +X, +image X maps to -Y.
        public static AngularFlowSample Convert(double compensatedPixelsX, double compensatedPixelsY, Vector3d integratedCameraGyro, double deltaSeconds, CameraIntrinsics intrinsics)
        {
            if (intrinsics == null) throw new ArgumentNullException(nameof(intrinsics));
            if (!Finite(compensatedPixelsX) || !Finite(compensatedPixelsY) || !integratedCameraGyro.IsFinite || !Finite(deltaSeconds) || deltaSeconds <= 0)
                throw new ArgumentException("Flow conversion inputs must be finite and delta time must be positive.");
            var micros = deltaSeconds * 1e6;
            if (micros < 1 || micros > uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            return new AngularFlowSample
            {
                IntegratedFlowXRad = System.Math.Atan2(compensatedPixelsY, intrinsics.Fy),
                IntegratedFlowYRad = -System.Math.Atan2(compensatedPixelsX, intrinsics.Fx),
                IntegratedGyroXRad = integratedCameraGyro.X,
                IntegratedGyroYRad = integratedCameraGyro.Y,
                IntegratedGyroZRad = integratedCameraGyro.Z,
                IntegrationTimeUs = (uint)System.Math.Round(micros)
            };
        }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class QualityInput
    {
        public int TrackedPoints, InlierCount;
        public double ForwardBackwardError, CompensationResidualPixels, FrameAgeSeconds, TelemetryAgeSeconds, SyncConfidence = 1, BlurTextureScore = 1;
        public bool AltitudeValid = true, ImuValid = true, LargeFrameGap, DecoderError;
    }
    public static class ReplayQualityMapper
    {
        public static byte Map(QualityInput input)
        {
            if (input == null || input.DecoderError || input.LargeFrameGap || !input.ImuValid || input.TrackedPoints <= 0 || input.InlierCount <= 0) return 0;
            var pointScore = Clamp(input.TrackedPoints / 80d, 0, 1);
            var inlierScore = Clamp((double)input.InlierCount / input.TrackedPoints, 0, 1);
            var fb = System.Math.Exp(-System.Math.Max(0, Safe(input.ForwardBackwardError, 100)) / 2);
            var residual = System.Math.Exp(-System.Math.Max(0, Safe(input.CompensationResidualPixels, 100)) / 4);
            var frameAge = System.Math.Exp(-System.Math.Max(0, Safe(input.FrameAgeSeconds, 10)) / .2);
            var telemetryAge = System.Math.Exp(-System.Math.Max(0, Safe(input.TelemetryAgeSeconds, 10)) / .15);
            var sync = Clamp(Safe(input.SyncConfidence, 0), 0, 1); var texture = Clamp(Safe(input.BlurTextureScore, 0), 0, 1);
            var altitude = input.AltitudeValid ? 1 : .35;
            var score = pointScore * System.Math.Sqrt(inlierScore * fb * residual) * frameAge * telemetryAge * sync * texture * altitude;
            return (byte)System.Math.Round(Clamp(score, 0, 1) * 255);
        }
        static double Safe(double value, double fallback) => !double.IsNaN(value) && !double.IsInfinity(value) ? value : fallback;
        static double Clamp(double value, double lo, double hi) => System.Math.Max(lo, System.Math.Min(hi, value));
    }

    public sealed class OpticalFlowRadModel
    {
        public ulong TimeUsec;
        public byte SensorId;
        public uint IntegrationTimeUs;
        public float IntegratedX, IntegratedY, IntegratedXgyro, IntegratedYgyro, IntegratedZgyro;
        public short Temperature;
        public byte Quality;
        public uint TimeDeltaDistanceUs;
        public float Distance;
        public long SourceFrame;
        public double VideoTimestampSeconds, TelemetryTimestampSeconds;
        public bool Publishable;
        public string RejectReason;
    }
    public sealed class OpticalFlowRadBuildInput
    {
        public ulong TimeUsec;
        public byte SensorId;
        public AngularFlowSample Flow;
        public FlowTrackingStatus TrackingStatus;
        public byte Quality;
        public double DistanceMeters = -1, DistanceAgeSeconds = double.PositiveInfinity, MaximumDistanceAgeSeconds = .5;
        public long SourceFrame;
        public double VideoTimestampSeconds, TelemetryTimestampSeconds;
    }
    public sealed class OpticalFlowRadBuilderOptions { public bool EmitDegraded = true; public byte MaximumDegradedQuality = 63; }

    public sealed class OpticalFlowRadBuilder
    {
        public const short UnknownTemperature = short.MinValue;
        readonly OpticalFlowRadBuilderOptions options; ulong previousTimeUsec;
        public OpticalFlowRadBuilder(OpticalFlowRadBuilderOptions options = null) { this.options = options ?? new OpticalFlowRadBuilderOptions(); }
        public OpticalFlowRadModel Build(OpticalFlowRadBuildInput input)
        {
            if (input == null || input.Flow == null) return Rejected(input, "missing_input");
            if (input.TrackingStatus == FlowTrackingStatus.LOST) return Rejected(input, "tracking_lost");
            if (input.TrackingStatus == FlowTrackingStatus.DEGRADED && !options.EmitDegraded) return Rejected(input, "tracking_degraded");
            if (input.Flow.IntegrationTimeUs == 0) return Rejected(input, "invalid_integration_time");
            if (!Finite(input.Flow.IntegratedFlowXRad) || !Finite(input.Flow.IntegratedFlowYRad) || !Finite(input.Flow.IntegratedGyroXRad) || !Finite(input.Flow.IntegratedGyroYRad) || !Finite(input.Flow.IntegratedGyroZRad)) return Rejected(input, "non_finite_field");
            var time = input.TimeUsec == 0 ? previousTimeUsec + 1 : input.TimeUsec;
            if (time <= previousTimeUsec) time = previousTimeUsec + 1;
            previousTimeUsec = time;
            var distanceValid = Finite(input.DistanceMeters) && input.DistanceMeters > 0 && Finite(input.DistanceAgeSeconds) && input.DistanceAgeSeconds >= 0 && input.DistanceAgeSeconds <= input.MaximumDistanceAgeSeconds;
            var quality = input.TrackingStatus == FlowTrackingStatus.DEGRADED ? (byte)System.Math.Min(input.Quality, options.MaximumDegradedQuality) : input.Quality;
            return new OpticalFlowRadModel
            {
                TimeUsec = time, SensorId = input.SensorId, IntegrationTimeUs = input.Flow.IntegrationTimeUs,
                IntegratedX = (float)input.Flow.IntegratedFlowXRad, IntegratedY = (float)input.Flow.IntegratedFlowYRad,
                IntegratedXgyro = (float)input.Flow.IntegratedGyroXRad, IntegratedYgyro = (float)input.Flow.IntegratedGyroYRad, IntegratedZgyro = (float)input.Flow.IntegratedGyroZRad,
                Temperature = UnknownTemperature, Quality = quality,
                TimeDeltaDistanceUs = distanceValid ? (uint)System.Math.Min(uint.MaxValue, System.Math.Round(input.DistanceAgeSeconds * 1e6)) : 0,
                Distance = distanceValid ? (float)input.DistanceMeters : -1,
                SourceFrame = input.SourceFrame, VideoTimestampSeconds = input.VideoTimestampSeconds, TelemetryTimestampSeconds = input.TelemetryTimestampSeconds,
                Publishable = true
            };
        }
        public void Reset() { previousTimeUsec = 0; }
        static OpticalFlowRadModel Rejected(OpticalFlowRadBuildInput input, string reason) => new OpticalFlowRadModel { SourceFrame = input?.SourceFrame ?? -1, VideoTimestampSeconds = input?.VideoTimestampSeconds ?? 0, TelemetryTimestampSeconds = input?.TelemetryTimestampSeconds ?? 0, Publishable = false, RejectReason = reason, Distance = -1, Temperature = UnknownTemperature };
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
