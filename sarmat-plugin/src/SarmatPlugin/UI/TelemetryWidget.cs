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
        private float titleSize;
        private float valueSize;

        public TelemetryWidget()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint, true);
            ColumnCount = 1;
            RowCount = 2;
            Margin = new Padding(3);
            BackColor = Color.FromArgb(34, 38, 42);
            RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            titleLabel = Label(Color.Silver);
            valueLabel = Label(Color.White);
            Controls.Add(titleLabel, 0, 0);
            Controls.Add(valueLabel, 0, 1);
        }

        public void SetContent(string title, string value, WidgetStatus status)
        {
            titleLabel.Text = title;
            valueLabel.Text = value;
            valueLabel.ForeColor = StatusColor(status);
        }

        public void ApplyFontSizes(float headerFontSize, float valueFontSize)
        {
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
        }

        public string TitleText => titleLabel.Text;
        public string ValueText => valueLabel.Text;

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
