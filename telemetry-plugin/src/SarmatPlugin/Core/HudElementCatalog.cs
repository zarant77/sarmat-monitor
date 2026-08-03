using System.Collections.Generic;

namespace SarmatPlugin.Core
{
    public static class HudElementCatalog
    {
        public static readonly IReadOnlyDictionary<string, string> Elements =
            new Dictionary<string, string>
            {
                ["batteryon"] = "Battery indicator 1",
                ["batteryon2"] = "Battery indicator 2",
                ["displayCellVoltage"] = "Battery cell voltage",
                ["displayalt"] = "Altitude",
                ["displayspeed"] = "Speed",
                ["displayheading"] = "Heading",
                ["displayrollpitch"] = "Roll and pitch",
                ["displayxtrack"] = "Cross-track error",
                ["displaygps"] = "GPS status",
                ["displayekf"] = "EKF status",
                ["displayvibe"] = "Vibration status",
                ["displayprearm"] = "Pre-arm status",
                ["displayconninfo"] = "Connection information",
                ["displayAOASSA"] = "AOA / SSA",
                ["displayicons"] = "Use icons instead of text"
            };
    }
}
