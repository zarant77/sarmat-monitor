using System;
using System.Collections.Generic;
using System.Linq;
using SarmatVisionHold.Replay.Camera;
using SarmatVisionHold.Replay.Math;
using SarmatVisionHold.Replay.Output;
using SarmatVisionHold.Replay.Processing;
using SarmatVisionHold.Replay.Synchronization;
using SarmatVisionHold.Replay.Telemetry;
using SarmatVisionHold.Vision;

namespace SarmatVisionHold.Replay.Tests
{
    internal static class Program
    {
        static int passed;
        static int Main()
        {
            var tests = new Action[] { Intrinsics, InvalidFov, AttitudeInterpolation, YawWraparound, QuaternionSlerp, QuaternionFallsBackToFreshEuler, GyroInterpolationAndStale, AltitudePriorityAndStale, ManualOffset, CrossCorrelationOffset, LowConfidenceAutoSync, CameraMountTransform, PureYawCompensation, PureRollCompensation, PurePitchCompensation, MixedRotation, ZeroRotation, TranslationRemains, RotationalFlowRemoved, PixelDisplacementToRadians, IntegrationTimeAndFieldUnits, QualityMapping, InvalidAndStaleDistance, MonotonicTimestampsAndReset, NonFiniteRejection, LostNotPublishable, CoordinateSigns, DiagnosticsSafetyGuard, SyntheticReplayScenarios };
            try { foreach (var test in tests) { test(); passed++; Console.WriteLine("PASS " + test.Method.Name); } Console.WriteLine($"All {passed} replay test groups passed."); return 0; }
            catch (Exception e) { Console.Error.WriteLine("FAIL after " + passed + " groups: " + e); return 1; }
        }

