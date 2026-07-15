using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using CouchControl.Windows.Startup;

namespace CouchControl.Agent.Settings;

public sealed class SettingsForm : Form
{
    private const string ApplicationName = "CouchControl.Agent";

    private readonly IAgentConfigurationStore configurationStore;
    private readonly IStartupRegistration startupRegistration;
    private readonly string startupCommandLine;
    private readonly Label configuredTvValue;
    private readonly Label preferredModeValue;
    private readonly CheckBox launchSteamCheckBox;
    private readonly CheckBox startWithWindowsCheckBox;
    private readonly Button saveButton;
    private readonly Button reloadButton;

    private bool isBusy;

    public SettingsForm(
        IAgentConfigurationStore configurationStore,
        IStartupRegistration startupRegistration,
        string startupCommandLine)
    {
        this.configurationStore = configurationStore;
        this.startupRegistration = startupRegistration;
        this.startupCommandLine = startupCommandLine;

        Text = "Couch Control Settings";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(520, 260);
        MinimumSize = new Size(480, 240);

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

        launchSteamCheckBox = new CheckBox
        {
            Text = "Launch Steam automatically",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        layout.Controls.Add(launchSteamCheckBox, 1, 2);

        startWithWindowsCheckBox = new CheckBox
        {
            Text = "Start with Windows",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        layout.Controls.Add(startWithWindowsCheckBox, 1, 3);

        var helpLabel = new Label
        {
            Text = "TV selection and display mode are still configured through CouchControl.Cli.",
            AutoSize = true,
            MaximumSize = new Size(280, 0),
            Margin = new Padding(0, 12, 0, 0)
        };
        layout.Controls.Add(helpLabel, 1, 4);

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
        layout.Controls.Add(buttonPanel, 1, 5);

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
                LaunchSteamAutomatically = launchSteamCheckBox.Checked
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
