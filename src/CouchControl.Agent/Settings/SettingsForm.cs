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
    private readonly IAudioDeviceService audioDeviceService;
    private readonly IStartupRegistration startupRegistration;
    private readonly string startupCommandLine;
    private readonly IApiTokenStore apiTokenStore;
    private readonly Label configuredTvValue;
    private readonly Label preferredModeValue;
    private readonly TextBox apiPortTextBox;
    private readonly TextBox corsOriginsTextBox;
    private readonly TextBox apiTokenTextBox;
    private readonly ComboBox listeningInterfaceComboBox;
    private readonly ComboBox couchAudioDeviceComboBox;
    private readonly ComboBox desktopAudioDeviceComboBox;
    private readonly Label firewallRuleStatusValue;
    private readonly CheckBox launchSteamCheckBox;
    private readonly CheckBox automaticRecoveryCheckBox;
    private readonly CheckBox startWithWindowsCheckBox;
    private readonly ListView pairedDevicesListView;
    private readonly Button revokeDeviceButton;
    private readonly Button recreateFirewallRuleButton;
    private readonly Button removeFirewallRuleButton;
    private readonly Button saveButton;
    private readonly Button reloadButton;
    private readonly ILocalNetworkInterfaceProvider networkInterfaceProvider;
    private readonly IWindowsFirewallRuleManager firewallRuleManager;

    private bool isBusy;

    public SettingsForm(
        IAgentConfigurationStore configurationStore,
        IAudioDeviceService audioDeviceService,
        IStartupRegistration startupRegistration,
        string startupCommandLine,
        IApiTokenStore apiTokenStore,
        ILocalNetworkInterfaceProvider networkInterfaceProvider,
        IWindowsFirewallRuleManager firewallRuleManager)
    {
        this.configurationStore = configurationStore;
        this.audioDeviceService = audioDeviceService;
        this.startupRegistration = startupRegistration;
        this.startupCommandLine = startupCommandLine;
        this.apiTokenStore = apiTokenStore;
        this.networkInterfaceProvider = networkInterfaceProvider;
        this.firewallRuleManager = firewallRuleManager;

        Text = "Couch Control Settings";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(820, 680);
        MinimumSize = new Size(760, 620);

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
        listeningInterfaceComboBox = AddComboRow(layout, 3, "Listen on");
        couchAudioDeviceComboBox = AddComboRow(layout, 4, "Couch audio");
        desktopAudioDeviceComboBox = AddComboRow(layout, 5, "Desktop audio");
        corsOriginsTextBox = AddTextRow(layout, 6, "CORS origins");
        apiTokenTextBox = AddTextRow(layout, 7, "API token", isReadOnly: true);
        firewallRuleStatusValue = AddReadOnlyRow(layout, 8, "Firewall rule");

        launchSteamCheckBox = new CheckBox
        {
            Text = "Launch Steam automatically",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        layout.Controls.Add(launchSteamCheckBox, 1, 7);
        layout.SetRow(launchSteamCheckBox, 9);

        automaticRecoveryCheckBox = new CheckBox
        {
            Text = "Automatically recover interrupted display operations (future option)",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        layout.Controls.Add(automaticRecoveryCheckBox, 1, 8);
        layout.SetRow(automaticRecoveryCheckBox, 10);

        startWithWindowsCheckBox = new CheckBox
        {
            Text = "Start with Windows",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        layout.Controls.Add(startWithWindowsCheckBox, 1, 9);
        layout.SetRow(startWithWindowsCheckBox, 11);

        var firewallButtonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        recreateFirewallRuleButton = new Button
        {
            Text = "Create or Recreate Firewall Rule",
            AutoSize = true
        };
        recreateFirewallRuleButton.Click += async (_, _) => await RecreateFirewallRuleAsync();

        removeFirewallRuleButton = new Button
        {
            Text = "Remove Firewall Rule",
            AutoSize = true
        };
        removeFirewallRuleButton.Click += async (_, _) => await RemoveFirewallRuleAsync();

        firewallButtonPanel.Controls.Add(recreateFirewallRuleButton);
        firewallButtonPanel.Controls.Add(removeFirewallRuleButton);
        layout.Controls.Add(firewallButtonPanel, 1, 12);

        var helpLabel = new Label
        {
            Text = "TV selection and display mode are still configured through CouchControl.Cli. Audio devices can be selected here. Restart the agent after changing the API port or listening interface. Firewall changes prompt for elevation only because Windows Defender Firewall is system-wide.",
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Margin = new Padding(0, 12, 0, 0)
        };
        layout.Controls.Add(helpLabel, 1, 13);

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
        layout.Controls.Add(buttonPanel, 1, 14);

        var pairedDevicesLabel = new Label
        {
            Text = "Paired devices",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 20, 0, 4)
        };
        layout.Controls.Add(pairedDevicesLabel, 0, 15);

        pairedDevicesListView = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            Width = 500,
            Height = 160
        };
        pairedDevicesListView.Columns.Add("Device", 180);
        pairedDevicesListView.Columns.Add("Paired", 140);
        pairedDevicesListView.Columns.Add("Last seen", 140);
        layout.Controls.Add(pairedDevicesListView, 1, 15);

        revokeDeviceButton = new Button
        {
            Text = "Revoke Selected Device",
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0)
        };
        revokeDeviceButton.Click += async (_, _) => await RevokeSelectedDeviceAsync();
        pairedDevicesListView.SelectedIndexChanged += (_, _) => revokeDeviceButton.Enabled = pairedDevicesListView.SelectedItems.Count > 0 && !isBusy;
        layout.Controls.Add(revokeDeviceButton, 1, 16);

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
            LoadListeningInterfaceOptions(configuration.ApiListeningInterfaceId);
            await LoadAudioDeviceOptionsAsync(configuration.CouchAudioDeviceId, configuration.DesktopAudioDeviceId);
            corsOriginsTextBox.Text = string.Join(", ", configuration.CorsAllowedOrigins);
            apiTokenTextBox.Text = await apiTokenStore.GetTokenAsync();
            firewallRuleStatusValue.Text = firewallRuleManager.GetStatus(configuration.ApiPort).StatusText;
            launchSteamCheckBox.Checked = configuration.LaunchSteamAutomatically;
            automaticRecoveryCheckBox.Checked = configuration.AutomaticallyRecoverInterruptedDisplayOperations;
            startWithWindowsCheckBox.Checked = startupRegistration.IsEnabled(ApplicationName);
            await LoadPairedDevicesAsync();
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
                AutomaticallyRecoverInterruptedDisplayOperations = automaticRecoveryCheckBox.Checked,
                ApiPort = ParseApiPort(),
                ApiListeningInterfaceId = ParseListeningInterfaceId(),
                CouchAudioDeviceId = ParseSelectedAudioDeviceId(couchAudioDeviceComboBox),
                CouchAudioDeviceName = ParseSelectedAudioDeviceName(couchAudioDeviceComboBox),
                DesktopAudioDeviceId = ParseSelectedAudioDeviceId(desktopAudioDeviceComboBox),
                DesktopAudioDeviceName = ParseSelectedAudioDeviceName(desktopAudioDeviceComboBox),
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
        automaticRecoveryCheckBox.Enabled = false;
        startWithWindowsCheckBox.Enabled = false;
        apiPortTextBox.Enabled = false;
        listeningInterfaceComboBox.Enabled = false;
        couchAudioDeviceComboBox.Enabled = false;
        desktopAudioDeviceComboBox.Enabled = false;
        corsOriginsTextBox.Enabled = false;
        pairedDevicesListView.Enabled = false;
        revokeDeviceButton.Enabled = false;
        recreateFirewallRuleButton.Enabled = false;
        removeFirewallRuleButton.Enabled = false;

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
            automaticRecoveryCheckBox.Enabled = true;
            startWithWindowsCheckBox.Enabled = true;
            apiPortTextBox.Enabled = true;
            listeningInterfaceComboBox.Enabled = true;
            couchAudioDeviceComboBox.Enabled = true;
            desktopAudioDeviceComboBox.Enabled = true;
            corsOriginsTextBox.Enabled = true;
            pairedDevicesListView.Enabled = true;
            revokeDeviceButton.Enabled = pairedDevicesListView.SelectedItems.Count > 0;
            recreateFirewallRuleButton.Enabled = true;
            removeFirewallRuleButton.Enabled = true;
        }
    }

    private async Task LoadPairedDevicesAsync()
    {
        pairedDevicesListView.Items.Clear();

        var devices = await apiTokenStore.GetPairedDevicesAsync();
        foreach (var device in devices.OrderBy(static device => device.DeviceName, StringComparer.OrdinalIgnoreCase))
        {
            var item = new ListViewItem(device.DeviceName)
            {
                Tag = device.DeviceId
            };
            item.SubItems.Add(device.PairedAtUtc.LocalDateTime.ToString("g"));
            item.SubItems.Add(device.LastSeenAtUtc?.LocalDateTime.ToString("g") ?? "Never");
            pairedDevicesListView.Items.Add(item);
        }

        revokeDeviceButton.Enabled = pairedDevicesListView.SelectedItems.Count > 0;
    }

    private async Task RevokeSelectedDeviceAsync()
    {
        if (isBusy || pairedDevicesListView.SelectedItems.Count == 0)
        {
            return;
        }

        var selectedItem = pairedDevicesListView.SelectedItems[0];
        var deviceId = selectedItem.Tag as string;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await apiTokenStore.RevokeDeviceAsync(deviceId);
            await LoadPairedDevicesAsync();
        });
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

    private static ComboBox AddComboRow(TableLayoutPanel layout, int rowIndex, string title)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
        };

        var comboBox = new ComboBox
        {
            Width = 420,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        layout.Controls.Add(titleLabel, 0, rowIndex);
        layout.Controls.Add(comboBox, 1, rowIndex);
        return comboBox;
    }

    private int ParseApiPort() =>
        int.TryParse(apiPortTextBox.Text, out var port) ? port : 47981;

    private string? ParseListeningInterfaceId()
    {
        if (listeningInterfaceComboBox.SelectedItem is not ListeningInterfaceItem selectedItem ||
            string.Equals(selectedItem.Id, AgentApiListeningInterface.Automatic, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return selectedItem.Id;
    }

    private IReadOnlyList<string> ParseCorsOrigins() =>
        corsOriginsTextBox.Text
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? ParseSelectedAudioDeviceId(ComboBox comboBox) =>
        comboBox.SelectedItem is AudioDeviceItem item && !string.IsNullOrWhiteSpace(item.Id)
            ? item.Id
            : null;

    private static string? ParseSelectedAudioDeviceName(ComboBox comboBox) =>
        comboBox.SelectedItem is AudioDeviceItem item && !string.IsNullOrWhiteSpace(item.Id)
            ? item.FriendlyName
            : null;

    private void LoadListeningInterfaceOptions(string? selectedInterfaceId)
    {
        listeningInterfaceComboBox.Items.Clear();
        var interfaces = networkInterfaceProvider.GetInterfaces();
        var items = new List<ListeningInterfaceItem>
        {
            new(AgentApiListeningInterface.Automatic, "Automatic (recommended)")
        };

        items.AddRange(interfaces.Select(static adapter =>
            new ListeningInterfaceItem(
                adapter.Id,
                $"{adapter.Name} ({string.Join(", ", adapter.LanIpv4Addresses)}){(adapter.IsRecommended ? " [recommended]" : string.Empty)}")));

        listeningInterfaceComboBox.Items.AddRange(items.Cast<object>().ToArray());

        var requestedId = string.IsNullOrWhiteSpace(selectedInterfaceId)
            ? AgentApiListeningInterface.Automatic
            : selectedInterfaceId;

        listeningInterfaceComboBox.SelectedItem = items.FirstOrDefault(item => string.Equals(item.Id, requestedId, StringComparison.OrdinalIgnoreCase))
            ?? items[0];
    }

    private async Task LoadAudioDeviceOptionsAsync(string? selectedCouchAudioDeviceId, string? selectedDesktopAudioDeviceId)
    {
        try
        {
            var devices = await audioDeviceService.GetPlaybackDevicesAsync();
            var items = new List<AudioDeviceItem>
            {
                new(null, null, "Not configured")
            };

            items.AddRange(devices.Select(static device =>
                new AudioDeviceItem(
                    device.Id,
                    device.FriendlyName,
                    device.IsDefault ? $"{device.FriendlyName} (Default)" : device.FriendlyName)));

            LoadAudioDeviceOptions(couchAudioDeviceComboBox, items, selectedCouchAudioDeviceId);
            LoadAudioDeviceOptions(desktopAudioDeviceComboBox, items, selectedDesktopAudioDeviceId);
        }
        catch
        {
            LoadAudioDeviceOptions(
                couchAudioDeviceComboBox,
                CreateUnavailableAudioDeviceItems(selectedCouchAudioDeviceId),
                selectedCouchAudioDeviceId);
            LoadAudioDeviceOptions(
                desktopAudioDeviceComboBox,
                CreateUnavailableAudioDeviceItems(selectedDesktopAudioDeviceId),
                selectedDesktopAudioDeviceId);
        }
    }

    private static void LoadAudioDeviceOptions(ComboBox comboBox, IReadOnlyList<AudioDeviceItem> items, string? selectedDeviceId)
    {
        comboBox.Items.Clear();
        comboBox.Items.AddRange(items.Cast<object>().ToArray());

        comboBox.SelectedItem = items.FirstOrDefault(item => string.Equals(item.Id, selectedDeviceId, StringComparison.OrdinalIgnoreCase))
            ?? items[0];
    }

    private static IReadOnlyList<AudioDeviceItem> CreateUnavailableAudioDeviceItems(string? selectedDeviceId)
    {
        var items = new List<AudioDeviceItem>
        {
            new(null, null, "Not configured"),
            new(null, null, "Audio devices unavailable")
        };

        if (!string.IsNullOrWhiteSpace(selectedDeviceId))
        {
            items.Add(new AudioDeviceItem(selectedDeviceId, selectedDeviceId, $"{selectedDeviceId} (saved)"));
        }

        return items;
    }

    private async Task RecreateFirewallRuleAsync()
    {
        if (isBusy)
        {
            return;
        }

        if (MessageBox.Show(
                "Windows Defender Firewall rules are system-wide, so Windows will prompt for administrator approval only for this firewall operation. The CouchControl agent itself will stay unelevated. Continue?",
                "Firewall Elevation Required",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information) != DialogResult.Yes)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = firewallRuleManager.RecreateRule(ParseApiPort());
            firewallRuleStatusValue.Text = firewallRuleManager.GetStatus(ParseApiPort()).StatusText;
            MessageBox.Show(result.Message, "Firewall Rule", MessageBoxButtons.OK, result.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            await Task.CompletedTask;
        });
    }

    private async Task RemoveFirewallRuleAsync()
    {
        if (isBusy)
        {
            return;
        }

        if (MessageBox.Show(
                "Removing the firewall rule also requires administrator approval because Windows Defender Firewall is shared system state. Continue?",
                "Firewall Elevation Required",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information) != DialogResult.Yes)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = firewallRuleManager.RemoveRule(ParseApiPort());
            firewallRuleStatusValue.Text = firewallRuleManager.GetStatus(ParseApiPort()).StatusText;
            MessageBox.Show(result.Message, "Firewall Rule", MessageBoxButtons.OK, result.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            await Task.CompletedTask;
        });
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

    private sealed record ListeningInterfaceItem(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record AudioDeviceItem(string? Id, string? FriendlyName, string Label)
    {
        public override string ToString() => Label;
    }
}
