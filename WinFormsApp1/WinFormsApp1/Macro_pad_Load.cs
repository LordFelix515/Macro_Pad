using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using MacroPad.Controls;

namespace MacroPad
{
    public partial class Macro_pad_Load : Form
    {
        public int prg;
        private ColoredProgressBar? _coloredPb;

        public Macro_pad_Load()
        {
            InitializeComponent();

            // hide designer ProgressBar if present
            try { progressBar1.Visible = false; } catch { }

            // create and store colored progress bar as a field so we can update it later
            _coloredPb = new ColoredProgressBar
            {
                Location = progressBar1?.Location ?? new Point(12, 12),
                Size = progressBar1?.Size ?? new Size(300, 20),
                Minimum = progressBar1?.Minimum ?? 0,
                Maximum = progressBar1?.Maximum ?? 100,
                BarColor = Color.DarkSlateBlue,
                BarBackColor = Color.FromArgb(60, 60, 60),
                Anchor = progressBar1?.Anchor ?? AnchorStyles.Top | AnchorStyles.Left
            };

            _coloredPb.Value = Math.Clamp(prg, _coloredPb.Minimum, _coloredPb.Maximum);

            this.Controls.Add(_coloredPb);
        }

        // Public setter so other forms can update displayed progress safely
        public void SetProgress(int value)
        {
            prg = value;
            if (_coloredPb == null)
                return;

            if (_coloredPb.InvokeRequired)
            {
                _coloredPb.BeginInvoke((Action)(() =>
                    _coloredPb.Value = Math.Clamp(prg, _coloredPb.Minimum, _coloredPb.Maximum)));
            }
            else
            {
                _coloredPb.Value = Math.Clamp(prg, _coloredPb.Minimum, _coloredPb.Maximum);
            }
        }

        public int GetProgress() => prg;

        private void label1_Click(object sender, EventArgs e)
        {

        }

        public void SetLabelText(string text)
        {
            if (label1.InvokeRequired)
            {
                label1.Invoke((Action)(() => label1.Text = text));
            }
            else
            {
                label1.Text = text;
            }
        }

        private void label1_Click_1(object sender, EventArgs e)
        {

        }
    }
}
