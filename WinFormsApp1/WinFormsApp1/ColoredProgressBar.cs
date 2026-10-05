using System;
using System.Drawing;
using System.Windows.Forms;
using System.ComponentModel;

namespace MacroPad.Controls
{
    public class ColoredProgressBar : ProgressBar
    {
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color BarColor { get; set; } = Color.DodgerBlue;

        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color BarBackColor { get; set; } = SystemColors.ControlDark;

        public ColoredProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // handle zero range
            if (Maximum == Minimum)
            {
                base.OnPaint(e);
                return;
            }

            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? SystemColors.Control);

            // background
            using (var backBrush = new SolidBrush(BarBackColor))
            {
                g.FillRectangle(backBrush, ClientRectangle);
            }

            // filled portion
            float range = Maximum - (float)Minimum;
            float pct = (Value - Minimum) / range;
            int width = (int)(ClientRectangle.Width * pct);
            if (width > 0)
            {
                var fillRect = new Rectangle(0, 0, width, ClientRectangle.Height);
                using (var barBrush = new SolidBrush(BarColor))
                {
                    g.FillRectangle(barBrush, fillRect);
                }
            }

            // optional border (match system look)
            using (var pen = new Pen(Color.FromArgb(100, Color.Black)))
            {
                g.DrawRectangle(pen, 0, 0, ClientRectangle.Width - 1, ClientRectangle.Height - 1);
            }
        }
    }
}