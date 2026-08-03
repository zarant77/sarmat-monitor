namespace SarmatPlugin.Core
{
    public static class TelemetryStatusPolicy
    {
        public static WidgetStatus Voltage(double value) => Minimum(value,
            TelemetryThresholds.Current.Voltage.Good, TelemetryThresholds.Current.Voltage.Normal);
        public static WidgetStatus Current(double value) => Maximum(value,
            TelemetryThresholds.Current.Current.Good, TelemetryThresholds.Current.Current.Normal);
        public static WidgetStatus Satellites(double value) => Minimum(value,
            TelemetryThresholds.Current.Satellites.Good, TelemetryThresholds.Current.Satellites.Normal);
        public static WidgetStatus Hdop(double value) => Maximum(value,
            TelemetryThresholds.Current.Hdop.Good, TelemetryThresholds.Current.Hdop.Normal);
        public static WidgetStatus LinkRssi(double value) => Minimum(value,
            TelemetryThresholds.Current.LinkRssi.Good, TelemetryThresholds.Current.LinkRssi.Normal);
        public static WidgetStatus DistanceToHome(double value) => Maximum(value,
            TelemetryThresholds.Current.DistanceToHome.Good,
            TelemetryThresholds.Current.DistanceToHome.Normal);

        public static WidgetStatus Minimum(double value, double goodMin, double normalMin)
        {
            if (value >= goodMin) return WidgetStatus.Good;
            return value >= normalMin ? WidgetStatus.Normal : WidgetStatus.Bad;
        }

        public static WidgetStatus Maximum(double value, double goodMax, double normalMax)
        {
            if (value <= goodMax) return WidgetStatus.Good;
            return value <= normalMax ? WidgetStatus.Normal : WidgetStatus.Bad;
        }
    }
}
