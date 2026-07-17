using CouchControl.Windows.AgentApi;
using CouchControl.Core.Abstractions;

namespace CouchControl.Agent.Settings;

public sealed class PairingCodeForm : Form
{
    private readonly IPairingService pairingService;
    private readonly IAgentConfigurationStore configurationStore;
    private readonly ILocalNetworkInterfaceProvider networkInterfaceProvider;
    private readonly Label codeValueLabel;
    private readonly Label computerAddressValueLabel;
    private readonly Label macAddressValueLabel;
    private readonly Label expiresValueLabel;
    private readonly Label statusLabel;
    private readonly Button closePairingButton;
    private readonly Button refreshButton;

    public PairingCodeForm(
        IPairingService pairingService,
        IAgentConfigurationStore configurationStore,
        ILocalNetworkInterfaceProvider networkInterfaceProvider)
    {
        this.pairingService = pairingService;
        this.configurationStore = configurationStore;
        this.networkInterfaceProvider = networkInterfaceProvider;

        Text = "Device Pairing";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(460, 300);
        MinimumSize = new Size(420, 280);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(16)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(CreateTitleLabel("Pairing Code"), 0, 0);
        codeValueLabel = CreateValueLabel("-");
        codeValueLabel.Font = new Font(Font.FontFamily, 22, FontStyle.Bold);
        layout.Controls.Add(codeValueLabel, 1, 0);

        layout.Controls.Add(CreateTitleLabel("Computer Address"), 0, 1);
        computerAddressValueLabel = CreateValueLabel("-");
        layout.Controls.Add(computerAddressValueLabel, 1, 1);

        layout.Controls.Add(CreateTitleLabel("MAC Address"), 0, 2);
        macAddressValueLabel = CreateValueLabel("-");
        layout.Controls.Add(macAddressValueLabel, 1, 2);

        layout.Controls.Add(CreateTitleLabel("Expires"), 0, 3);
        expiresValueLabel = CreateValueLabel("-");
        layout.Controls.Add(expiresValueLabel, 1, 3);

        statusLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(300, 0),
            Margin = new Padding(0, 12, 0, 0),
            Text = "Enter the computer address in the mobile app, then enter this pairing code."
        };
        layout.Controls.Add(statusLabel, 1, 4);

        closePairingButton = new Button
        {
            Text = "Disable Pairing",
            AutoSize = true,
            Margin = new Padding(0, 16, 0, 0)
        };
        closePairingButton.Click += async (_, _) =>
        {
            await pairingService.DisableAsync();
            await RefreshSessionAsync();
        };
        layout.Controls.Add(closePairingButton, 1, 5);

        refreshButton = new Button
        {
            Text = "Refresh",
            AutoSize = true,
            Margin = new Padding(8, 16, 0, 0)
        };
        refreshButton.Click += async (_, _) => await RefreshSessionAsync();
        layout.Controls.Add(refreshButton, 1, 6);

        Controls.Add(layout);

        Shown += async (_, _) =>
        {
            await RefreshSessionAsync();
        };
        FormClosing += OnFormClosing;
    }

    public async Task ShowSessionAsync(PairingSession session)
    {
        await UpdateUiAsync(session);
        Show();
        BringToFront();
        await RefreshSessionAsync();
    }

    private async Task RefreshSessionAsync()
    {
        if (IsDisposed)
        {
            return;
        }

        var session = await pairingService.GetCurrentSessionAsync();
        if (session is null)
        {
            codeValueLabel.Text = "Closed";
            await RefreshNetworkDetailsAsync();
            expiresValueLabel.Text = "-";
            statusLabel.Text = "Pairing is inactive. Start a new session from the tray menu.";
            closePairingButton.Enabled = false;
            return;
        }

        await UpdateUiAsync(session);
    }

    private async Task UpdateUiAsync(PairingSession session)
    {
        codeValueLabel.Text = session.PairingCode;
        await RefreshNetworkDetailsAsync();
        expiresValueLabel.Text = $"{Math.Max(0, (int)Math.Ceiling((session.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalSeconds))} seconds";
        statusLabel.Text = session.FailedAttempts > 0
            ? $"Failed attempts: {session.FailedAttempts}. Remaining: {session.RemainingAttempts}."
            : "Enter the computer address in the mobile app, then enter this pairing code.";
        closePairingButton.Enabled = true;
    }

    private async Task RefreshNetworkDetailsAsync()
    {
        var configuration = await configurationStore.LoadAsync();
        var bindingPlan = networkInterfaceProvider.CreateBindingPlan(configuration);
        computerAddressValueLabel.Text = bindingPlan.ListenUrls.FirstOrDefault() ??
            (bindingPlan.LanIpv4Addresses.Count == 0 ? "No LAN address available" : bindingPlan.LanIpv4Addresses[0]);
        macAddressValueLabel.Text = bindingPlan.MacAddress ?? "Unavailable";
    }

    private static Label CreateTitleLabel(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
        };

    private static Label CreateValueLabel(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(300, 0)
        };

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
