namespace CouchControl.Agent.Status;

public sealed class StatusForm : Form
{
    private readonly IAgentStatusService agentStatusService;
    private readonly Func<Task> saveSnapshotAsync;
    private readonly Func<Task> clearSnapshotAsync;
    private readonly Dictionary<string, Label> valueLabels = new(StringComparer.Ordinal);
    private readonly Button refreshButton;
    private readonly Button saveSnapshotButton;
    private readonly Button clearSnapshotButton;
    private bool refreshInProgress;
    private bool suspendRefresh;

    public StatusForm(
        IAgentStatusService agentStatusService,
        Func<Task> saveSnapshotAsync,
        Func<Task> clearSnapshotAsync)
    {
        this.agentStatusService = agentStatusService;
        this.saveSnapshotAsync = saveSnapshotAsync;
        this.clearSnapshotAsync = clearSnapshotAsync;

        Text = "Couch Control Status";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(700, 360);
        MinimumSize = new Size(560, 320);

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(16)
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 9,
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, 0, "Current mode");
        AddRow(layout, 1, "Current operation");
        AddRow(layout, 2, "Current step");
        AddRow(layout, 3, "Configured TV");
        AddRow(layout, 4, "TV connected");
        AddRow(layout, 5, "Listening LAN");
        AddRow(layout, 6, "MAC address");
        AddRow(layout, 7, "Steam");
        AddRow(layout, 8, "Last result");

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Margin = new Padding(0, 16, 0, 0)
        };

        refreshButton = new Button
        {
            Text = "Refresh",
            AutoSize = true
        };
        refreshButton.Click += async (_, _) => await RunButtonActionAsync(refreshButton, RefreshNowAsync);

        saveSnapshotButton = new Button
        {
            Text = "Save Current Desktop Snapshot",
            AutoSize = true
        };
        saveSnapshotButton.Click += async (_, _) => await RunButtonActionAsync(saveSnapshotButton, saveSnapshotAsync);

        clearSnapshotButton = new Button
        {
            Text = "Clear Saved Desktop Snapshot",
            AutoSize = true
        };
        clearSnapshotButton.Click += async (_, _) => await RunButtonActionAsync(clearSnapshotButton, clearSnapshotAsync);

        buttonPanel.Controls.Add(refreshButton);
        buttonPanel.Controls.Add(saveSnapshotButton);
        buttonPanel.Controls.Add(clearSnapshotButton);

        rootLayout.Controls.Add(layout, 0, 0);
        rootLayout.Controls.Add(buttonPanel, 0, 1);
        Controls.Add(rootLayout);

        Shown += async (_, _) => await RefreshNowAsync();
        FormClosing += OnFormClosing;
    }

    public async Task RefreshNowAsync()
    {
        if (refreshInProgress || IsDisposed || suspendRefresh || !Visible)
        {
            return;
        }

        refreshInProgress = true;

        try
        {
            var snapshot = await agentStatusService.GetStatusAsync();
            valueLabels["Current mode"].Text = snapshot.CurrentMode;
            valueLabels["Current operation"].Text = snapshot.CurrentOperation;
            valueLabels["Current step"].Text = snapshot.CurrentStep;
            valueLabels["Configured TV"].Text = snapshot.ConfiguredTv;
            valueLabels["TV connected"].Text = snapshot.TvConnectionStatus;
            valueLabels["Listening LAN"].Text = snapshot.ListeningAddresses;
            valueLabels["MAC address"].Text = snapshot.MacAddress;
            valueLabels["Steam"].Text = snapshot.SteamStatus;
            valueLabels["Last result"].Text = snapshot.LastResult;
        }
        catch (Exception ex)
        {
            valueLabels["Last result"].Text = $"Status refresh failed: {ex.Message}";
        }
        finally
        {
            refreshInProgress = false;
        }
    }

    private void AddRow(TableLayoutPanel layout, int rowIndex, string title)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = title,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Font = new Font(Font, FontStyle.Bold)
        };

        var value = new Label
        {
            Text = "-",
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };

        valueLabels[title] = value;
        layout.Controls.Add(label, 0, rowIndex);
        layout.Controls.Add(value, 1, rowIndex);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.UserClosing)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    protected override void WndProc(ref Message m)
    {
        const int WmEnterSizeMove = 0x0231;
        const int WmExitSizeMove = 0x0232;

        switch (m.Msg)
        {
            case WmEnterSizeMove:
                suspendRefresh = true;
                break;
            case WmExitSizeMove:
                suspendRefresh = false;
                break;
        }

        base.WndProc(ref m);
    }

    private async Task RunButtonActionAsync(Button button, Func<Task> action)
    {
        if (!button.Enabled)
        {
            return;
        }

        button.Enabled = false;
        try
        {
            await action();
            await RefreshNowAsync();
        }
        finally
        {
            button.Enabled = true;
        }
    }
}
