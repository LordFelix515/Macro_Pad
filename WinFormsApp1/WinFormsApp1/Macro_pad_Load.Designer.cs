namespace MacroPad
{
    partial class Macro_pad_Load
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Macro_pad_Load));
            progressBar1 = new ProgressBar();
            label3 = new Label();
            label1 = new Label();
            SuspendLayout();
            // 
            // progressBar1
            // 
            progressBar1.BackColor = Color.FromArgb(48, 48, 48);
            progressBar1.ForeColor = Color.DarkSlateBlue;
            progressBar1.Location = new Point(12, 67);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new Size(414, 23);
            progressBar1.TabIndex = 0;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Font = new Font("Trebuchet MS", 24F, FontStyle.Bold, GraphicsUnit.Point, 0);
            label3.ForeColor = SystemColors.Control;
            label3.Location = new Point(132, 11);
            label3.Name = "label3";
            label3.Size = new Size(170, 40);
            label3.TabIndex = 3;
            label3.Text = "Macro Pad";
            // 
            // label1
            // 
            label1.ForeColor = Color.MediumSlateBlue;
            label1.Location = new Point(12, 93);
            label1.Name = "label1";
            label1.Size = new Size(414, 38);
            label1.TabIndex = 4;
            label1.Text = "  ";
            label1.TextAlign = ContentAlignment.MiddleCenter;
            label1.Visible = false;
            label1.Click += label1_Click_1;
            // 
            // Macro_pad_Load
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(48, 48, 48);
            ClientSize = new Size(438, 131);
            Controls.Add(label1);
            Controls.Add(label3);
            Controls.Add(progressBar1);
            FormBorderStyle = FormBorderStyle.None;
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "Macro_pad_Load";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Macro Pad";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ProgressBar progressBar1;
        private Label label3;
        private Label label1;
    }
}