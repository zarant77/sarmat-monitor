using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using OpenCvSharp;
using SarmatVisionHold.ReplayAnalyzer.Input;

namespace SarmatVisionHold.ReplayAnalyzer.Tests
{
    internal static class Program
    {
        static int Main()
        {
            var root = Path.Combine(Path.GetTempPath(), "sarmat-replay-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try { TlogFramingAndDecode(root); Console.WriteLine("PASS TlogFramingAndDecode"); VideoTimestampsAndDuplicates(root); Console.WriteLine("PASS VideoTimestampsAndDuplicates"); EndToEndDiagnosticsOnly(root); Console.WriteLine("PASS EndToEndDiagnosticsOnly"); Console.WriteLine("All 3 replay analyzer integration groups passed."); return 0; }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        static void TlogFramingAndDecode(string root)
        {
            var path = Path.Combine(root, "synthetic.tlog"); var bytes = new List<byte>(); var writer = new MAVLink.MavlinkParse(false);
            for (var i = 0; i < 3; i++)
            {
                var timestamp = 1700000000000000UL + (ulong)i * 20000;
                var attitude = new MAVLink.mavlink_attitude_t { time_boot_ms = (uint)(i * 20), roll = i * .01f, pitch = i * -.02f, yaw = i * .03f, rollspeed = .1f, pitchspeed = .2f, yawspeed = .3f };
                Add(bytes, timestamp, writer.GenerateMAVLinkPacket20(MAVLink.MAVLINK_MSG_ID.ATTITUDE, attitude, false, 1, 1, i));
                var range = new MAVLink.mavlink_distance_sensor_t { time_boot_ms = (uint)(i * 20), min_distance = 10, max_distance = 1000, current_distance = (ushort)(200 + i), orientation = 25 };
                Add(bytes, timestamp + 1000, writer.GenerateMAVLinkPacket20(MAVLink.MAVLINK_MSG_ID.DISTANCE_SENSOR, range, false, 1, 1, i + 10));
            }
            File.WriteAllBytes(path, bytes.ToArray()); var archive = new TlogReplayReader().Read(path);
            Eq(3, archive.Attitudes.Count); True(archive.Gyros.Count >= 3); Eq(3, archive.Altitudes.Count); True(archive.DurationSeconds >= .04 && archive.DurationSeconds < .05); Eq("ATTITUDE", archive.Attitudes[0].Source); Eq("DISTANCE_SENSOR", archive.Altitudes[0].Source); Near(2, archive.Altitudes[0].Meters, 1e-6); Eq(0, archive.BadPackets);
        }

        static void VideoTimestampsAndDuplicates(string root)
        {
            var path = Path.Combine(root, "synthetic.avi");
            using (var writer = new VideoWriter(path, FourCC.MJPG, 20, new Size(160, 120)))
            {
                True(writer.IsOpened(), "test video writer unavailable");
                using (var first = new Mat(120, 160, MatType.CV_8UC3, new Scalar(20, 40, 60))) { writer.Write(first); writer.Write(first); }
                using (var second = new Mat(120, 160, MatType.CV_8UC3, new Scalar(80, 20, 10))) { writer.Write(second); writer.Write(second); }
            }
            using (var reader = new VideoReplayReader(path, 0, -1, .25))
            {
                var timestamps = new List<double>(); VideoReplayFrame frame; while (reader.TryRead(out frame)) using (frame) timestamps.Add(frame.TimestampSeconds);
                Eq(4, timestamps.Count); True(reader.Metadata.DuplicateFrames >= 2); for (var i = 1; i < timestamps.Count; i++) True(timestamps[i] > timestamps[i - 1]); Eq(0L, reader.Metadata.TimestampRollbacks);
            }
        }

        static void EndToEndDiagnosticsOnly(string root)
        {
            var video = Path.Combine(root, "flight.avi"); var tlog = Path.Combine(root, "flight.tlog"); var output = Path.Combine(root, "output"); const int frames = 40;
            using (var writer = new VideoWriter(video, FourCC.MJPG, 20, new Size(160, 120)))
            using (var texture = new Mat(120, 160, MatType.CV_8UC3))
            {
                True(writer.IsOpened()); Cv2.Randu(texture, Scalar.All(0), Scalar.All(255));
                for (var i = 0; i < frames; i++) using (var shifted = new Mat()) using (var transform = new Mat(2, 3, MatType.CV_64FC1))
                {
                    transform.Set(0, 0, 1d); transform.Set(0, 1, 0d); transform.Set(0, 2, i * .35);
                    transform.Set(1, 0, 0d); transform.Set(1, 1, 1d); transform.Set(1, 2, -i * .2);
                    Cv2.WarpAffine(texture, shifted, transform, texture.Size(), InterpolationFlags.Linear, BorderTypes.Reflect101); writer.Write(shifted);
                }
            }
            var bytes = new List<byte>(); var mav = new MAVLink.MavlinkParse(false); const ulong epoch = 1700000100000000UL;
            for (var i = 0; i < frames + 2; i++)
            {
                var stamp = epoch + (ulong)i * 50000; var boot = (uint)(i * 50);
                Add(bytes, stamp, mav.GenerateMAVLinkPacket20(MAVLink.MAVLINK_MSG_ID.ATTITUDE, new MAVLink.mavlink_attitude_t { time_boot_ms = boot, roll = 0, pitch = 0, yaw = 0, rollspeed = 0, pitchspeed = 0, yawspeed = 0 }, false, 1, 1, i));
                Add(bytes, stamp + 500, mav.GenerateMAVLinkPacket20(MAVLink.MAVLINK_MSG_ID.HIGHRES_IMU, new MAVLink.mavlink_highres_imu_t { time_usec = (ulong)boot * 1000, xgyro = 0, ygyro = 0, zgyro = 0 }, false, 1, 1, i + 60));
                Add(bytes, stamp + 1000, mav.GenerateMAVLinkPacket20(MAVLink.MAVLINK_MSG_ID.DISTANCE_SENSOR, new MAVLink.mavlink_distance_sensor_t { time_boot_ms = boot, min_distance = 10, max_distance = 1000, current_distance = 250, orientation = 25 }, false, 1, 1, i + 120));
            }
            File.WriteAllBytes(tlog, bytes.ToArray());
            var executable = typeof(TlogReplayReader).Assembly.Location;
            var start = new ProcessStartInfo(executable, $"--video \"{video}\" --tlog \"{tlog}\" --output \"{output}\" --horizontal-fov 90 --camera-mount-pitch -90 --headless") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using (var process = Process.Start(start))
            {
                var stdout = process.StandardOutput.ReadToEnd(); var stderr = process.StandardError.ReadToEnd(); True(process.WaitForExit(60000), "ReplayAnalyzer timeout"); Eq(0, process.ExitCode); True(stdout.Contains("DiagnosticsOnly=true")); True(stdout.Contains("No MAVLink messages were transmitted"), stdout + stderr);
            }
            foreach (var name in new[] { "replay.csv", "optical-flow-rad.csv", "report.md", "synchronization.json", "config-resolved.json" }) True(File.Exists(Path.Combine(output, name)), "missing " + name);
            True(File.ReadAllLines(Path.Combine(output, "replay.csv")).Length >= frames); True(File.ReadAllText(Path.Combine(output, "report.md")).Contains("No MAVLink data was transmitted"));
        }

        static void Add(List<byte> destination, ulong microsecondsSinceUnix, byte[] packet)
        {
            var prefix = BitConverter.GetBytes(microsecondsSinceUnix); if (BitConverter.IsLittleEndian) Array.Reverse(prefix); destination.AddRange(prefix); destination.AddRange(packet);
        }
        static void True(bool value, string message = null) { if (!value) throw new Exception(message ?? "expected true"); }
        static void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"expected {expected}, got {actual}"); }
        static void Near(double expected, double actual, double epsilon) { if (System.Math.Abs(expected - actual) > epsilon) throw new Exception($"expected {expected}, got {actual}"); }
    }
}
