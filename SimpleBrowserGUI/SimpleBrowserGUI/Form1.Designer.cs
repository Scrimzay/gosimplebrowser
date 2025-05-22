namespace SimpleBrowserGUI
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
            urlTextBox = new TextBox();
            navPanel = new Panel();
            fetchButton = new Button();
            forwardButton = new Button();
            backButton = new Button();
            contentTextBox = new RichTextBox();
            imagePanel = new FlowLayoutPanel();
            navPanel.SuspendLayout();
            SuspendLayout();
            // 
            // urlTextBox
            // 
            urlTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            urlTextBox.Location = new Point(75, 12);
            urlTextBox.Name = "urlTextBox";
            urlTextBox.Size = new Size(584, 23);
            urlTextBox.TabIndex = 0;
            urlTextBox.KeyDown += urlTextBox_KeyDown;
            // 
            // navPanel
            // 
            navPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            navPanel.Controls.Add(fetchButton);
            navPanel.Controls.Add(forwardButton);
            navPanel.Controls.Add(backButton);
            navPanel.Location = new Point(75, 41);
            navPanel.Name = "navPanel";
            navPanel.Size = new Size(584, 40);
            navPanel.TabIndex = 1;
            // 
            // fetchButton
            // 
            fetchButton.Location = new Point(397, 3);
            fetchButton.Name = "fetchButton";
            fetchButton.Size = new Size(100, 30);
            fetchButton.TabIndex = 2;
            fetchButton.Text = "Search";
            fetchButton.UseVisualStyleBackColor = true;
            fetchButton.Click += FetchButton_Click;
            // 
            // forwardButton
            // 
            forwardButton.Location = new Point(145, 3);
            forwardButton.Name = "forwardButton";
            forwardButton.Size = new Size(100, 30);
            forwardButton.TabIndex = 1;
            forwardButton.Text = "Forward";
            forwardButton.UseVisualStyleBackColor = true;
            forwardButton.Click += ForwardButton_Click;
            // 
            // backButton
            // 
            backButton.Location = new Point(3, 3);
            backButton.Name = "backButton";
            backButton.Size = new Size(100, 30);
            backButton.TabIndex = 0;
            backButton.Text = "Back";
            backButton.UseVisualStyleBackColor = true;
            backButton.Click += BackButton_Click;
            // 
            // contentTextBox
            // 
            contentTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            contentTextBox.BackColor = SystemColors.Control;
            contentTextBox.Location = new Point(75, 87);
            contentTextBox.Name = "contentTextBox";
            contentTextBox.ReadOnly = true;
            contentTextBox.ScrollBars = RichTextBoxScrollBars.Vertical;
            contentTextBox.Size = new Size(584, 411);
            contentTextBox.TabIndex = 2;
            contentTextBox.Text = "";
            // 
            // imagePanel
            // 
            imagePanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            imagePanel.AutoScroll = true;
            imagePanel.Location = new Point(72, 393);
            imagePanel.Name = "imagePanel";
            imagePanel.Size = new Size(584, 211);
            imagePanel.TabIndex = 3;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(884, 561);
            Controls.Add(imagePanel);
            Controls.Add(contentTextBox);
            Controls.Add(navPanel);
            Controls.Add(urlTextBox);
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Simple Browser";
            navPanel.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox urlTextBox;
        private Panel navPanel;
        private Button fetchButton;
        private Button forwardButton;
        private Button backButton;
        private RichTextBox contentTextBox;
        private FlowLayoutPanel imagePanel;
    }
}
