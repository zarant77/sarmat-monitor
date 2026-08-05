using System;

namespace SarmatVisionHold.Replay.Math
{
    public struct Vector3d
    {
        public double X, Y, Z;
        public Vector3d(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double Length => System.Math.Sqrt(X * X + Y * Y + Z * Z);
        public bool IsFinite => Finite(X) && Finite(Y) && Finite(Z);
        public Vector3d Normalized()
        {
            var length = Length;
            return length > 1e-12 && Finite(length) ? new Vector3d(X / length, Y / length, Z / length) : new Vector3d();
        }
        public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3d operator *(Vector3d a, double b) => new Vector3d(a.X * b, a.Y * b, a.Z * b);
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public struct Quaterniond
    {
        public double W, X, Y, Z;
        public Quaterniond(double w, double x, double y, double z) { W = w; X = x; Y = y; Z = z; }
        public static Quaterniond Identity => new Quaterniond(1, 0, 0, 0);
        public bool IsFinite => Finite(W) && Finite(X) && Finite(Y) && Finite(Z);
        public Quaterniond Normalized()
        {
            var n = System.Math.Sqrt(W * W + X * X + Y * Y + Z * Z);
            return n > 1e-12 && Finite(n) ? new Quaterniond(W / n, X / n, Y / n, Z / n) : Identity;
        }
        public Quaterniond Inverse()
        {
            var n = W * W + X * X + Y * Y + Z * Z;
            return n > 1e-12 && Finite(n) ? new Quaterniond(W / n, -X / n, -Y / n, -Z / n) : Identity;
        }
        public Vector3d Rotate(Vector3d value)
        {
            var q = Normalized();
            var p = new Quaterniond(0, value.X, value.Y, value.Z);
            var r = q * p * q.Inverse();
            return new Vector3d(r.X, r.Y, r.Z);
        }
        public static Quaterniond operator *(Quaterniond a, Quaterniond b) => new Quaterniond(
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);

        // Aerospace roll/pitch/yaw: intrinsic X/Y/Z, equivalent to qYaw * qPitch * qRoll.
        public static Quaterniond FromEuler(double roll, double pitch, double yaw)
        {
            var cr = System.Math.Cos(roll / 2); var sr = System.Math.Sin(roll / 2);
            var cp = System.Math.Cos(pitch / 2); var sp = System.Math.Sin(pitch / 2);
            var cy = System.Math.Cos(yaw / 2); var sy = System.Math.Sin(yaw / 2);
            return new Quaterniond(
                cy * cp * cr + sy * sp * sr,
                cy * cp * sr - sy * sp * cr,
                sy * cp * sr + cy * sp * cr,
                sy * cp * cr - cy * sp * sr).Normalized();
        }

        public static Quaterniond FromRotationVector(Vector3d radians)
        {
            var angle = radians.Length;
            if (!radians.IsFinite || angle < 1e-12) return Identity;
            var axis = radians * (1 / angle);
            var s = System.Math.Sin(angle / 2);
            return new Quaterniond(System.Math.Cos(angle / 2), axis.X * s, axis.Y * s, axis.Z * s).Normalized();
        }

        public static Quaterniond Slerp(Quaterniond a, Quaterniond b, double t)
        {
            a = a.Normalized(); b = b.Normalized(); t = Clamp(t, 0, 1);
            var dot = a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            if (dot < 0) { b = new Quaterniond(-b.W, -b.X, -b.Y, -b.Z); dot = -dot; }
            if (dot > .9995)
                return new Quaterniond(a.W + (b.W - a.W) * t, a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t).Normalized();
            var theta = System.Math.Acos(Clamp(dot, -1, 1));
            var sin = System.Math.Sin(theta);
            var wa = System.Math.Sin((1 - t) * theta) / sin;
            var wb = System.Math.Sin(t * theta) / sin;
            return new Quaterniond(a.W * wa + b.W * wb, a.X * wa + b.X * wb, a.Y * wa + b.Y * wb, a.Z * wa + b.Z * wb).Normalized();
        }

        public Vector3d ToEuler()
        {
            var q = Normalized();
            var roll = System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
            var sinPitch = 2 * (q.W * q.Y - q.Z * q.X);
            var pitch = System.Math.Abs(sinPitch) >= 1 ? (sinPitch < 0 ? -System.Math.PI / 2 : System.Math.PI / 2) : System.Math.Asin(sinPitch);
            var yaw = System.Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
            return new Vector3d(roll, pitch, yaw);
        }
        static double Clamp(double v, double lo, double hi) => System.Math.Max(lo, System.Math.Min(hi, v));
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public static class ReplayStatistics
    {
        public static double Median(double[] values)
        {
            if (values == null || values.Length == 0) return 0;
            var finite = Array.FindAll(values, v => !double.IsNaN(v) && !double.IsInfinity(v));
            if (finite.Length == 0) return 0;
            Array.Sort(finite);
            var m = finite.Length / 2;
            return finite.Length % 2 == 0 ? (finite[m - 1] + finite[m]) / 2 : finite[m];
        }
        public static double Percentile(double[] values, double p)
        {
            if (values == null || values.Length == 0) return 0;
            var copy = Array.FindAll(values, v => !double.IsNaN(v) && !double.IsInfinity(v));
            if (copy.Length == 0) return 0;
            Array.Sort(copy); p = System.Math.Max(0, System.Math.Min(1, p));
            var at = (copy.Length - 1) * p; var lo = (int)at; var hi = (int)System.Math.Ceiling(at);
            return copy[lo] + (copy[hi] - copy[lo]) * (at - lo);
        }
    }
}
