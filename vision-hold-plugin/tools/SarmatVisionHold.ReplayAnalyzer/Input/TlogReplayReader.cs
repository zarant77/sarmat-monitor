using System;
using System.IO;
using SarmatVisionHold.Replay.Math;
using SarmatVisionHold.Replay.Telemetry;

namespace SarmatVisionHold.ReplayAnalyzer.Input
{
    public sealed class TlogReplayReader
    {
        DateTime firstReceiveUtc;
        ulong firstParserTimestamp;
        double baroBaseline = double.NaN;

        public ReplayTelemetryArchive Read(string path)
        {
            var archive = new ReplayTelemetryArchive { Path = Path.GetFullPath(path), Bytes = new FileInfo(path).Length };
            var parser = new MAVLink.MavlinkParse(true);
            using (var stream = File.OpenRead(path))
            {
                while (stream.Position < stream.Length)
                {
                    MAVLink.MAVLinkMessage packet;
                    try { packet = parser.ReadPacket(stream); }
                    catch { archive.BadPackets++; continue; }
                    if (packet == null || ReferenceEquals(packet, MAVLink.MAVLinkMessage.Invalid)) { archive.BadPackets++; continue; }
                    var time = TimelineTime(packet, parser.lasttimestamp); var receive = ValidDate(packet.rxtime) ? packet.rxtime.ToUniversalTime() : DateTime.MinValue;
                    archive.Count(packet.msgtypename);
                    try { Decode(packet, time, receive, archive); } catch { archive.BadPackets++; }
                }
            }
            archive.Sort(); return archive;
        }

        double TimelineTime(MAVLink.MAVLinkMessage packet, ulong parserTimestamp)
        {
            if (ValidDate(packet.rxtime))
            {
                var utc = packet.rxtime.ToUniversalTime(); if (firstReceiveUtc == default(DateTime)) firstReceiveUtc = utc;
                return System.Math.Max(0, (utc - firstReceiveUtc).TotalSeconds);
            }
            if (parserTimestamp > 0)
            {
                if (firstParserTimestamp == 0) firstParserTimestamp = parserTimestamp;
                return parserTimestamp >= firstParserTimestamp ? (parserTimestamp - firstParserTimestamp) / 1e6 : 0;
            }
            return 0;
        }

