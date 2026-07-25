using System.Linq;
using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using CouchControl.Core.Orchestration;
using CouchControl.Windows.AgentApi;
using CouchControl.Windows.Startup;

namespace CouchControl.Agent.Settings;

public sealed class SettingsForm : Form
{
    private const string ApplicationName = "CouchControl.Agent";

    private readonly IAgentConfigurationStore configurationStore;
    private readonly IDisplayManager displayManager;
    private readonly IDisplaySnapshotStore snapshotStore;
    private readonly Func<Task> saveSnapshotAsync;
    private readonly Func<Task> clearSnapshotAsync;
    private readonly IAudioDeviceService audioDeviceService;
    private readonly IStartupRegistration startupRegistration;
    private readonly string startupCommandLine;
    private readonly IApiTokenStore apiTokenStore;
    private readonly ComboBox couchDisplayComboBox;
    private readonly TextBox preferredWidthTextBox;
    private readonly TextBox preferredHeightTextBox;
    private readonly TextBox preferredRefreshRateTextBox;
    private readonly TextBox steamExecutablePathTextBox;
    private readonly TextBox heroicExecutablePathTextBox;
    private readonly TextBox apiPortTextBox;
    private readonly TextBox corsOriginsTextBox;
    private readonly TextBox apiTokenTextBox;
    private readonly ComboBox listeningInterfaceComboBox;
    private readonly ComboBox couchAudioDeviceComboBox;
    private readonly ComboBox desktopAudioDeviceComboBox;
    private readonly TextBox tvPreparationCommandTextBox;
    private readonly TextBox tvPreparationDelayTextBox;
    private readonly TextBox couchAudioCommandTextBox;
    private readonly TextBox desktopAudioCommandTextBox;
    private readonly Label snapshotStatusValue;
    private readonly Label firewallRuleStatusValue;
    private readonly ComboBox couchLauncherComboBox;
    private readonly CheckBox automaticRecoveryCheckBox;
    private readonly CheckBox startWithWindowsCheckBox;
    private readonly ListView pairedDevicesListView;
    private readonly Button revokeDeviceButton;
    private readonly Button checkFirewallRuleButton;
    private readonly Button recreateFirewallRuleButton;
    private readonly Button removeFirewallRuleButton;
    private readonly Button saveSnapshotButton;
    private readonly Button clearSnapshotButton;
    private readonly Button showWakeSignInInstructionsButton;
    private readonly Button saveButton;
    private readonly Button reloadButton;
    private readonly ILocalNetworkInterfaceProvider networkInterfaceProvider;
    private readonly IWindowsFirewallRuleManager firewallRuleManager;

    private bool isBusy;

