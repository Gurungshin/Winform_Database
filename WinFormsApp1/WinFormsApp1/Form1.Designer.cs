namespace WinFormsApp1
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            listBoxLogs = new ListBox();
            SuspendLayout();
            // 
            // listBoxLogs
            // 
            listBoxLogs.FormattingEnabled = true;
            listBoxLogs.Location = new Point(27, 29);
            listBoxLogs.Name = "listBoxLogs";
            listBoxLogs.Size = new Size(1778, 384);
            listBoxLogs.TabIndex = 0;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1817, 450);
            Controls.Add(listBoxLogs);
            Name = "Form1";
            Text = "Production Archiver Engine";
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.ListBox listBoxLogs;
    }
}