        static void Intrinsics()
        {
            var c = CameraIntrinsics.FromFov(640, 480, 90); Near(320, c.Fx, 1e-9); Near(320, c.Fy, 1e-9); Near(319.5, c.Cx); Near(239.5, c.Cy); True(c.VerticalFovDegrees > 73 && c.VerticalFovDegrees < 74); True(c.ResolutionMatches(640, 480));
            var vertical = CameraIntrinsics.FromFov(640, 480, 0, c.VerticalFovDegrees); Near(90, vertical.HorizontalFovDegrees, 1e-9);
        }
        static void InvalidFov()
        {
            Throws(() => CameraIntrinsics.FromFov(0, 480, 90)); foreach (var value in new[] { 0d, -1, 180, double.NaN, double.PositiveInfinity }) Throws(() => CameraIntrinsics.FromFov(640, 480, value));
            Throws(() => CameraIntrinsics.FromFov(640, 480, 90, 90));
        }
        static void AttitudeInterpolation()
        {
            var values = new[] { A(0, 0, 0, 0, false), A(1, .2, -.4, .6, false) }; var i = new AttitudeInterpolator(values, 2); Quaterniond q; double age, span; string source; True(i.TrySample(.5, out q, out age, out span, out source)); var e = q.ToEuler(); Near(.1, e.X, .03); Near(-.2, e.Y, .03); Near(.3, e.Z, .03); Eq("ATTITUDE", source);
        }
        static void YawWraparound()
        {
            var values = new[] { A(0, 0, 0, Deg(179), false), A(1, 0, 0, Deg(-179), false) }; var i = new AttitudeInterpolator(values, 2); Quaterniond q; double age, span; string source; True(i.TrySample(.5, out q, out age, out span, out source)); True(System.Math.Abs(System.Math.Abs(q.ToEuler().Z) - System.Math.PI) < .03, "yaw interpolated through zero instead of wraparound");
        }
        static void QuaternionSlerp()
        {
            var values = new[] { A(0, 0, 0, 0, true), A(2, 0, 0, System.Math.PI / 2, true) }; var i = new AttitudeInterpolator(values, 3); Quaterniond q; double age, span; string source; True(i.TrySample(1, out q, out age, out span, out source)); Near(System.Math.PI / 4, q.ToEuler().Z, 1e-6); Eq("ATTITUDE_QUATERNION", source);
        }
        static void QuaternionFallsBackToFreshEuler()
        {
            var values = new[] { A(0, 0, 0, 1, true), A(.1, 0, 0, 1.1, true), A(5, 0, 0, .2, false), A(5.1, 0, 0, .3, false) };
            var i = new AttitudeInterpolator(values, .2); Quaterniond q; double age, span; string source; True(i.TrySample(5.05, out q, out age, out span, out source)); Eq("ATTITUDE", source); Near(.25, q.ToEuler().Z, .01);
        }
        static void GyroInterpolationAndStale()
        {
            var values = new[] { G(0, 0, "ATTITUDE"), G(1, 10, "ATTITUDE"), G(0, 2, "HIGHRES_IMU"), G(1, 4, "HIGHRES_IMU") }; var i = new GyroInterpolator(values, 2); Vector3d value; double age, span; string source; True(i.TrySample(.5, out value, out age, out span, out source)); Near(3, value.Z); Eq("HIGHRES_IMU", source);
            var stale = new GyroInterpolator(values, .1); False(stale.TrySample(10, out value, out age, out span, out source));
        }
        static void AltitudePriorityAndStale()
        {
            var values = new[] { H(0, 10, "RELATIVE"), H(1, 12, "RELATIVE"), H(0, 2, "DISTANCE_SENSOR"), H(1, 4, "DISTANCE_SENSOR") }; var i = new AltitudeInterpolator(values, 2); double value, age, span; string source; True(i.TrySample(.5, out value, out age, out span, out source)); Near(3, value); Eq("DISTANCE_SENSOR", source); False(new AltitudeInterpolator(values, .1).TrySample(5, out value, out age, out span, out source));
        }
        static void ManualOffset() { var t = new ReplayTimeline(.125); Near(5.125, t.TelemetryTime(5)); Near(2, t.ReplayTime(7, 5)); Throws(() => new ReplayTimeline(double.NaN)); }
        static void CrossCorrelationOffset()
        {
            const double expected = .72; var telemetry = new List<TimedScalar>(); var video = new List<TimedScalar>();
            for (var t = 0d; t < 12; t += .02) telemetry.Add(new TimedScalar(t, Signal(t)));
            for (var t = 1d; t < 10; t += .04) video.Add(new TimedScalar(t, Signal(t + expected)));
            var result = new ClockAlignmentEstimator().Estimate(video, telemetry, -2, 2, .02, .2); Near(expected, result.OffsetSeconds, .04); True(result.Applied, $"confidence={result.Confidence}");
        }
        static void LowConfidenceAutoSync()
        {
            var a = Enumerable.Range(0, 30).Select(x => new TimedScalar(x * .1, 1)).ToList(); var result = new ClockAlignmentEstimator().Estimate(a, a, -1, 1, .1, .35); False(result.Applied); Eq("low_confidence", result.Reason);
        }
        static void CameraMountTransform()
        {
            var yaw = new CameraMount(0, 0, System.Math.PI / 2); var v = yaw.BodyRateToCamera(new Vector3d(1, 0, 0)); Near(0, v.X, 1e-9); Near(1, v.Y, 1e-9);
            var pitch = new CameraMount(0, System.Math.PI / 2, 0); v = pitch.BodyRateToCamera(new Vector3d(1, 0, 0)); Near(-1, v.Z, 1e-9);
        }
        static void PureYawCompensation() => RotationCase(new Vector3d(0, 0, .04), 0, 0);
        static void PureRollCompensation() => RotationCase(new Vector3d(.04, 0, 0), 0, 0);
        static void PurePitchCompensation() => RotationCase(new Vector3d(0, .04, 0), 0, 0);
        static void MixedRotation() => RotationCase(new Vector3d(.02, -.03, .04), 0, 0);
        static void ZeroRotation() => RotationCase(new Vector3d(), 0, 0);
        static void TranslationRemains() => RotationCase(new Vector3d(.02, -.03, .04), 3, -2);
        static void RotationalFlowRemoved()
        {
            var r = SyntheticReplayGenerator.Run(new Vector3d(.03, .02, -.05), 0, 0, false, false, false); True(Hypot(r.RawFlowX, r.RawFlowY) > .2); True(Hypot(r.CompensatedFlowX, r.CompensatedFlowY) < .03);
        }
        static void PixelDisplacementToRadians()
        {
            var c = CameraIntrinsics.FromFov(640, 480, 90); var value = FlowRadConverter.Convert(32, -16, new Vector3d(.1, -.2, .3), .02, c); Near(System.Math.Atan(-.05), value.IntegratedFlowXRad, 1e-12); Near(-System.Math.Atan(.1), value.IntegratedFlowYRad, 1e-12); Eq((uint)20000, value.IntegrationTimeUs);
        }
        static void IntegrationTimeAndFieldUnits()
        {
            var builder = new OpticalFlowRadBuilder(); var model = builder.Build(BuildInput(10, FlowTrackingStatus.OK, .02, 2, 0)); True(model.Publishable); Eq((uint)20000, model.IntegrationTimeUs); Near(2, model.Distance); Eq(OpticalFlowRadBuilder.UnknownTemperature, model.Temperature); Eq((byte)200, model.Quality);
        }
        static void QualityMapping()
        {
            var high = ReplayQualityMapper.Map(new QualityInput { TrackedPoints = 100, InlierCount = 95, ForwardBackwardError = .1, CompensationResidualPixels = .1, SyncConfidence = 1, BlurTextureScore = 1, ImuValid = true, AltitudeValid = true });
            var low = ReplayQualityMapper.Map(new QualityInput { TrackedPoints = 10, InlierCount = 2, ForwardBackwardError = 5, CompensationResidualPixels = 8, SyncConfidence = .2, BlurTextureScore = .2, ImuValid = true, AltitudeValid = false }); True(high > 180); True(low < 10); Eq((byte)0, ReplayQualityMapper.Map(new QualityInput { TrackedPoints = 100, InlierCount = 100, ImuValid = false }));
        }
        static void InvalidAndStaleDistance()
        {
            var builder = new OpticalFlowRadBuilder(); var invalid = builder.Build(BuildInput(10, FlowTrackingStatus.OK, .02, -1, 0)); Near(-1, invalid.Distance); Eq((uint)0, invalid.TimeDeltaDistanceUs); var stale = builder.Build(BuildInput(20, FlowTrackingStatus.OK, .02, 3, 2)); Near(-1, stale.Distance);
        }
        static void MonotonicTimestampsAndReset()
        {
            var builder = new OpticalFlowRadBuilder(); var a = builder.Build(BuildInput(100, FlowTrackingStatus.OK)); var b = builder.Build(BuildInput(50, FlowTrackingStatus.OK)); True(b.TimeUsec > a.TimeUsec); builder.Reset(); var c = builder.Build(BuildInput(50, FlowTrackingStatus.OK)); Eq((ulong)50, c.TimeUsec);
        }
        static void NonFiniteRejection() { var input = BuildInput(1, FlowTrackingStatus.OK); input.Flow.IntegratedFlowXRad = double.NaN; var m = new OpticalFlowRadBuilder().Build(input); False(m.Publishable); Eq("non_finite_field", m.RejectReason); Throws(() => FlowRadConverter.Convert(double.NaN, 0, new Vector3d(), .02, CameraIntrinsics.FromFov(640, 480, 90))); }
        static void LostNotPublishable() { var m = new OpticalFlowRadBuilder().Build(BuildInput(1, FlowTrackingStatus.LOST)); False(m.Publishable); Eq("tracking_lost", m.RejectReason); }
        static void CoordinateSigns()
        {
            var c = CameraIntrinsics.FromFov(640, 480, 90); var imageRight = FlowRadConverter.Convert(10, 0, new Vector3d(), .02, c); Near(0, imageRight.IntegratedFlowXRad); True(imageRight.IntegratedFlowYRad < 0); var imageDown = FlowRadConverter.Convert(0, 10, new Vector3d(), .02, c); True(imageDown.IntegratedFlowXRad > 0); Near(0, imageDown.IntegratedFlowYRad);
            var mount = new CameraMount(0, 0, 0); var v = mount.BodyRateToCamera(new Vector3d(1, -2, 3)); Near(1, v.X); Near(-2, v.Y); Near(3, v.Z);
        }
        static void DiagnosticsSafetyGuard()
        {
            ReplaySafety.EnsureDiagnosticsPublisher(new NullOpticalFlowRadPublisher()); ReplaySafety.EnsureDiagnosticsPublisher(new MockOpticalFlowRadPublisher()); Throws(() => ReplaySafety.EnsureDiagnosticsPublisher(new ForbiddenPublisher())); True(ReplaySafety.DiagnosticsOnly);
        }
        static void SyntheticReplayScenarios()
        {
            var scenarios = new[]
            {
                Scenario("static", new Vector3d(), 0, 0), Scenario("translation_x", new Vector3d(), 3, 0), Scenario("translation_y", new Vector3d(), 0, -2),
                Scenario("yaw", new Vector3d(0,0,.05), 0, 0), Scenario("roll", new Vector3d(.05,0,0), 0, 0), Scenario("pitch", new Vector3d(0,.05,0), 0, 0),
                Scenario("translation_yaw", new Vector3d(0,0,.05), 3, -2), Scenario("variable_fps", new Vector3d(.01,.02,0), 1, 1),
                Scenario("dropped_frames", new Vector3d(), 0, 0), Scenario("stale_imu", new Vector3d(), 0, 0), Scenario("wrong_offset", new Vector3d(), 0, 0), Scenario("loss_texture", new Vector3d(), 0, 0)
            };
            foreach (var s in scenarios)
            {
                if (s.Name == "stale_imu") { Eq((byte)0, ReplayQualityMapper.Map(new QualityInput { TrackedPoints = 100, InlierCount = 100, ImuValid = false })); continue; }
                if (s.Name == "loss_texture") { var lost = new OpticalFlowRadBuilder().Build(BuildInput(1, FlowTrackingStatus.LOST)); False(lost.Publishable); continue; }
                if (s.Name == "wrong_offset") { var flat = Enumerable.Range(0, 20).Select(i => new TimedScalar(i * .1, 0)).ToList(); False(new ClockAlignmentEstimator().Estimate(flat, flat, -1, 1, .1).Applied); continue; }
                var r = SyntheticReplayGenerator.Run(s.Rotation, s.X, s.Y, s.Name == "variable_fps", s.Name == "dropped_frames", false);
                Near(s.X, r.CompensatedFlowX, .04); Near(s.Y, r.CompensatedFlowY, .04); True(r.ResidualPixels < .04, s.Name + " residual=" + r.ResidualPixels);
                Console.WriteLine($"SYNTHETIC {s.Name} raw=({r.RawFlowX:F4},{r.RawFlowY:F4}) compensated=({r.CompensatedFlowX:F4},{r.CompensatedFlowY:F4}) residual={r.ResidualPixels:F5}");
            }
        }

