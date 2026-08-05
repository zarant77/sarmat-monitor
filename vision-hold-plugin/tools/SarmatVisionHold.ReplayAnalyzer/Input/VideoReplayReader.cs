using System;
using OpenCvSharp;

namespace SarmatVisionHold.ReplayAnalyzer.Input
{
    public sealed class VideoReplayFrame : IDisposable
    {
        public long Index;
        public double TimestampSeconds, DeltaSeconds;
        public bool Duplicate, Gap, TimestampRollback, UsedNominalTimestamp;
        public Mat Image;
        public void Dispose() { Image?.Dispose(); Image = null; }
    }
    public sealed class VideoReplayMetadata
    {
        public int Width, Height;
        public double NominalFps, ContainerFrameCount;
        public long DecodedFrames, DuplicateFrames, GapFrames, TimestampRollbacks, DecoderErrors;
        public double DurationSeconds;
    }

    public sealed class VideoReplayReader : IDisposable
    {
        readonly VideoCapture capture;
        readonly double start, duration, gapThreshold;
        Mat previous;
        long index;
        double previousTimestamp = double.NaN;
        public VideoReplayMetadata Metadata { get; }

        public VideoReplayReader(string path, double startSeconds, double durationSeconds, double gapResetSeconds)
        {
            capture = new VideoCapture(path);
            if (!capture.IsOpened()) throw new InvalidOperationException("OpenCV could not open video: " + path);
            start = System.Math.Max(0, startSeconds); duration = durationSeconds; gapThreshold = gapResetSeconds;
            Metadata = new VideoReplayMetadata { Width = (int)capture.FrameWidth, Height = (int)capture.FrameHeight, NominalFps = Positive(capture.Fps, 25), ContainerFrameCount = capture.FrameCount };
            if (Metadata.Width <= 0 || Metadata.Height <= 0) throw new InvalidOperationException("Video has invalid dimensions.");
            if (start > 0) capture.PosMsec = (int)System.Math.Round(start * 1000);
        }

        public bool TryRead(out VideoReplayFrame result)
        {
            result = null; var frame = new Mat(); bool read;
            try { read = capture.Read(frame); }
            catch { Metadata.DecoderErrors++; frame.Dispose(); return false; }
            if (!read || frame.Empty()) { frame.Dispose(); return false; }
            var containerTimestamp = capture.PosMsec / 1000d;
            var nominalTimestamp = start + index / Metadata.NominalFps;
            var timestamp = Finite(containerTimestamp) && containerTimestamp >= 0 ? containerTimestamp : nominalTimestamp;
            var fallback = !Finite(containerTimestamp) || containerTimestamp < 0;
            var rollback = Finite(previousTimestamp) && timestamp <= previousTimestamp;
            if (rollback)
            {
                Metadata.TimestampRollbacks++;
                if (System.Math.Abs(timestamp - previousTimestamp) < 1e-6) { timestamp = previousTimestamp + 1 / Metadata.NominalFps; fallback = true; }
            }
            if (duration >= 0 && timestamp > start + duration) { frame.Dispose(); return false; }
            var dt = Finite(previousTimestamp) ? timestamp - previousTimestamp : 0;
            var gap = dt > System.Math.Max(gapThreshold, 3 / Metadata.NominalFps);
            var duplicate = previous != null && previous.Size() == frame.Size() && previous.Type() == frame.Type() && Cv2.Norm(previous, frame, NormTypes.L1) == 0;
            if (gap) Metadata.GapFrames++; if (duplicate) Metadata.DuplicateFrames++;
            previous?.Dispose(); previous = frame.Clone(); previousTimestamp = timestamp;
            result = new VideoReplayFrame { Index = index++, TimestampSeconds = timestamp, DeltaSeconds = dt, Duplicate = duplicate, Gap = gap, TimestampRollback = rollback, UsedNominalTimestamp = fallback, Image = frame };
            Metadata.DecodedFrames++; Metadata.DurationSeconds = timestamp - start; return true;
        }
        public void Dispose() { previous?.Dispose(); capture.Dispose(); }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static double Positive(double value, double fallback) => Finite(value) && value > 0 ? value : fallback;
    }
}
