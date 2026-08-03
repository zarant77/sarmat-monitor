using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SarmatPlugin.Core;

namespace SarmatPlugin.UI
{
    public sealed class SarmatPanel : UserControl
    {
        private readonly BufferedTableLayoutPanel grid;
        private readonly Dictionary<string, TelemetryWidget> widgets;
        private string enabledSignature;
        private bool hasTelemetryContent;

        public event EventHandler SettingsRequested;
        public event EventHandler VideoSourceRequested;
        public event EventHandler VehicleReconnectRequested;

        public SarmatPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint, true);
            Name = "SarmatPluginPanel";
            Width = 520;
            Height = 180;
            MinimumSize = new Size(160, 100);
            BackColor = Color.FromArgb(34, 38, 42);
            ForeColor = Color.White;

            grid = new BufferedTableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1,
                Padding = new Padding(3),
                Margin = new Padding(0),
                BackColor = BackColor
            };
            widgets = WidgetCatalog.Definitions.ToDictionary(x => x.Id, x => new TelemetryWidget(),
                StringComparer.OrdinalIgnoreCase);
            Controls.Add(grid);

            var menu = new ContextMenuStrip();
            menu.Items.Add("Settings", null, (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Reconnect camera", null,
                (s, e) => VideoSourceRequested?.Invoke(this, EventArgs.Empty));
            menu.Items.Add("Reconnect drone", null,
                (s, e) => VehicleReconnectRequested?.Invoke(this, EventArgs.Empty));
            ApplyContextMenu(this, menu);
            foreach (var widget in widgets.Values) ApplyContextMenu(widget, menu);

            SizeChanged += (s, e) => ArrangeWidgets();
            grid.SizeChanged += (s, e) => ArrangeWidgets();
            HandleCreated += (s, e) => ScheduleLayout();
            ParentChanged += (s, e) => ScheduleLayout();
            VisibleChanged += (s, e) => { if (Visible) ScheduleLayout(); };
        }

        public void Render(TelemetrySnapshot telemetry, ObsStatus obsStatus, RuijieStatus ruijieStatus,
            SafetySnapshot snapshot, PluginSettings settings)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => Render(telemetry, obsStatus, ruijieStatus, snapshot, settings)));
                return;
            }

            UpdateVisibleWidgets(settings.EnabledWidgets);

            SetWidget("sat_count", "Sat Count", telemetry.Satellites.ToString("0"),
                TelemetryStatusPolicy.Satellites(telemetry.Satellites));
            SetWidget("gps_hdop", "GPS HDOP", telemetry.Hdop.ToString("0.00"),
                TelemetryStatusPolicy.Hdop(telemetry.Hdop));
            SetWidget("dist_home", "Dist to Home", telemetry.DistanceToHomeMeters.ToString("0") + " m",
                TelemetryStatusPolicy.DistanceToHome(telemetry.DistanceToHomeMeters));
            SetWidget("bat_used", "Bat used", telemetry.BatteryUsedMah.ToString("0") + " mAh", WidgetStatus.Normal);

            if (!ruijieStatus.Connected || ruijieStatus.Stale || !ruijieStatus.Rssi.HasValue)
                SetWidget("ruijie", "Ruijie", "DIS", WidgetStatus.Bad);
            else
                SetWidget("ruijie", "Ruijie", ruijieStatus.Rssi.Value + " dBm",
                    TelemetryStatusPolicy.LinkRssi(ruijieStatus.Rssi.Value));

            SetWidget("obs", "OBS",
                !obsStatus.Connected ? "DIS" : obsStatus.Recording == true ? "REC" : "NR",
                WidgetStatusPolicy.Obs(telemetry.Armed, obsStatus));

            SetWidget("ground_speed", "Ground Speed", telemetry.GroundSpeed.ToString("0.0") + " m/s", WidgetStatus.Normal);
            SetWidget("vertical_speed", "Vertical Speed", telemetry.VerticalSpeed.ToString("0.0") + " m/s", WidgetStatus.Normal);
            SetWidget("air_speed", "Air Speed", telemetry.AirSpeed.ToString("0.0") + " m/s", WidgetStatus.Normal);
            SetWidget("altitude", "Altitude", telemetry.Altitude.ToString("0.0") + " m", WidgetStatus.Normal);
            SetWidget("battery_voltage", "Battery",
                telemetry.BatteryVoltage.ToString("0.0") + "V " + telemetry.CurrentAmps.ToString("0") + "A",
                TelemetryStatusPolicy.Voltage(telemetry.BatteryVoltage));
            SetWidget("current", "Current", telemetry.CurrentAmps.ToString("0.0") + " A",
                TelemetryStatusPolicy.Current(telemetry.CurrentAmps));
            foreach (var item in telemetry.AdditionalTelemetry)
            {
                var definition = WidgetCatalog.Definitions.FirstOrDefault(x =>
                    string.Equals(x.Id, item.Key, StringComparison.OrdinalIgnoreCase));
                if (definition != null)
                    SetWidget(definition.Id, definition.Title, item.Value, WidgetStatus.Normal);
            }
            if (!hasTelemetryContent)
                hasTelemetryContent = ArrangeWidgets();

        }

        private void SetWidget(string id, string title, string value, WidgetStatus status)
        {
            TelemetryWidget widget;
            if (widgets.TryGetValue(id, out widget))
                widget.SetContent(title, value, status);
        }

        private void UpdateVisibleWidgets(IEnumerable<string> enabled)
        {
            var definitions = WidgetCatalog.Definitions.ToDictionary(x => x.Id,
                StringComparer.OrdinalIgnoreCase);
            var ordered = (enabled ?? WidgetCatalog.DefaultIds)
                .Where(definitions.ContainsKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(x => definitions[x]).ToArray();
            var signature = string.Join("|", ordered.Select(x => x.Id));
            if (signature == enabledSignature) return;

            enabledSignature = signature;
            grid.SuspendLayout();
            grid.Controls.Clear();
            foreach (var definition in ordered)
                grid.Controls.Add(widgets[definition.Id]);
            grid.ResumeLayout(true);
            ArrangeWidgets();
        }

        private bool ArrangeWidgets()
        {
            if (grid.ClientSize.Width <= 0 || grid.ClientSize.Height <= 0 ||
                grid.Controls.Count == 0) return false;
            var count = grid.Controls.Count;
            var availableWidth = Math.Max(20, grid.ClientSize.Width - grid.Padding.Horizontal);
            var availableHeight = Math.Max(20, grid.ClientSize.Height - grid.Padding.Vertical);
            const int spacing = 6;

            var bestColumns = 1;
            var bestRows = count;
            var bestHeaderSize = 4f;
            var bestValueSize = 6f;
            var bestScore = double.MinValue;
            for (var columns = 1; columns <= count; columns++)
            {
                var rows = (int)Math.Ceiling(count / (double)columns);
                var cellWidth = availableWidth / (double)columns - spacing;
                var cellHeight = availableHeight / (double)rows - spacing;
                if (cellWidth <= 0 || cellHeight <= 0) continue;

                var headerSize = FittingFontSize(true, (int)cellWidth, (int)(cellHeight * 0.38));
                var valueSize = FittingFontSize(false, (int)cellWidth, (int)(cellHeight * 0.62));
                var aspect = cellWidth / cellHeight;
                var score = headerSize / 14.0 + valueSize / 26.0 -
                    Math.Abs(Math.Log(aspect / 1.65)) * 0.04;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestHeaderSize = headerSize;
                    bestValueSize = valueSize;
                    bestColumns = columns;
                    bestRows = rows;
                }
            }

            grid.SuspendLayout();
            var visibleWidgets = grid.Controls.Cast<TelemetryWidget>().ToArray();
            grid.Controls.Clear();
            grid.ColumnCount = bestColumns;
            grid.RowCount = bestRows;
            grid.ColumnStyles.Clear();
            grid.RowStyles.Clear();
            for (var column = 0; column < bestColumns; column++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / bestColumns));
            for (var row = 0; row < bestRows; row++)
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / bestRows));
            for (var index = 0; index < visibleWidgets.Length; index++)
            {
                var widget = visibleWidgets[index];
                widget.Dock = DockStyle.Fill;
                grid.Controls.Add(widget, index % bestColumns, index / bestColumns);
                widget.ApplyFontSizes(bestHeaderSize, bestValueSize);
            }
            grid.ResumeLayout(true);
            grid.Invalidate(true);
            return true;
        }

        private void ScheduleLayout()
        {
            if (!IsHandleCreated || IsDisposed || Disposing) return;
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || Disposing) return;
                PerformLayout();
                grid.PerformLayout();
                ArrangeWidgets();
            }));
        }

        private float FittingFontSize(bool title, int width, int height)
        {
            var low = title ? 4f : 6f;
            var high = title ? 14f : 26f;
            var usableWidth = Math.Max(1, width - 6);
            var usableHeight = Math.Max(1, height - 4);
            for (var i = 0; i < 7; i++)
            {
                var candidate = (low + high) / 2f;
                if (AllTextFits(title, candidate, usableWidth, usableHeight))
                    low = candidate;
                else
                    high = candidate;
            }
            return low;
        }

        private bool AllTextFits(bool title, float fontSize, int width, int height)
        {
            using (var font = new Font(SystemFonts.MessageBoxFont.FontFamily, fontSize, FontStyle.Bold))
            {
                foreach (TelemetryWidget widget in grid.Controls)
                {
                    var text = title ? widget.TitleText : widget.ValueText;
                    var measured = TextRenderer.MeasureText(text ?? "", font, Size.Empty,
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    if (measured.Width > width || measured.Height > height) return false;
                }
            }
            return true;
        }

        private static void ApplyContextMenu(Control root, ContextMenuStrip menu)
        {
            root.ContextMenuStrip = menu;
            foreach (Control child in root.Controls) ApplyContextMenu(child, menu);
        }

        private sealed class BufferedTableLayoutPanel : TableLayoutPanel
        {
            public BufferedTableLayoutPanel()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint, true);
                UpdateStyles();
            }
        }
    }
}
