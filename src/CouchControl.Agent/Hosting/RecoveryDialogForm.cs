namespace CouchControl.Agent.Hosting;

internal sealed class RecoveryDialogForm : Form
{
    public RecoveryDialogForm(string message, bool automaticRecoveryConfigured)
    {
        Text = "Display Recovery Required";
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(520, 220);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Text = "CouchControl detected an interrupted display operation."
        };

        var body = message;
        if (automaticRecoveryConfigured)
        {
            body += $"{Environment.NewLine}{Environment.NewLine}Automatic recovery is configured but remains disabled in this development build.";
        }

        var messageLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            Text = body
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };

        var restoreButton = new Button
        {
            Text = "Restore previous desktop configuration",
            AutoSize = true,
            DialogResult = DialogResult.Yes
        };
        var ignoreButton = new Button
        {
            Text = "Ignore",
            AutoSize = true,
            DialogResult = DialogResult.Ignore
        };
        var logsButton = new Button
        {
            Text = "Open logs",
            AutoSize = true,
            DialogResult = DialogResult.Retry
        };

        buttonPanel.Controls.Add(restoreButton);
        buttonPanel.Controls.Add(ignoreButton);
        buttonPanel.Controls.Add(logsButton);

        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(messageLabel, 0, 1);
        layout.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(layout);
        AcceptButton = restoreButton;
        CancelButton = ignoreButton;
    }
}
