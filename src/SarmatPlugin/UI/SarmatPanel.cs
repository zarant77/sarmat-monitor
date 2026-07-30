using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SarmatPlugin.Core;

namespace SarmatPlugin.UI
{
    public sealed class SarmatPanel : UserControl
    {
        private readonly Label satCount = MetricValue();
        private readonly Label gpsHdop = MetricValue();
        private readonly Label distanceToHome = MetricValue();
        private readonly Label batteryUsed = MetricValue();
        private readonly Label ruijie = StatusValue();
        private readonly Label obs = StatusValue();
        private readonly List<Label> headers = new List<Label>();
        private readonly List<TableLayoutPanel> metricTiles = new List<TableLayoutPanel>();
        private readonly TableLayoutPanel layout;
        private readonly Control satTile;
        private readonly Control hdopTile;
        private readonly Control distanceTile;
        private readonly Control batteryTile;
        private readonly Control ruijieTile;
        private readonly Control obsTile;
        private readonly ToolTip tooltip = new ToolTip();
        private float appliedHeaderFontSize;
        private float appliedValueFontSize;
        private ResponsiveMode responsiveMode = (ResponsiveMode)(-1);
        public event EventHandler SettingsRequested;
        public event EventHandler VideoSourceRequested;

        public SarmatPanel()
        {
            Name = "SarmatPluginPanel";
            Width = 520; Height = 132; MinimumSize = new Size(180, 100);
            BackColor = Color.FromArgb(34, 38, 42); ForeColor = Color.White;
            layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4), Margin = new Padding(0) };
            satTile = Metric("Sat Count", satCount);
            hdopTile = Metric("GPS HDOP", gpsHdop);
            distanceTile = Metric("Dist to Home", distanceToHome);
            batteryTile = Metric("Bat used", batteryUsed);
            ruijieTile = Metric("Ruijie", ruijie);
            obsTile = Metric("OBS", obs);
            Controls.Add(layout);

            var menu = new ContextMenuStrip();
            menu.Items.Add("Settings", null, (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Start Sarmat RTSP video", null,
                (s, e) => VideoSourceRequested?.Invoke(this, EventArgs.Empty));
            UpdateResponsiveLayout();
            ApplyContextMenu(this, menu);
            SizeChanged += (s, e) => UpdateResponsiveLayout();
        }