        static void RotationCase(Vector3d rotation, double tx, double ty)
        {
            var result = SyntheticReplayGenerator.Run(rotation, tx, ty, false, false, false); Near(tx, result.CompensatedFlowX, .03); Near(ty, result.CompensatedFlowY, .03); True(result.ResidualPixels < .03); True(result.Confidence > .9);
        }
        static ScenarioData Scenario(string name, Vector3d rotation, double x, double y) => new ScenarioData { Name = name, Rotation = rotation, X = x, Y = y };
        static TimedAttitude A(double time, double roll, double pitch, double yaw, bool quaternion) => new TimedAttitude { TimeSeconds = time, BodyToNed = Quaterniond.FromEuler(roll, pitch, yaw), IsQuaternion = quaternion, Source = quaternion ? "ATTITUDE_QUATERNION" : "ATTITUDE" };
        static TimedGyro G(double time, double z, string source) => new TimedGyro { TimeSeconds = time, BodyRateRadPerSecond = new Vector3d(0, 0, z), Source = source };
        static TimedAltitude H(double time, double value, string source) => new TimedAltitude { TimeSeconds = time, Meters = value, Valid = true, Source = source };
        static OpticalFlowRadBuildInput BuildInput(ulong time, FlowTrackingStatus state, double dt = .02, double distance = 2, double distanceAge = 0) => new OpticalFlowRadBuildInput { TimeUsec = time, Flow = FlowRadConverter.Convert(1, -1, new Vector3d(.01, -.02, .03), dt, CameraIntrinsics.FromFov(640, 480, 90)), TrackingStatus = state, Quality = 200, DistanceMeters = distance, DistanceAgeSeconds = distanceAge, MaximumDistanceAgeSeconds = .5 };
        static double Signal(double t) => System.Math.Sin(t * 1.73) + .6 * System.Math.Sin(t * .47 + .2) + (t > 4.2 && t < 5.1 ? 1.4 : 0) - (t > 7.4 && t < 8 ? .8 : 0);
        static double Hypot(double x, double y) => System.Math.Sqrt(x * x + y * y);
        static double Deg(double value) => value * System.Math.PI / 180;
        static void True(bool value, string message = null) { if (!value) throw new Exception(message ?? "expected true"); }
        static void False(bool value) => True(!value, "expected false");
        static void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"expected {expected}, got {actual}"); }
        static void Near(double expected, double actual, double epsilon = 1e-6) { if (double.IsNaN(actual) || System.Math.Abs(expected - actual) > epsilon) throw new Exception($"expected {expected}, got {actual}, eps={epsilon}"); }
        static void Throws(Action action) { try { action(); } catch { return; } throw new Exception("expected exception"); }

        sealed class ScenarioData { public string Name; public Vector3d Rotation; public double X, Y; }
        sealed class ForbiddenPublisher : IOpticalFlowRadPublisher { public void Publish(OpticalFlowRadModel sample) { } public void Dispose() { } }
    }

    static class SyntheticReplayGenerator
    {
        public static RotationCompensationResult Run(Vector3d cameraRotation, double translationX, double translationY, bool variableFps, bool droppedFrame, bool noise)
        {
            var intrinsics = CameraIntrinsics.FromFov(640, 480, 90); var tracks = new List<ReplayFlowTrack>(); var oldToNew = Quaterniond.FromRotationVector(cameraRotation * -1); var random = new Random(7);
            // Feature grid represents a textured planar ground observed by a calibrated pinhole camera.
            for (var y = 60; y <= 420; y += 45) for (var x = 60; x <= 580; x += 52)
            {
                var ray = oldToNew.Rotate(intrinsics.Unproject(x, y)); double px, py; if (!intrinsics.TryProject(ray, out px, out py)) continue;
                var nx = noise ? (random.NextDouble() - .5) * .02 : 0; var ny = noise ? (random.NextDouble() - .5) * .02 : 0;
                tracks.Add(new ReplayFlowTrack { FromX = x, FromY = y, ToX = px + translationX + nx, ToY = py + translationY + ny, Accepted = true });
            }
            if (droppedFrame) { /* Gap behavior is tested by builder reset; geometry remains deterministic. */ }
            if (variableFps) { /* Displacement is integrated per frame and is independent of nominal FPS. */ }
            return new RotationCompensator().Compensate(tracks, intrinsics, Quaterniond.Identity, Quaterniond.FromRotationVector(cameraRotation), cameraRotation, RotationCompensationMode.Comparison);
        }
    }
}
