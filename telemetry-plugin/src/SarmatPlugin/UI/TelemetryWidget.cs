using System;
using System.Drawing;
using System.Windows.Forms;
using SarmatPlugin.Core;

namespace SarmatPlugin.UI
{
    internal sealed class TelemetryWidget : TableLayoutPanel
    {
        private readonly Label titleLabel;
        private readonly Label valueLabel;
        private readonly Label detailLabel;
        private bool batteryLayout;
        private float titleSize;
        private float valueSize;
        private float detailSize;

        public TelemetryWidget()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint, true);
            ColumnCount = 1;
            RowCount = 3;
            Margin = new Padding(3);
            BackColor = Color.FromArgb(34, 38, 42);
            RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            titleLabel = Label(Color.Silver);
            valueLabel = Label(Color.White);
            detailLabel = Label(Color.White);
            detailLabel.Visible = false;
            Controls.Add(titleLabel, 0, 0);
            Controls.Add(valueLabel, 0, 1);
            Controls.Add(detailLabel, 0, 2);
        }

        public void SetContent(string title, string value, WidgetStatus status)
        {
            titleLabel.Text = title;
            valueLabel.Text = value;
            valueLabel.ForeColor = StatusColor(status);
        }

        public void SetBatteryContent(int? chargePercent, string voltageAndCurrent, WidgetStatus status)
        {
            if (!batteryLayout)
            {
                batteryLayout = true;
                RowStyles[0].Height = 28;
                RowStyles[1].Height = 42;
                RowStyles[2].SizeType = SizeType.Percent;
                RowStyles[2].Height = 30;
                detailLabel.Visible = true;
            }
            SetContent("Battery", chargePercent.HasValue ? chargePercent.Value + "%" : "—%", status);
            detailLabel.Text = voltageAndCurrent;
            detailLabel.ForeColor = StatusColor(status);
        }

        public void ApplyFontSizes(float headerFontSize, float valueFontSize)
        {
            if (batteryLayout) { headerFontSize *= 0.74f; valueFontSize *= 0.68f; }
            if (Math.Abs(titleSize - headerFontSize) > 0.01f)
            {
                titleLabel.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, headerFontSize, FontStyle.Bold);
                titleSize = headerFontSize;
            }
            if (Math.Abs(valueSize - valueFontSize) > 0.01f)
            {
                valueLabel.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, valueFontSize, FontStyle.Bold);
                valueSize = valueFontSize;
            }
            if (batteryLayout)
            {
                var compactSize = Math.Max(4f, valueFontSize * 0.65f);
                if (Math.Abs(detailSize - compactSize) > 0.01f)
                {
                    detailLabel.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, compactSize, FontStyle.Regular);
                    detailSize = compactSize;
                }
            }
        }

        public string TitleText => titleLabel.Text;
        public string ValueText => valueLabel.Text;
        public bool IsBatteryLayout => batteryLayout;
        public string DetailText => detailLabel.Text;

        private static Label Label(Color color) => new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = color,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            Margin = new Padding(1)
        };

        private static Color StatusColor(WidgetStatus status)
        {
            switch (status)
            {
                case WidgetStatus.Good: return Color.LimeGreen;
                case WidgetStatus.Bad: return Color.OrangeRed;
                default: return Color.Gold;
            }
        }
    }
}
