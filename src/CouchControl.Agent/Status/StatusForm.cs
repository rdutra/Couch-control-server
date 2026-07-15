namespace CouchControl.Agent.Status;

public sealed class StatusForm : Form
{
    private readonly IAgentStatusService agentStatusService;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private readonly Dictionary<string, Label> valueLabels = new(StringComparer.Ordinal);
    private bool refreshInProgress;

    public StatusForm(IAgentStatusService agentStatusService)
    {
        this.agentStatusService = agentStatusService;

        Text = "Couch Control Status";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(640, 320);
        MinimumSize = new Size(520, 280);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(16),
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, 0, "Current mode");
        AddRow(layout, 1, "Current operation");
        AddRow(layout, 2, "Current step");
        AddRow(layout, 3, "Configured TV");
        AddRow(layout, 4, "TV connected");
        AddRow(layout, 5, "Steam");
        AddRow(layout, 6, "Last result");
        Controls.Add(layout);

        refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 2000
        };
        refreshTimer.Tick += async (_, _) => await RefreshNowAsync();
        refreshTimer.Start();

        Shown += async (_, _) => await RefreshNowAsync();
        FormClosing += OnFormClosing;
    }

    public async Task RefreshNowAsync()
    {
        if (refreshInProgress || IsDisposed)
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
}