        public void Render(TelemetrySnapshot telemetry, ObsStatus obsStatus, RuijieStatus ruijieStatus,
            SafetySnapshot snapshot, PluginSettings settings)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => Render(telemetry, obsStatus, ruijieStatus, snapshot, settings)));
                return;
            }
            ApplyFontSettings(settings);
            satCount.Text = telemetry.Satellites.ToString("0");
            gpsHdop.Text = telemetry.Hdop.ToString("0.00");
            distanceToHome.Text = telemetry.DistanceToHomeMeters.ToString("0");
            batteryUsed.Text = telemetry.BatteryUsedMah.ToString("0");
            satCount.ForeColor = telemetry.Satellites >= settings.MinimumSatellites ? Color.LimeGreen : Color.OrangeRed;
            gpsHdop.ForeColor = telemetry.Hdop <= settings.MaximumHdop ? Color.LimeGreen : Color.OrangeRed;
            distanceToHome.ForeColor = DistanceColor(telemetry.DistanceToHomeMeters, settings.SafeDistanceToHomeMeters);
            batteryUsed.ForeColor = Color.Gold;
            var age = ruijieStatus.LastSuccessUtc == default ? "never" :
                Math.Max(0, (DateTime.UtcNow - ruijieStatus.LastSuccessUtc).TotalSeconds).ToString("0") + "s";
            if (!ruijieStatus.Connected || ruijieStatus.Stale || !ruijieStatus.Rssi.HasValue)
            {
                ruijie.Text = "Disconnected";
                ruijie.ForeColor = Color.OrangeRed;
            }
            else
            {
                ruijie.Text = $"{ruijieStatus.Rssi.Value} dBm";
                ruijie.ForeColor = RuijieColor(ruijieStatus);
            }
            if (!obsStatus.Connected)
            {
                obs.Text = "Disconnected";
                obs.ForeColor = Color.OrangeRed;
            }
            else if (obsStatus.Recording == true)
            {
                obs.Text = "Recording";
                obs.ForeColor = Color.LimeGreen;
            }
            else
            {
                obs.Text = "Not recording";
                obs.ForeColor = Color.OrangeRed;
            }
            var details = new StringBuilder()
                .AppendLine($"Battery: {telemetry.BatteryVoltage:0.0} V")
                .AppendLine($"Satellites: {telemetry.Satellites}")
                .AppendLine($"HDOP: {telemetry.Hdop:0.00}")
                .AppendLine($"Distance to home: {telemetry.DistanceToHomeMeters:0} m")
                .AppendLine($"Battery used estimate: {telemetry.BatteryUsedMah:0} mAh")
                .AppendLine($"Ruijie: {ruijieStatus.Error ?? "OK"}")
                .AppendLine($"Ruijie last update: {age}")
                .AppendLine($"OBS: {obsStatus.Error ?? "OK"}")
                .Append("Safety: ")
                .Append(snapshot.Reasons.Count == 0 ? snapshot.Severity.ToString() :
                    string.Join("; ", snapshot.Reasons.Select(x => x.Text))).ToString();
            tooltip.SetToolTip(this, details);
            tooltip.SetToolTip(ruijie, details);
            tooltip.SetToolTip(obs, details);
        }

        private static Label MetricValue() => new Label { Dock = DockStyle.Fill, ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 18, FontStyle.Bold) };
        private static Label StatusValue() => new Label { Dock = DockStyle.Fill, ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 15, FontStyle.Bold) };
        private Control Metric(string caption, Label value)
        {
            var tile = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(1) };
            metricTiles.Add(tile);
            tile.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            tile.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var header = new Label { Text = caption, Dock = DockStyle.Fill, ForeColor = Color.Silver,
                TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10, FontStyle.Bold) };
            headers.Add(header);
            tile.Controls.Add(header, 0, 0);
            tile.Controls.Add(value, 0, 1);
            return tile;
        }
        private static Color RuijieColor(RuijieStatus status)
        {
            if (string.Equals(status.SignalQuality, "Bad", StringComparison.OrdinalIgnoreCase) ||
                status.Rssi <= -85) return Color.OrangeRed;
            if (string.Equals(status.SignalQuality, "Weak", StringComparison.OrdinalIgnoreCase) ||
                status.Rssi <= -75) return Color.Gold;
            return Color.LimeGreen;
        }
        private static Color DistanceColor(double distance, double safeDistance)
        {
            if (distance <= safeDistance / 2.0) return Color.LimeGreen;
            if (distance < safeDistance) return Color.Gold;
            return Color.OrangeRed;
        }
        private static void ApplyContextMenu(Control root, ContextMenuStrip menu)
        {
            root.ContextMenuStrip = menu;
            foreach (Control child in root.Controls)
                ApplyContextMenu(child, menu);
        }
        private void UpdateResponsiveLayout()
        {
            if (layout == null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            var ratio = (double)ClientSize.Width / Math.Max(1, ClientSize.Height);
            var next = ClientSize.Width >= 480 && ratio >= 1.8
                ? ResponsiveMode.Wide
                : ClientSize.Width >= 300 && ratio >= 0.9
                    ? ResponsiveMode.Medium
                    : ResponsiveMode.Narrow;
            if (next == responsiveMode) return;
            responsiveMode = next;

            layout.SuspendLayout();
            layout.Controls.Clear();
            layout.ColumnStyles.Clear();
            layout.RowStyles.Clear();
            switch (next)
            {
                case ResponsiveMode.Wide:
                    ConfigureGrid(4, 2);
                    Add(satTile, 0, 0); Add(hdopTile, 1, 0); Add(distanceTile, 2, 0); Add(batteryTile, 3, 0);
                    Add(ruijieTile, 0, 1, 2); Add(obsTile, 2, 1, 2);
                    break;
                case ResponsiveMode.Medium:
                    ConfigureGrid(2, 3);
                    Add(satTile, 0, 0); Add(hdopTile, 1, 0);
                    Add(distanceTile, 0, 1); Add(batteryTile, 1, 1);
                    Add(ruijieTile, 0, 2); Add(obsTile, 1, 2);
                    break;
                default:
                    ConfigureGrid(1, 6);
                    Add(satTile, 0, 0); Add(hdopTile, 0, 1); Add(distanceTile, 0, 2);
                    Add(batteryTile, 0, 3); Add(ruijieTile, 0, 4); Add(obsTile, 0, 5);
                    break;
            }
            layout.ResumeLayout(true);
        }
        private void ConfigureGrid(int columns, int rows)
        {
            layout.ColumnCount = columns;
            layout.RowCount = rows;
            for (var i = 0; i < columns; i++)
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            for (var i = 0; i < rows; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
        }
        private void Add(Control control, int column, int row, int columnSpan = 1)
        {
            layout.Controls.Add(control, column, row);
            if (columnSpan > 1) layout.SetColumnSpan(control, columnSpan);
        }
        private void ApplyFontSettings(PluginSettings settings)
        {
            var headerSize = (float)settings.HeaderFontSize;
            var valueSize = (float)settings.ValueFontSize;
            if (Math.Abs(appliedHeaderFontSize - headerSize) > 0.01f)
            {
                foreach (var header in headers)
                    header.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, headerSize, FontStyle.Bold);
                foreach (var tile in metricTiles)
                    tile.RowStyles[0].Height = Math.Max(18, headerSize * 1.8f);
                appliedHeaderFontSize = headerSize;
            }
            if (Math.Abs(appliedValueFontSize - valueSize) > 0.01f)
            {
                foreach (var value in new[] { satCount, gpsHdop, distanceToHome, batteryUsed, ruijie, obs })
                    value.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, valueSize, FontStyle.Bold);
                appliedValueFontSize = valueSize;
            }
        }
        private enum ResponsiveMode
        {
            Wide,
            Medium,
            Narrow
        }
    }
}
