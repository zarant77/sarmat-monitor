using System.Drawing;
using System.Windows.Forms;

namespace SarmatPlugin
{
    internal sealed class TranslucentWarningLabel : Label
    {
        private readonly Color overlayColor = Color.FromArgb(150, 190, 0, 0);
        public bool FlashOn { get; set; } = true;

        public TranslucentWarningLabel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            if (!FlashOn) return;
            using (var brush = new SolidBrush(overlayColor))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (FlashOn) base.OnPaint(e);
        }
    }
}