    public SettingsForm(
        IAgentConfigurationStore configurationStore,
        IDisplayManager displayManager,
        IDisplaySnapshotStore snapshotStore,
        Func<Task> saveSnapshotAsync,
        Func<Task> clearSnapshotAsync,
        IAudioDeviceService audioDeviceService,
        IStartupRegistration startupRegistration,
        string startupCommandLine,
        IApiTokenStore apiTokenStore,
        ILocalNetworkInterfaceProvider networkInterfaceProvider,
        IWindowsFirewallRuleManager firewallRuleManager)
    {
        this.configurationStore = configurationStore;
        this.displayManager = displayManager;
        this.snapshotStore = snapshotStore;
        this.saveSnapshotAsync = saveSnapshotAsync;
        this.clearSnapshotAsync = clearSnapshotAsync;
        this.audioDeviceService = audioDeviceService;
        this.startupRegistration = startupRegistration;
        this.startupCommandLine = startupCommandLine;
        this.apiTokenStore = apiTokenStore;
        this.networkInterfaceProvider = networkInterfaceProvider;
        this.firewallRuleManager = firewallRuleManager;

        Text = "Couch Control Settings";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(900, 720);
        MinimumSize = new Size(820, 640);

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12)
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill
        };

        var displayLayout = CreateTabLayout();
        couchDisplayComboBox = AddComboRow(displayLayout, 0, "Couch TV");
        (preferredWidthTextBox, preferredHeightTextBox, preferredRefreshRateTextBox) = AddModeRow(displayLayout, 1, "Couch mode");
        tvPreparationCommandTextBox = AddTextRow(displayLayout, 2, "TV prep command");
        tvPreparationDelayTextBox = AddTextRow(displayLayout, 3, "TV prep delay ms");
        snapshotStatusValue = AddReadOnlyRow(displayLayout, 4, "Desktop snapshot");
        var snapshotButtonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        saveSnapshotButton = new Button
        {
            Text = "Save Current Desktop Snapshot",
            AutoSize = true
        };
        saveSnapshotButton.Click += async (_, _) => await RunSnapshotActionAsync(saveSnapshotButton, saveSnapshotAsync);
        clearSnapshotButton = new Button
        {
            Text = "Clear Saved Snapshot",
            AutoSize = true
        };
        clearSnapshotButton.Click += async (_, _) => await RunSnapshotActionAsync(clearSnapshotButton, clearSnapshotAsync);
        snapshotButtonPanel.Controls.Add(saveSnapshotButton);
        snapshotButtonPanel.Controls.Add(clearSnapshotButton);
        displayLayout.Controls.Add(snapshotButtonPanel, 1, 5);
        AddHelpText(displayLayout, 6, "The desktop snapshot is used to restore your normal monitor layout when leaving couch mode.");
        tabs.TabPages.Add(CreateTabPage("Display", displayLayout));

        var audioLayout = CreateTabLayout();
        couchAudioDeviceComboBox = AddComboRow(audioLayout, 0, "Couch audio");
        desktopAudioDeviceComboBox = AddComboRow(audioLayout, 1, "Desktop audio");
        couchAudioCommandTextBox = AddTextRow(audioLayout, 2, "Couch audio cmd");
        desktopAudioCommandTextBox = AddTextRow(audioLayout, 3, "Desktop audio cmd");
        AddHelpText(audioLayout, 4, "Audio commands are optional fallbacks. Device selection is preferred when available.");
        tabs.TabPages.Add(CreateTabPage("Audio", audioLayout));

        var appsLayout = CreateTabLayout();
        couchLauncherComboBox = AddComboRow(appsLayout, 0, "Couch launcher");
        couchLauncherComboBox.Items.AddRange(
        [
            "None",
            "Steam — Big Picture",
            "Heroic — Console Mode"
        ]);
        steamExecutablePathTextBox = AddTextRow(appsLayout, 1, "Steam path override");
        heroicExecutablePathTextBox = AddTextRow(appsLayout, 2, "Heroic path override");
        AddHelpText(appsLayout, 3, "Leave paths empty to detect installed launchers automatically.");
        tabs.TabPages.Add(CreateTabPage("Apps", appsLayout));

        var networkLayout = CreateTabLayout();
        apiPortTextBox = AddTextRow(networkLayout, 0, "API port");
        listeningInterfaceComboBox = AddComboRow(networkLayout, 1, "Listen on");
        corsOriginsTextBox = AddTextRow(networkLayout, 2, "CORS origins");
        apiTokenTextBox = AddTextRow(networkLayout, 3, "API token", isReadOnly: true);
        firewallRuleStatusValue = AddReadOnlyRow(networkLayout, 4, "Firewall rule");

        var firewallButtonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        checkFirewallRuleButton = new Button
        {
            Text = "Check Firewall Status",
            AutoSize = true
        };
        checkFirewallRuleButton.Click += async (_, _) => await CheckFirewallRuleAsync();

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

        firewallButtonPanel.Controls.Add(checkFirewallRuleButton);
        firewallButtonPanel.Controls.Add(recreateFirewallRuleButton);
        firewallButtonPanel.Controls.Add(removeFirewallRuleButton);
        networkLayout.Controls.Add(firewallButtonPanel, 1, 5);
        AddHelpText(networkLayout, 6, "Restart the agent after changing the API port or listening interface. Firewall status checks and changes use Windows PowerShell only when you click a firewall button.");
        tabs.TabPages.Add(CreateTabPage("Network", networkLayout));

        var systemLayout = CreateTabLayout();

        automaticRecoveryCheckBox = new CheckBox
        {
            Text = "Automatically recover interrupted display operations (future option)",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        systemLayout.Controls.Add(automaticRecoveryCheckBox, 1, 0);

        startWithWindowsCheckBox = new CheckBox
        {
            Text = "Start with Windows",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        systemLayout.Controls.Add(startWithWindowsCheckBox, 1, 1);

        var wakeSignInLabel = new Label
        {
            Text = "Wake sign-in",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 20, 0, 4)
        };
        systemLayout.Controls.Add(wakeSignInLabel, 0, 2);

        var wakeSignInPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 16, 0, 0)
        };
        var wakeSignInHelpText = new Label
        {
            Text = "Wake-on-LAN can power on the PC, but Windows may still require your PIN after sleep. For couch use, change this in Windows Settings yourself instead of having CouchControl launch system settings.",
            AutoSize = true,
            MaximumSize = new Size(560, 0)
        };
        showWakeSignInInstructionsButton = new Button
        {
            Text = "Show Windows Sign-in Instructions",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        showWakeSignInInstructionsButton.Click += (_, _) => ShowWakeSignInInstructions();
        wakeSignInPanel.Controls.Add(wakeSignInHelpText);
        wakeSignInPanel.Controls.Add(showWakeSignInInstructionsButton);
        systemLayout.Controls.Add(wakeSignInPanel, 1, 2);

        var pairedDevicesLabel = new Label
        {
            Text = "Paired devices",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 20, 0, 4)
        };
        systemLayout.Controls.Add(pairedDevicesLabel, 0, 3);

        pairedDevicesListView = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            Width = 560,
            Height = 240
        };
        pairedDevicesListView.Columns.Add("Device", 200);
        pairedDevicesListView.Columns.Add("Paired", 160);
        pairedDevicesListView.Columns.Add("Last seen", 160);
        systemLayout.Controls.Add(pairedDevicesListView, 1, 3);

        revokeDeviceButton = new Button
        {
            Text = "Revoke Selected Device",
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0)
        };
        revokeDeviceButton.Click += async (_, _) => await RevokeSelectedDeviceAsync();
        pairedDevicesListView.SelectedIndexChanged += (_, _) => revokeDeviceButton.Enabled = pairedDevicesListView.SelectedItems.Count > 0 && !isBusy;
        systemLayout.Controls.Add(revokeDeviceButton, 1, 4);
        tabs.TabPages.Add(CreateTabPage("System", systemLayout));

        var bottomButtonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0)
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

        bottomButtonPanel.Controls.Add(saveButton);
        bottomButtonPanel.Controls.Add(reloadButton);

        rootLayout.Controls.Add(tabs, 0, 0);
        rootLayout.Controls.Add(bottomButtonPanel, 0, 1);
        Controls.Add(rootLayout);

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
            await LoadDisplayOptionsAsync(configuration);
            preferredWidthTextBox.Text = configuration.PreferredCouchWidth.ToString();
            preferredHeightTextBox.Text = configuration.PreferredCouchHeight.ToString();
            preferredRefreshRateTextBox.Text = configuration.PreferredCouchRefreshRateHz.ToString("0.##");
            steamExecutablePathTextBox.Text = configuration.SteamExecutablePath ?? string.Empty;
            heroicExecutablePathTextBox.Text = configuration.HeroicExecutablePath ?? string.Empty;
            apiPortTextBox.Text = configuration.ApiPort.ToString();
            LoadListeningInterfaceOptions(configuration.ApiListeningInterfaceId);
            await LoadAudioDeviceOptionsAsync(configuration.CouchAudioDeviceId, configuration.DesktopAudioDeviceId);
            tvPreparationCommandTextBox.Text = configuration.TvPreparationCommand ?? string.Empty;
            tvPreparationDelayTextBox.Text = configuration.TvPreparationDelayMs.ToString();
            couchAudioCommandTextBox.Text = configuration.CouchAudioCommand ?? string.Empty;
            desktopAudioCommandTextBox.Text = configuration.DesktopAudioCommand ?? string.Empty;
            await LoadSnapshotStatusAsync();
            corsOriginsTextBox.Text = string.Join(", ", configuration.CorsAllowedOrigins);
            apiTokenTextBox.Text = await apiTokenStore.GetTokenAsync();
            firewallRuleStatusValue.Text = "Not checked";
            couchLauncherComboBox.SelectedIndex = (int)configuration.CouchLauncher;
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
            var selectedDisplay = ParseSelectedDisplay(couchDisplayComboBox);
            var updatedConfiguration = configuration with
            {
                CouchDisplayIdentifier = selectedDisplay is null ? null : new DisplayIdentifier(selectedDisplay.DevicePath!),
                CouchDisplayIdentity = selectedDisplay?.Identity,
                PreferredCouchWidth = ParsePositiveInt(preferredWidthTextBox, configuration.PreferredCouchWidth),
                PreferredCouchHeight = ParsePositiveInt(preferredHeightTextBox, configuration.PreferredCouchHeight),
                PreferredCouchRefreshRateHz = ParsePositiveDecimal(preferredRefreshRateTextBox, configuration.PreferredCouchRefreshRateHz),
                CouchLauncher = (CouchLauncher)Math.Max(0, couchLauncherComboBox.SelectedIndex),
                LaunchSteamAutomatically = couchLauncherComboBox.SelectedIndex == (int)CouchLauncher.SteamBigPicture,
                AutomaticallyRecoverInterruptedDisplayOperations = automaticRecoveryCheckBox.Checked,
                SteamExecutablePath = ParseOptionalText(steamExecutablePathTextBox),
                HeroicExecutablePath = ParseOptionalText(heroicExecutablePathTextBox),
                TvPreparationCommand = ParseOptionalText(tvPreparationCommandTextBox),
                TvPreparationDelayMs = ParseNonNegativeInt(tvPreparationDelayTextBox, configuration.TvPreparationDelayMs),
                CouchAudioCommand = ParseOptionalText(couchAudioCommandTextBox),
                DesktopAudioCommand = ParseOptionalText(desktopAudioCommandTextBox),
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
        couchDisplayComboBox.Enabled = false;
        preferredWidthTextBox.Enabled = false;
        preferredHeightTextBox.Enabled = false;
        preferredRefreshRateTextBox.Enabled = false;
        steamExecutablePathTextBox.Enabled = false;
        heroicExecutablePathTextBox.Enabled = false;
        couchLauncherComboBox.Enabled = false;
        automaticRecoveryCheckBox.Enabled = false;
        startWithWindowsCheckBox.Enabled = false;
        apiPortTextBox.Enabled = false;
        listeningInterfaceComboBox.Enabled = false;
        couchAudioDeviceComboBox.Enabled = false;
        desktopAudioDeviceComboBox.Enabled = false;
        tvPreparationCommandTextBox.Enabled = false;
        tvPreparationDelayTextBox.Enabled = false;
        couchAudioCommandTextBox.Enabled = false;
        desktopAudioCommandTextBox.Enabled = false;
        corsOriginsTextBox.Enabled = false;
        pairedDevicesListView.Enabled = false;
        revokeDeviceButton.Enabled = false;
        checkFirewallRuleButton.Enabled = false;
        recreateFirewallRuleButton.Enabled = false;
        removeFirewallRuleButton.Enabled = false;
        saveSnapshotButton.Enabled = false;
        clearSnapshotButton.Enabled = false;
        showWakeSignInInstructionsButton.Enabled = false;

        try
        {
            await action();
        }
        finally
        {
            isBusy = false;
            saveButton.Enabled = true;
            reloadButton.Enabled = true;
            couchDisplayComboBox.Enabled = true;
            preferredWidthTextBox.Enabled = true;
            preferredHeightTextBox.Enabled = true;
            preferredRefreshRateTextBox.Enabled = true;
            steamExecutablePathTextBox.Enabled = true;
            heroicExecutablePathTextBox.Enabled = true;
            couchLauncherComboBox.Enabled = true;
            automaticRecoveryCheckBox.Enabled = true;
            startWithWindowsCheckBox.Enabled = true;
            apiPortTextBox.Enabled = true;
            listeningInterfaceComboBox.Enabled = true;
            couchAudioDeviceComboBox.Enabled = true;
            desktopAudioDeviceComboBox.Enabled = true;
            tvPreparationCommandTextBox.Enabled = true;
            tvPreparationDelayTextBox.Enabled = true;
            couchAudioCommandTextBox.Enabled = true;
            desktopAudioCommandTextBox.Enabled = true;
            corsOriginsTextBox.Enabled = true;
            pairedDevicesListView.Enabled = true;
            revokeDeviceButton.Enabled = pairedDevicesListView.SelectedItems.Count > 0;
            checkFirewallRuleButton.Enabled = true;
            recreateFirewallRuleButton.Enabled = true;
            removeFirewallRuleButton.Enabled = true;
            saveSnapshotButton.Enabled = true;
            clearSnapshotButton.Enabled = true;
            showWakeSignInInstructionsButton.Enabled = true;
        }
    }

    private static void ShowWakeSignInInstructions()
    {
        MessageBox.Show(
            "To avoid needing a keyboard after Wake-on-LAN:\n\n" +
            "1. Open Windows Settings.\n" +
            "2. Go to Accounts > Sign-in options.\n" +
            "3. Under Additional settings, set \"If you've been away, when should Windows require you to sign in again?\" to \"Never\".\n\n" +
            "If you prefer to keep sign-in required, use Windows Hello face or fingerprint unlock instead.",
            "Wake sign-in instructions",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async Task RunSnapshotActionAsync(Button button, Func<Task> action)
    {
        if (isBusy || !button.Enabled)
        {
            return;
        }

        button.Enabled = false;
        try
        {
            await action();
            await LoadSnapshotStatusAsync();
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private async Task LoadSnapshotStatusAsync()
    {
        try
        {
            var snapshot = await snapshotStore.LoadLastDesktopSnapshotAsync();
            snapshotStatusValue.Text = snapshot is null
                ? "Not saved"
                : $"Saved {snapshot.CapturedAtUtc.LocalDateTime:g} ({snapshot.Paths.Count(static path => path.IsActive)} active display path(s))";
        }
        catch (Exception ex)
        {
            snapshotStatusValue.Text = $"Unavailable: {ex.Message}";
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

    private static TableLayoutPanel CreateTabLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(16),
            AutoScroll = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
    }

    private static TabPage CreateTabPage(string title, Control content)
    {
        var page = new TabPage(title)
        {
            Padding = new Padding(0)
        };
        page.Controls.Add(content);
        return page;
    }

    private static void AddHelpText(TableLayoutPanel layout, int rowIndex, string text)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 8, 0, 0)
        };
        layout.Controls.Add(label, 1, rowIndex);
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

    private static (TextBox Width, TextBox Height, TextBox RefreshRate) AddModeRow(TableLayoutPanel layout, int rowIndex, string title)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
        };

        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = Padding.Empty
        };

        var widthTextBox = CreateModeTextBox();
        var heightTextBox = CreateModeTextBox();
        var refreshRateTextBox = CreateModeTextBox();

        panel.Controls.Add(widthTextBox);
        panel.Controls.Add(new Label { Text = "x", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 4, 4, 0) });
        panel.Controls.Add(heightTextBox);
        panel.Controls.Add(new Label { Text = "@", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 4, 4, 0) });
        panel.Controls.Add(refreshRateTextBox);
        panel.Controls.Add(new Label { Text = "Hz", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 4, 0, 0) });

        layout.Controls.Add(titleLabel, 0, rowIndex);
        layout.Controls.Add(panel, 1, rowIndex);

        return (widthTextBox, heightTextBox, refreshRateTextBox);
    }

    private static TextBox CreateModeTextBox() =>
        new()
        {
            Width = 80
        };

    private int ParseApiPort() =>
        int.TryParse(apiPortTextBox.Text, out var port) ? port : 47981;

    private static int ParsePositiveInt(TextBox textBox, int fallback) =>
        int.TryParse(textBox.Text, out var value) && value > 0 ? value : fallback;

    private static int ParseNonNegativeInt(TextBox textBox, int fallback) =>
        int.TryParse(textBox.Text, out var value) && value >= 0 ? value : fallback;

    private static decimal ParsePositiveDecimal(TextBox textBox, decimal fallback) =>
        decimal.TryParse(textBox.Text, out var value) && value > 0 ? value : fallback;

    private static string? ParseOptionalText(TextBox textBox) =>
        string.IsNullOrWhiteSpace(textBox.Text) ? null : textBox.Text.Trim();

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

    private static DisplayItem? ParseSelectedDisplay(ComboBox comboBox) =>
        comboBox.SelectedItem is DisplayItem item && !string.IsNullOrWhiteSpace(item.DevicePath)
            ? item
            : null;

    private async Task LoadDisplayOptionsAsync(AgentConfiguration configuration)
    {
        couchDisplayComboBox.Items.Clear();

        var items = new List<DisplayItem>
        {
            new(null, null, "Not configured")
        };

        try
        {
            var displays = await displayManager.GetDisplaysAsync();
            items.AddRange(displays
                .Where(static display => !string.IsNullOrWhiteSpace(display.DevicePath))
                .OrderByDescending(static display => display.IsPrimary)
                .ThenBy(static display => display.FriendlyName, StringComparer.OrdinalIgnoreCase)
                .Select(static display =>
                {
                    var stableId = DisplayStableId.FromDevicePath(display.DevicePath);
                    var parsed = DisplayMatchingService.ParseDevicePath(display.DevicePath);
                    var identity = new CouchDisplayIdentity(
                        DevicePath: display.DevicePath ?? string.Empty,
                        FriendlyName: display.FriendlyName,
                        Manufacturer: parsed?.Manufacturer ?? string.Empty,
                        ProductCode: parsed?.ProductCode ?? string.Empty,
                        SerialOrInstance: parsed?.SerialOrInstance ?? string.Empty,
                        AdapterLuid: display.AdapterLuid ?? string.Empty,
                        TargetId: display.TargetId ?? 0)
                    {
                        StableId = stableId
                    };

                    var mode = display.CurrentMode is null
                        ? "mode unknown"
                        : $"{display.CurrentMode.Width}x{display.CurrentMode.Height} @ {display.CurrentMode.RefreshRateHz:0.##} Hz";
                    var status = display.IsPrimary ? "Primary" : display.IsActive ? "Active" : "Inactive";

                    return new DisplayItem(
                        display.DevicePath,
                        identity,
                        $"{display.FriendlyName} ({stableId}) - {status}, {mode}");
                }));
        }
        catch
        {
            items.Add(new DisplayItem(null, null, "Displays unavailable"));
        }

        if (configuration.CouchDisplayIdentity is not null &&
            !items.Any(item => string.Equals(item.DevicePath, configuration.CouchDisplayIdentity.DevicePath, StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(new DisplayItem(
                configuration.CouchDisplayIdentity.DevicePath,
                configuration.CouchDisplayIdentity,
                $"{configuration.CouchDisplayIdentity.FriendlyName} ({configuration.CouchDisplayIdentity.StableId}) - saved"));
        }
        else if (configuration.CouchDisplayIdentifier is not null &&
            !items.Any(item => string.Equals(item.DevicePath, configuration.CouchDisplayIdentifier.Value, StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(new DisplayItem(
                configuration.CouchDisplayIdentifier.Value,
                null,
                $"{configuration.CouchDisplayIdentifier.Value} - saved"));
        }

        couchDisplayComboBox.Items.AddRange(items.Cast<object>().ToArray());
        couchDisplayComboBox.SelectedItem = items.FirstOrDefault(item =>
                configuration.CouchDisplayIdentity is not null
                    ? string.Equals(item.DevicePath, configuration.CouchDisplayIdentity.DevicePath, StringComparison.OrdinalIgnoreCase)
                    : configuration.CouchDisplayIdentifier is not null &&
                      string.Equals(item.DevicePath, configuration.CouchDisplayIdentifier.Value, StringComparison.OrdinalIgnoreCase))
            ?? items[0];
    }

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

    private async Task CheckFirewallRuleAsync()
    {
        if (isBusy)
        {
            return;
        }

        await RunBusyAsync(() =>
        {
            firewallRuleStatusValue.Text = firewallRuleManager.GetStatus(ParseApiPort()).StatusText;
            return Task.CompletedTask;
        });
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
            firewallRuleStatusValue.Text = result.Succeeded ? "Present (private TCP rule)" : "Unknown";
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
            firewallRuleStatusValue.Text = result.Succeeded ? "Missing" : "Unknown";
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

    private sealed record DisplayItem(string? DevicePath, CouchDisplayIdentity? Identity, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record AudioDeviceItem(string? Id, string? FriendlyName, string Label)
    {
        public override string ToString() => Label;
    }
}
