using System;
using SarmatVisionHold.Replay.Math;

namespace SarmatVisionHold.Replay.Camera
{
    public sealed class CameraIntrinsics
    {
        public int Width { get; }
        public int Height { get; }
        public double Fx { get; }
        public double Fy { get; }
        public double Cx { get; }
        public double Cy { get; }
        public double HorizontalFovDegrees { get; }
        public double VerticalFovDegrees { get; }

        CameraIntrinsics(int width, int height, double fx, double fy, double horizontal, double vertical)
        {
            Width = width; Height = height; Fx = fx; Fy = fy; Cx = (width - 1) / 2d; Cy = (height - 1) / 2d;
            HorizontalFovDegrees = horizontal; VerticalFovDegrees = vertical;
        }

        public static CameraIntrinsics FromFov(int width, int height, double horizontalFovDegrees, double verticalFovDegrees = 0)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException("resolution", "Camera resolution must be positive.");
            var hasH = ValidFov(horizontalFovDegrees); var hasV = ValidFov(verticalFovDegrees);
            if (!hasH && !hasV) throw new ArgumentOutOfRangeException("fov", "At least one finite FOV in (0, 180) is required.");
            double fx, fy;
            if (hasH) fx = width / (2 * System.Math.Tan(Deg(horizontalFovDegrees) / 2)); else fx = 0;
            if (hasV) fy = height / (2 * System.Math.Tan(Deg(verticalFovDegrees) / 2)); else fy = 0;
            if (!hasH) { fx = fy; horizontalFovDegrees = Rad(2 * System.Math.Atan(width / (2 * fx))); }
            if (!hasV) { fy = fx; verticalFovDegrees = Rad(2 * System.Math.Atan(height / (2 * fy))); }
            if (!Finite(fx) || !Finite(fy) || fx <= 0 || fy <= 0) throw new ArgumentOutOfRangeException("fov", "FOV produced invalid focal length.");
            if (hasH && hasV && System.Math.Abs(fx - fy) / System.Math.Max(fx, fy) > .1) throw new ArgumentException("Horizontal/vertical FOV are inconsistent with resolution and square-pixel aspect.");
            return new CameraIntrinsics(width, height, fx, fy, horizontalFovDegrees, verticalFovDegrees);
        }

        public Vector3d Unproject(double x, double y) => new Vector3d((x - Cx) / Fx, (y - Cy) / Fy, 1).Normalized();
        public bool TryProject(Vector3d ray, out double x, out double y)
        {
            x = y = 0;
            if (!ray.IsFinite || ray.Z <= 1e-9) return false;
            x = Fx * ray.X / ray.Z + Cx; y = Fy * ray.Y / ray.Z + Cy;
            return Finite(x) && Finite(y);
        }
        public bool ResolutionMatches(int width, int height) => width == Width && height == Height;
        public double AspectConsistencyError => System.Math.Abs(Fx - Fy) / System.Math.Max(Fx, Fy);
        static bool ValidFov(double value) => Finite(value) && value > 0 && value < 180;
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static double Deg(double degrees) => degrees * System.Math.PI / 180;
        static double Rad(double radians) => radians * 180 / System.Math.PI;
    }

    public sealed class CameraMount
    {
        // Rotation from MAVLink body FRD coordinates into OpenCV camera coordinates.
        public Quaterniond CameraFromBody { get; }
        public double RollRad { get; }
        public double PitchRad { get; }
        public double YawRad { get; }
        public CameraMount(double rollRad, double pitchRad, double yawRad)
        {
            if (!Finite(rollRad) || !Finite(pitchRad) || !Finite(yawRad)) throw new ArgumentException("Camera mount angles must be finite.");
            RollRad = rollRad; PitchRad = pitchRad; YawRad = yawRad;
            CameraFromBody = Quaterniond.FromEuler(rollRad, pitchRad, yawRad);
        }
        public Vector3d BodyRateToCamera(Vector3d bodyRate) => CameraFromBody.Rotate(bodyRate);
        public Quaterniond CameraToWorld(Quaterniond bodyToNed) => (bodyToNed * CameraFromBody.Inverse()).Normalized();
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
