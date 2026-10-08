namespace SarmatAltitude.Core
{
    public static class GuidedAltitudeCommand
    {
        public static MAVLink.mavlink_set_position_target_global_int_t Create(byte systemId, byte componentId,
            double latitude, double longitude, double altitudeMeters)
        {
            return new MAVLink.mavlink_set_position_target_global_int_t
            {
                target_system = systemId, target_component = componentId,
                coordinate_frame = (byte)MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT,
                // Use all three position axes; ignore velocity, acceleration, yaw and yaw rate.
                type_mask = 0x0DF8,
                lat_int = (int)System.Math.Round(latitude * 1e7),
                lon_int = (int)System.Math.Round(longitude * 1e7),
                alt = (float)altitudeMeters
            };
        }
    }
}
