using System.Linq;
using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using CouchControl.Windows.AgentApi;
using CouchControl.Windows.Startup;

namespace CouchControl.Agent.Settings;

public sealed class SettingsForm : Form
{
    private const string ApplicationName = "CouchControl.Agent";

    private readonly IAgentConfigurationStore configurationStore;
    private readonly IStartupRegistration startupRegistration;
    private readonly string startupCommandLine;
    private readonly IApiTokenStore apiTokenStore;
    private readonly Label configuredTvValue;
    private readonly Label preferredModeValue;
    private readonly TextBox apiPortTextBox;
    private readonly TextBox corsOriginsTextBox;
    private readonly TextBox apiTokenTextBox;
    private readonly CheckBox launchSteamCheckBox;
    private readonly CheckBox startWithWindowsCheckBox;
    private readonly Button saveButton;
    private readonly Button reloadButton;

    private bool isBusy;

    public SettingsForm(
        IAgentConfigurationStore configurationStore,
        IStartupRegistration startupRegistration,
        string startupCommandLine,
        IApiTokenStore apiTokenStore)
    {
        this.configurationStore = configurationStore;
        this.startupRegistration = startupRegistration;
        this.startupCommandLine = startupCommandLine;
        this.apiTokenStore = apiTokenStore;

        Text = "Couch Control Settings";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(640, 380);
        MinimumSize = new Size(580, 340);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(16)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        configuredTvValue = AddReadOnlyRow(layout, 0, "Configured TV");
        preferredModeValue = AddReadOnlyRow(layout, 1, "Preferred mode");
        apiPortTextBox = AddTextRow(layout, 2, "API port");
        corsOriginsTextBox = AddTextRow(layout, 3, "CORS origins");
        apiTokenTextBox = AddTextRow(layout, 4, "API token", isReadOnly: true);

        launchSteamCheckBox = new CheckBox
        {
            Text = "Launch Steam automatically",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        layout.Controls.Add(launchSteamCheckBox, 1, 5);

        startWithWindowsCheckBox = new CheckBox
        {
            Text = "Start with Windows",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        layout.Controls.Add(startWithWindowsCheckBox, 1, 6);

        var helpLabel = new Label
        {
            Text = "TV selection and display mode are still configured through CouchControl.Cli. Restart the agent after changing the API port.",
            AutoSize = true,
            MaximumSize = new Size(380, 0),
            Margin = new Padding(0, 12, 0, 0)
        };
        layout.Controls.Add(helpLabel, 1, 7);

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 16, 0, 0)
        };

        saveButton = new Button
        {
            Text = "Save",
            AutoSize = true
        };
        saveButton.Click += async (_, _) => await SaveAsync();

        reloadButton = new Button
        {
            Text = "Reload",
            AutoSize = true
        };
        reloadButton.Click += async (_, _) => await LoadCurrentValuesAsync();

        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(reloadButton);
        layout.Controls.Add(buttonPanel, 1, 8);

        Controls.Add(layout);

        Shown += async (_, _) => await LoadCurrentValuesAsync();
        FormClosing += OnFormClosing;
    }

    public async Task LoadCurrentValuesAsync()
    {
        if (isBusy || IsDisposed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var configuration = await configurationStore.LoadAsync();
            configuredTvValue.Text = configuration.CouchDisplayIdentity is not null
                ? $"{configuration.CouchDisplayIdentity.FriendlyName} ({configuration.CouchDisplayIdentity.StableId})"
                : configuration.CouchDisplayIdentifier?.Value ?? "Not configured";
            preferredModeValue.Text = $"{configuration.PreferredCouchWidth}x{configuration.PreferredCouchHeight} @ {configuration.PreferredCouchRefreshRateHz} Hz";
            apiPortTextBox.Text = configuration.ApiPort.ToString();
            corsOriginsTextBox.Text = string.Join(", ", configuration.CorsAllowedOrigins);
            apiTokenTextBox.Text = await apiTokenStore.GetTokenAsync();
            launchSteamCheckBox.Checked = configuration.LaunchSteamAutomatically;
            startWithWindowsCheckBox.Checked = startupRegistration.IsEnabled(ApplicationName);
        });
    }

    private async Task SaveAsync()
    {
        if (isBusy)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var configuration = await configurationStore.LoadAsync();
            var updatedConfiguration = configuration with
            {
                LaunchSteamAutomatically = launchSteamCheckBox.Checked,
                ApiPort = ParseApiPort(),
                CorsAllowedOrigins = ParseCorsOrigins()
            };

            await configurationStore.SaveAsync(updatedConfiguration);
            startupRegistration.SetEnabled(ApplicationName, startupCommandLine, startWithWindowsCheckBox.Checked);
        });
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        isBusy = true;
        saveButton.Enabled = false;
        reloadButton.Enabled = false;
        launchSteamCheckBox.Enabled = false;
        startWithWindowsCheckBox.Enabled = false;
        apiPortTextBox.Enabled = false;
        corsOriginsTextBox.Enabled = false;

        try
        {
            await action();
        }
        finally
        {
            isBusy = false;
            saveButton.Enabled = true;
            reloadButton.Enabled = true;
            launchSteamCheckBox.Enabled = true;
            startWithWindowsCheckBox.Enabled = true;
            apiPortTextBox.Enabled = true;
            corsOriginsTextBox.Enabled = true;
        }
    }

    private static Label AddReadOnlyRow(TableLayoutPanel layout, int rowIndex, string title)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
        };

        var valueLabel = new Label
        {
            Text = "-",
            AutoSize = true,
            MaximumSize = new Size(260, 0)
        };

        layout.Controls.Add(titleLabel, 0, rowIndex);
        layout.Controls.Add(valueLabel, 1, rowIndex);
        return valueLabel;
    }

    private static TextBox AddTextRow(TableLayoutPanel layout, int rowIndex, string title, bool isReadOnly = false)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
        };

        var valueTextBox = new TextBox
        {
            Width = 340,
            ReadOnly = isReadOnly
        };

        layout.Controls.Add(titleLabel, 0, rowIndex);
        layout.Controls.Add(valueTextBox, 1, rowIndex);
        return valueTextBox;
    }

    private int ParseApiPort() =>
        int.TryParse(apiPortTextBox.Text, out var port) ? port : 47981;

    private IReadOnlyList<string> ParseCorsOrigins() =>
        corsOriginsTextBox.Text
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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