        void Decode(MAVLink.MAVLinkMessage packet, double time, DateTime receive, ReplayTelemetryArchive a)
        {
            switch ((MAVLink.MAVLINK_MSG_ID)packet.msgid)
            {
                case MAVLink.MAVLINK_MSG_ID.ATTITUDE_QUATERNION:
                    var aq = packet.ToStructure<MAVLink.mavlink_attitude_quaternion_t>();
                    a.Attitudes.Add(new TimedAttitude { TimeSeconds = time, BootTimeSeconds = aq.time_boot_ms / 1000d, ReceiveTimeUtc = receive, BodyToNed = new Quaterniond(aq.q1, aq.q2, aq.q3, aq.q4).Normalized(), IsQuaternion = true, Source = "ATTITUDE_QUATERNION" });
                    a.Gyros.Add(Gyro(time, aq.time_boot_ms / 1000d, receive, aq.rollspeed, aq.pitchspeed, aq.yawspeed, "ATTITUDE")); break;
                case MAVLink.MAVLINK_MSG_ID.ATTITUDE_QUATERNION_COV:
                    var aqc = packet.ToStructure<MAVLink.mavlink_attitude_quaternion_cov_t>();
                    if (aqc.q != null && aqc.q.Length >= 4) a.Attitudes.Add(new TimedAttitude { TimeSeconds = time, BootTimeSeconds = Boot(aqc.time_usec), ReceiveTimeUtc = receive, BodyToNed = new Quaterniond(aqc.q[0], aqc.q[1], aqc.q[2], aqc.q[3]).Normalized(), IsQuaternion = true, Source = "ATTITUDE_QUATERNION_COV" });
                    a.Gyros.Add(Gyro(time, Boot(aqc.time_usec), receive, aqc.rollspeed, aqc.pitchspeed, aqc.yawspeed, "ATTITUDE")); break;
                case MAVLink.MAVLINK_MSG_ID.ATTITUDE:
                    var at = packet.ToStructure<MAVLink.mavlink_attitude_t>();
                    a.Attitudes.Add(new TimedAttitude { TimeSeconds = time, BootTimeSeconds = at.time_boot_ms / 1000d, ReceiveTimeUtc = receive, BodyToNed = Quaterniond.FromEuler(at.roll, at.pitch, at.yaw), IsQuaternion = false, Source = "ATTITUDE" });
                    a.Gyros.Add(Gyro(time, at.time_boot_ms / 1000d, receive, at.rollspeed, at.pitchspeed, at.yawspeed, "ATTITUDE")); break;
                case MAVLink.MAVLINK_MSG_ID.HIGHRES_IMU:
                    var hi = packet.ToStructure<MAVLink.mavlink_highres_imu_t>(); a.Gyros.Add(Gyro(time, Boot(hi.time_usec), receive, hi.xgyro, hi.ygyro, hi.zgyro, "HIGHRES_IMU"));
                    if (Finite(hi.pressure_alt)) AddBaro(a, time, receive, hi.pressure_alt); break;
                case MAVLink.MAVLINK_MSG_ID.SCALED_IMU: AddScaled(packet.ToStructure<MAVLink.mavlink_scaled_imu_t>(), time, receive, a); break;
                case MAVLink.MAVLINK_MSG_ID.SCALED_IMU2:
                    var s2 = packet.ToStructure<MAVLink.mavlink_scaled_imu2_t>(); a.Gyros.Add(Gyro(time, s2.time_boot_ms / 1000d, receive, s2.xgyro * .001, s2.ygyro * .001, s2.zgyro * .001, "SCALED_IMU2")); break;
                case MAVLink.MAVLINK_MSG_ID.SCALED_IMU3:
                    var s3 = packet.ToStructure<MAVLink.mavlink_scaled_imu3_t>(); a.Gyros.Add(Gyro(time, s3.time_boot_ms / 1000d, receive, s3.xgyro * .001, s3.ygyro * .001, s3.zgyro * .001, "SCALED_IMU3")); break;
                case MAVLink.MAVLINK_MSG_ID.RAW_IMU:
                    var raw = packet.ToStructure<MAVLink.mavlink_raw_imu_t>(); a.Gyros.Add(Gyro(time, Boot(raw.time_usec), receive, raw.xgyro * .001, raw.ygyro * .001, raw.zgyro * .001, "RAW_IMU")); break;
                case MAVLink.MAVLINK_MSG_ID.DISTANCE_SENSOR:
                    var distance = packet.ToStructure<MAVLink.mavlink_distance_sensor_t>();
                    if (distance.orientation == 25 && distance.current_distance >= distance.min_distance && distance.current_distance <= distance.max_distance) a.Altitudes.Add(Altitude(time, receive, distance.current_distance / 100d, "DISTANCE_SENSOR")); break;
                case MAVLink.MAVLINK_MSG_ID.RANGEFINDER:
                    var range = packet.ToStructure<MAVLink.mavlink_rangefinder_t>(); a.Altitudes.Add(Altitude(time, receive, range.distance, "RANGEFINDER")); break;
                case MAVLink.MAVLINK_MSG_ID.ALTITUDE:
                    var alt = packet.ToStructure<MAVLink.mavlink_altitude_t>();
                    if (Finite(alt.bottom_clearance) && alt.bottom_clearance > 0) a.Altitudes.Add(Altitude(time, receive, alt.bottom_clearance, "TERRAIN"));
                    if (Finite(alt.altitude_relative) && alt.altitude_relative > 0) a.Altitudes.Add(Altitude(time, receive, alt.altitude_relative, "RELATIVE")); break;
                case MAVLink.MAVLINK_MSG_ID.GLOBAL_POSITION_INT:
                    var gp = packet.ToStructure<MAVLink.mavlink_global_position_int_t>(); a.Altitudes.Add(Altitude(time, receive, gp.relative_alt / 1000d, "RELATIVE")); a.Velocities.Add(new TimedVelocity { TimeSeconds = time, NedMetersPerSecond = new Vector3d(gp.vx / 100d, gp.vy / 100d, gp.vz / 100d), Source = "GLOBAL_POSITION_INT" }); break;
                case MAVLink.MAVLINK_MSG_ID.LOCAL_POSITION_NED:
                    var lp = packet.ToStructure<MAVLink.mavlink_local_position_ned_t>(); if (-lp.z > 0) a.Altitudes.Add(Altitude(time, receive, -lp.z, "RELATIVE")); a.Velocities.Add(new TimedVelocity { TimeSeconds = time, NedMetersPerSecond = new Vector3d(lp.vx, lp.vy, lp.vz), Source = "LOCAL_POSITION_NED" }); break;
                case MAVLink.MAVLINK_MSG_ID.VFR_HUD:
                    var hud = packet.ToStructure<MAVLink.mavlink_vfr_hud_t>(); AddBaro(a, time, receive, hud.alt); break;
                case MAVLink.MAVLINK_MSG_ID.HEARTBEAT:
                    var hb = packet.ToStructure<MAVLink.mavlink_heartbeat_t>(); a.States.Add(new TimedVehicleState { TimeSeconds = time, Armed = (hb.base_mode & 128) != 0, CustomMode = hb.custom_mode }); break;
                case MAVLink.MAVLINK_MSG_ID.RC_CHANNELS:
                    var rc = packet.ToStructure<MAVLink.mavlink_rc_channels_t>(); a.States.Add(new TimedVehicleState { TimeSeconds = time, RcChannels = new[] { rc.chan1_raw, rc.chan2_raw, rc.chan3_raw, rc.chan4_raw, rc.chan5_raw, rc.chan6_raw, rc.chan7_raw, rc.chan8_raw, rc.chan9_raw, rc.chan10_raw, rc.chan11_raw, rc.chan12_raw, rc.chan13_raw, rc.chan14_raw, rc.chan15_raw, rc.chan16_raw, rc.chan17_raw, rc.chan18_raw } }); break;
            }
        }

