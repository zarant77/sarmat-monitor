using System;
using System.Collections.Generic;
using System.Linq;
using SarmatVisionHold.Replay.Camera;
using SarmatVisionHold.Replay.Math;

namespace SarmatVisionHold.Replay.Processing
{
    public enum RotationCompensationMode { Gyro, AttitudeDelta, Automatic, Comparison }

    public sealed class ReplayFlowTrack
    {
        public double FromX, FromY, ToX, ToY;
        public bool Accepted = true;
    }
    public sealed class CompensatedFlowVector
    {
        public double FromX, FromY, RawToX, RawToY, PredictedToX, PredictedToY, CompensatedToX, CompensatedToY;
    }
    public sealed class RotationCompensationResult
    {
        public double RawFlowX, RawFlowY;
        public double PredictedRotationX, PredictedRotationY;
        public double CompensatedFlowX, CompensatedFlowY;
        public double ResidualPixels, Confidence;
        public string SelectedMode, Reason;
        public double GyroResidualPixels, AttitudeResidualPixels, ModeDifferencePixels;
        public readonly List<CompensatedFlowVector> Vectors = new List<CompensatedFlowVector>();
    }

    public sealed class RotationCompensator
    {
        public RotationCompensationResult Compensate(IList<ReplayFlowTrack> tracks, CameraIntrinsics intrinsics,
            Quaterniond previousCameraToWorld, Quaterniond currentCameraToWorld, Vector3d integratedCameraGyro,
            RotationCompensationMode mode)
        {
            if (intrinsics == null) throw new ArgumentNullException(nameof(intrinsics));
            var attitudeRayTransform = currentCameraToWorld.Inverse() * previousCameraToWorld;
            var gyroRayTransform = Quaterniond.FromRotationVector(integratedCameraGyro * -1);
            var attitude = Calculate(tracks, intrinsics, attitudeRayTransform, "attitude-delta");
            var gyro = Calculate(tracks, intrinsics, gyroRayTransform, "gyro");
            RotationCompensationResult selected;
            switch (mode)
            {
                case RotationCompensationMode.Gyro: selected = gyro; break;
                case RotationCompensationMode.AttitudeDelta: selected = attitude; break;
                default: selected = gyro.Confidence >= attitude.Confidence ? gyro : attitude; break;
            }
            selected.GyroResidualPixels = gyro.ResidualPixels;
            selected.AttitudeResidualPixels = attitude.ResidualPixels;
            selected.ModeDifferencePixels = System.Math.Sqrt(Square(gyro.PredictedRotationX - attitude.PredictedRotationX) + Square(gyro.PredictedRotationY - attitude.PredictedRotationY));
            if (mode == RotationCompensationMode.Comparison) selected.SelectedMode += "+comparison";
            return selected;
        }

        RotationCompensationResult Calculate(IList<ReplayFlowTrack> tracks, CameraIntrinsics intrinsics, Quaterniond newCameraFromOldCamera, string mode)
        {
            var accepted = (tracks ?? new ReplayFlowTrack[0]).Where(x => x != null && x.Accepted && Finite(x.FromX) && Finite(x.FromY) && Finite(x.ToX) && Finite(x.ToY)).ToList();
            if (accepted.Count == 0) return new RotationCompensationResult { SelectedMode = mode, Reason = "no_tracks" };
            var vectors = new List<CompensatedFlowVector>(); var rawX = new List<double>(); var rawY = new List<double>(); var predictedX = new List<double>(); var predictedY = new List<double>();
            foreach (var track in accepted)
            {
                var rotated = newCameraFromOldCamera.Rotate(intrinsics.Unproject(track.FromX, track.FromY));
                double px, py; if (!intrinsics.TryProject(rotated, out px, out py)) continue;
                var rdx = track.ToX - track.FromX; var rdy = track.ToY - track.FromY;
                var pdx = px - track.FromX; var pdy = py - track.FromY;
                rawX.Add(rdx); rawY.Add(rdy); predictedX.Add(pdx); predictedY.Add(pdy);
                vectors.Add(new CompensatedFlowVector { FromX = track.FromX, FromY = track.FromY, RawToX = track.ToX, RawToY = track.ToY, PredictedToX = px, PredictedToY = py, CompensatedToX = track.FromX + rdx - pdx, CompensatedToY = track.FromY + rdy - pdy });
            }
            if (vectors.Count == 0) return new RotationCompensationResult { SelectedMode = mode, Reason = "unprojectable_tracks" };
            var rawMedianX = ReplayStatistics.Median(rawX.ToArray()); var rawMedianY = ReplayStatistics.Median(rawY.ToArray());
            var predictedMedianX = ReplayStatistics.Median(predictedX.ToArray()); var predictedMedianY = ReplayStatistics.Median(predictedY.ToArray());
            var compensatedX = rawX.Zip(predictedX, (a, b) => a - b).ToArray(); var compensatedY = rawY.Zip(predictedY, (a, b) => a - b).ToArray();
            var cx = ReplayStatistics.Median(compensatedX); var cy = ReplayStatistics.Median(compensatedY);
            var residuals = compensatedX.Select((x, i) => System.Math.Sqrt(Square(x - cx) + Square(compensatedY[i] - cy))).ToArray();
            var residual = ReplayStatistics.Median(residuals);
            var confidence = Clamp((double)vectors.Count / accepted.Count, 0, 1) * System.Math.Exp(-residual / 4);
            return new RotationCompensationResult { RawFlowX = rawMedianX, RawFlowY = rawMedianY, PredictedRotationX = predictedMedianX, PredictedRotationY = predictedMedianY, CompensatedFlowX = cx, CompensatedFlowY = cy, ResidualPixels = residual, Confidence = Finite(confidence) ? confidence : 0, SelectedMode = mode, Reason = confidence >= .25 ? "ok" : "large_residual", Vectors = { } }.WithVectors(vectors);
        }
        static double Square(double value) => value * value;
        static double Clamp(double value, double lo, double hi) => System.Math.Max(lo, System.Math.Min(hi, value));
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    static class RotationCompensationResultExtensions
    {
        public static RotationCompensationResult WithVectors(this RotationCompensationResult result, IEnumerable<CompensatedFlowVector> vectors)
        {
            result.Vectors.AddRange(vectors); return result;
        }
    }
}
