using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZeroTrace_Security_Official
{
    /// <summary>
    /// Small native WinForms overlay used for the FluentDesignForm close caption button.
    /// It deliberately paints the glyph from the application's accent token so the
    /// rendered close mark is independent of the DevExpress skin's semantic Red role.
    /// </summary>
    internal sealed class AccentCaptionCloseButton : Control
    {
        private readonly Color accentColor;
        private bool hot;

        public AccentCaptionCloseButton(Color accentColor)
        {
            this.accentColor = accentColor;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            // Do not assign Color.Transparent here. A plain WinForms Control does not
            // support transparent BackColor unless ControlStyles.SupportsTransparentBackColor
            // is enabled before the assignment, and the FluentDesignForm caption host
            // does not require simulated transparency for this overlay. The parent
            // caption color is copied by Form1 when the control is installed/relayout.
            TabStop = false;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = "Close";
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hot = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hot = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int logicalDpi = 96;
            try
            {
                logicalDpi = DeviceDpi;
            }
            catch
            {
                // DeviceDpi is unavailable on older WinForms implementations; 96 is
                // the correct design baseline and keeps the control functional.
            }

            float scale = Math.Max(0.75f, logicalDpi / 96f);
            float centerX = Width / 2f;
            float centerY = Height / 2f;
            float half = 7f * scale;
            float stroke = Math.Max(1.5f, 1.8f * scale);

            // Keep the caption control visually quiet while retaining a clear hit area.
            // The exact accent color is used for both the normal and hover glyph so the
            // UI does not introduce a second arbitrary title-bar accent.
            using (Pen pen = new Pen(accentColor, stroke))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                e.Graphics.DrawLine(pen, centerX - half, centerY - half, centerX + half, centerY + half);
                e.Graphics.DrawLine(pen, centerX + half, centerY - half, centerX - half, centerY + half);
            }

            if (hot)
            {
                using (Pen hotPen = new Pen(Color.FromArgb(48, accentColor.R, accentColor.G, accentColor.B), Math.Max(1f, scale)))
                {
                    e.Graphics.DrawRectangle(hotPen, 0.5f, 0.5f, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
                }
            }
        }
    }
}
