using CouchControl.Windows.AgentApi;

namespace CouchControl.Agent.Settings;

public sealed class PairingCodeForm : Form
{
    private readonly IPairingService pairingService;
    private readonly Label codeValueLabel;
    private readonly Label expiresValueLabel;
    private readonly Label statusLabel;
    private readonly Button closePairingButton;
    private readonly Button refreshButton;

    public PairingCodeForm(IPairingService pairingService)
    {
        this.pairingService = pairingService;

        Text = "Device Pairing";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(360, 220);
        MinimumSize = new Size(320, 200);

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

        layout.Controls.Add(CreateTitleLabel("Expires"), 0, 1);
        expiresValueLabel = CreateValueLabel("-");
        layout.Controls.Add(expiresValueLabel, 1, 1);

        statusLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(260, 0),
            Margin = new Padding(0, 12, 0, 0),
            Text = "Pairing is active for one device."
        };
        layout.Controls.Add(statusLabel, 1, 2);

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
        layout.Controls.Add(closePairingButton, 1, 3);

        refreshButton = new Button
        {
            Text = "Refresh",
            AutoSize = true,
            Margin = new Padding(8, 16, 0, 0)
        };
        refreshButton.Click += async (_, _) => await RefreshSessionAsync();
        layout.Controls.Add(refreshButton, 1, 4);

        Controls.Add(layout);

        Shown += async (_, _) =>
        {
            await RefreshSessionAsync();
        };
        FormClosing += OnFormClosing;
    }

    public async Task ShowSessionAsync(PairingSession session)
    {
        UpdateUi(session);
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
            expiresValueLabel.Text = "-";
            statusLabel.Text = "Pairing is inactive. Start a new session from the tray menu.";
            closePairingButton.Enabled = false;
            return;
        }

        UpdateUi(session);
    }

    private void UpdateUi(PairingSession session)
    {
        codeValueLabel.Text = session.PairingCode;
        expiresValueLabel.Text = $"{Math.Max(0, (int)Math.Ceiling((session.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalSeconds))} seconds";
        statusLabel.Text = session.FailedAttempts > 0
            ? $"Failed attempts: {session.FailedAttempts}. Remaining: {session.RemainingAttempts}."
            : "Pairing is active for one device.";
        closePairingButton.Enabled = true;
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
            MaximumSize = new Size(220, 0)
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