        void AddScaled(MAVLink.mavlink_scaled_imu_t value, double time, DateTime receive, ReplayTelemetryArchive a) => a.Gyros.Add(Gyro(time, value.time_boot_ms / 1000d, receive, value.xgyro * .001, value.ygyro * .001, value.zgyro * .001, "SCALED_IMU"));
        void AddBaro(ReplayTelemetryArchive a, double time, DateTime receive, double value) { if (!Finite(value)) return; if (!Finite(baroBaseline)) baroBaseline = value; var delta = value - baroBaseline; if (delta >= 0) a.Altitudes.Add(Altitude(time, receive, delta, "BARO_DELTA")); }
        static TimedGyro Gyro(double time, double boot, DateTime receive, double x, double y, double z, string source) => new TimedGyro { TimeSeconds = time, BootTimeSeconds = boot, ReceiveTimeUtc = receive, BodyRateRadPerSecond = new Vector3d(x, y, z), Source = source };
        static TimedAltitude Altitude(double time, DateTime receive, double meters, string source) => new TimedAltitude { TimeSeconds = time, ReceiveTimeUtc = receive, Meters = meters, Valid = Finite(meters) && meters > 0, Source = source };
        static double Boot(ulong usec) => usec > 100000000000000UL ? 0 : usec / 1e6;
        static bool ValidDate(DateTime value) => value.Year >= 2000 && value.Year < 2200;
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
