using CouchControl.Windows.AgentApi;

namespace CouchControl.Agent.Status;

public sealed class NetworkDiagnosticsForm : Form
{
    private readonly IAgentNetworkDiagnosticsService diagnosticsService;
    private readonly Dictionary<string, Label> valueLabels = new(StringComparer.Ordinal);
    private readonly Button refreshButton;
    private bool refreshInProgress;

    public NetworkDiagnosticsForm(IAgentNetworkDiagnosticsService diagnosticsService)
    {
        this.diagnosticsService = diagnosticsService;

        Text = "Couch Control Network Diagnostics";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(760, 340);
        MinimumSize = new Size(620, 300);

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
            RowCount = 8,
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, 0, "Host name");
        AddRow(layout, 1, "Agent port");
        AddRow(layout, 2, "Listening interface");
        AddRow(layout, 3, "LAN IPv4 addresses");
        AddRow(layout, 4, "MAC address");
        AddRow(layout, 5, "Firewall rule");
        AddRow(layout, 6, "API health");
        AddRow(layout, 7, "Windows network");

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
        refreshButton.Click += async (_, _) => await RefreshNowAsync();

        buttonPanel.Controls.Add(refreshButton);
        rootLayout.Controls.Add(layout, 0, 0);
        rootLayout.Controls.Add(buttonPanel, 0, 1);
        Controls.Add(rootLayout);

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
            var snapshot = await diagnosticsService.GetSnapshotAsync();
            valueLabels["Host name"].Text = snapshot.HostName;
            valueLabels["Agent port"].Text = snapshot.Port.ToString();
            valueLabels["Listening interface"].Text = snapshot.ListeningInterface;
            valueLabels["LAN IPv4 addresses"].Text = snapshot.LanIpv4Addresses.Count == 0
                ? "None"
                : string.Join(", ", snapshot.LanIpv4Addresses);
            valueLabels["MAC address"].Text = snapshot.MacAddress ?? "Unavailable";
            valueLabels["Firewall rule"].Text = snapshot.FirewallRuleStatus;
            valueLabels["API health"].Text = snapshot.ApiHealthStatus;
            valueLabels["Windows network"].Text = snapshot.NetworkProfileStatus;
        }
        catch (Exception ex)
        {
            valueLabels["API health"].Text = $"Diagnostics refresh failed: {ex.Message}";
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
            MaximumSize = new Size(500, 0),
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
