using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using CouchControl.Agent.Logging;
using CouchControl.Agent.Settings;
using CouchControl.Agent.Status;
using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using CouchControl.Windows;
using CouchControl.Windows.AgentApi;
using CouchControl.Windows.Recovery;
using CouchControl.Windows.Runtime;
using CouchControl.Windows.Startup;
using Microsoft.Extensions.Logging;

namespace CouchControl.Agent.Hosting;

public sealed class AgentApplicationContext : ApplicationContext
{
    private const string ApplicationName = "CouchControl.Agent";
    private const string TrayIconResourceName = "CouchControl.Agent.Assets.couchcontrol.ico";

    private readonly NotifyIcon notifyIcon;
    private readonly ToolStripMenuItem activateCouchModeItem;
    private readonly ToolStripMenuItem restoreDesktopModeItem;
    private readonly ToolStripMenuItem pairDeviceItem;
    private readonly ToolStripMenuItem saveDesktopSnapshotItem;
    private readonly ToolStripMenuItem clearDesktopSnapshotItem;
    private readonly ToolStripMenuItem startWithWindowsItem;
    private readonly ToolStripMenuItem exitItem;
    private readonly Control uiInvoker;
    private readonly IAgentApiOperationService operationService;
    private readonly IAgentStatusService agentStatusService;
    private readonly IAgentConfigurationStore configurationStore;
    private readonly IDisplayManager displayManager;
    private readonly IDisplaySnapshotStore snapshotStore;
    private readonly IDisplayRecoveryCoordinator recoveryCoordinator;
    private readonly IStartupRegistration startupRegistration;
    private readonly ISingleInstanceCoordinator singleInstanceCoordinator;
    private readonly IAgentLogFileAccessor logFileAccessor;
    private readonly CouchControlPaths paths;
    private readonly ILogger<AgentApplicationContext> logger;
    private readonly IPairingService pairingService;
    private readonly string startupCommandLine;
    private readonly StatusForm statusForm;
    private readonly NetworkDiagnosticsForm diagnosticsForm;
    private readonly SettingsForm settingsForm;
    private readonly PairingCodeForm pairingCodeForm;
    private bool startupRegistrationEnabled;
    private bool firstRunSetupShown;

    public AgentApplicationContext(
        IAgentApiOperationService operationService,
        IAgentStatusService agentStatusService,
        IAgentConfigurationStore configurationStore,
        IDisplayManager displayManager,
        IDisplaySnapshotStore snapshotStore,
        IDisplayRecoveryCoordinator recoveryCoordinator,
        IStartupRegistration startupRegistration,
        ISingleInstanceCoordinator singleInstanceCoordinator,
        IAgentLogFileAccessor logFileAccessor,
        IApiTokenStore apiTokenStore,
        IPairingService pairingService,
        IAgentNetworkDiagnosticsService diagnosticsService,
        IAudioDeviceService audioDeviceService,
        ILocalNetworkInterfaceProvider networkInterfaceProvider,
        IWindowsFirewallRuleManager firewallRuleManager,
        CouchControlPaths paths,
        ILogger<AgentApplicationContext> logger)
    {
        this.operationService = operationService;
        this.agentStatusService = agentStatusService;
        this.configurationStore = configurationStore;
        this.displayManager = displayManager;
        this.snapshotStore = snapshotStore;
        this.recoveryCoordinator = recoveryCoordinator;
        this.startupRegistration = startupRegistration;
        this.singleInstanceCoordinator = singleInstanceCoordinator;
        this.logFileAccessor = logFileAccessor;
        this.pairingService = pairingService;
        this.paths = paths;
        this.logger = logger;

        startupCommandLine = CouchControl.Windows.Startup.CurrentUserStartupRegistration.BuildCommandLine(
            Environment.ProcessPath ?? throw new InvalidOperationException("Process path is unavailable."));

        uiInvoker = new Control();
        uiInvoker.CreateControl();
        startupRegistrationEnabled = startupRegistration.IsEnabled(ApplicationName);

        activateCouchModeItem = new ToolStripMenuItem("Activate Couch Mode", null, (_, _) => QueueUiAction(StartCouchMode));
        restoreDesktopModeItem = new ToolStripMenuItem("Restore Desktop Mode", null, (_, _) => QueueUiAction(StartDesktopMode));
        pairDeviceItem = new ToolStripMenuItem("Pair Device", null, (_, _) => QueueUiAction(StartPairingAsync));
        saveDesktopSnapshotItem = new ToolStripMenuItem("Save Current Desktop Snapshot", null, (_, _) => QueueUiAction(SaveCurrentDesktopSnapshotAsync));
        clearDesktopSnapshotItem = new ToolStripMenuItem("Clear Saved Desktop Snapshot", null, (_, _) => QueueUiAction(ClearDesktopSnapshotAsync));
        startWithWindowsItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => QueueUiAction(ToggleStartupRegistration))
        {
            CheckOnClick = true
        };
        exitItem = new ToolStripMenuItem("Exit", null, (_, _) => QueueUiAction(ExitApplication));

        statusForm = new StatusForm(
            agentStatusService,
            SaveCurrentDesktopSnapshotAsync,
            ClearDesktopSnapshotAsync);
        diagnosticsForm = new NetworkDiagnosticsForm(diagnosticsService);
        settingsForm = new SettingsForm(
            configurationStore,
            displayManager,
            snapshotStore,
            SaveCurrentDesktopSnapshotAsync,
            ClearDesktopSnapshotAsync,
            audioDeviceService,
            startupRegistration,
            startupCommandLine,
            apiTokenStore,
            networkInterfaceProvider,
            firewallRuleManager);
        pairingCodeForm = new PairingCodeForm(pairingService, configurationStore, networkInterfaceProvider);

        var contextMenu = new ContextMenuStrip();
        contextMenu.Opening += (_, _) => RefreshMenuState();
        contextMenu.Items.Add(new ToolStripMenuItem("Couch Control") { Enabled = false });
        contextMenu.Items.Add(activateCouchModeItem);
        contextMenu.Items.Add(restoreDesktopModeItem);
        contextMenu.Items.Add(pairDeviceItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(saveDesktopSnapshotItem);
        contextMenu.Items.Add(clearDesktopSnapshotItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(new ToolStripMenuItem("Status", null, (_, _) => QueueUiAction(ShowStatusWindow)));
        contextMenu.Items.Add(new ToolStripMenuItem("Network Diagnostics", null, (_, _) => QueueUiAction(ShowDiagnosticsWindowAsync)));
        contextMenu.Items.Add(new ToolStripMenuItem("Settings", null, (_, _) => QueueUiAction(ShowSettingsWindowAsync)));
        contextMenu.Items.Add(new ToolStripMenuItem("Show Configuration Folder", null, (_, _) => QueueUiAction(() => OpenDirectory(paths.RootDirectory))));
        contextMenu.Items.Add(new ToolStripMenuItem("View Logs", null, (_, _) => QueueUiAction(OpenLogs)));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(startWithWindowsItem);
        contextMenu.Items.Add(exitItem);

        notifyIcon = new NotifyIcon
        {
            Text = "Couch Control",
            Icon = LoadTrayIcon(),
            Visible = true,
            ContextMenuStrip = contextMenu
        };
        notifyIcon.DoubleClick += (_, _) => ShowStatusWindow();

        singleInstanceCoordinator.ActivationRequested += OnActivationRequested;
        operationService.OperationCompleted += OnOperationCompleted;
        statusForm.FormClosed += (_, _) => { };
        settingsForm.FormClosed += (_, _) => { };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            singleInstanceCoordinator.ActivationRequested -= OnActivationRequested;
            operationService.OperationCompleted -= OnOperationCompleted;
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            statusForm.Dispose();
            diagnosticsForm.Dispose();
            settingsForm.Dispose();
            pairingCodeForm.Dispose();
            uiInvoker.Dispose();
        }

        base.Dispose(disposing);
    }

    public void StartStartupRecoveryCheck() =>
        uiInvoker.BeginInvoke(async () => await RunStartupRecoveryCheckAsync());

    public void StartFirstRunSetupCheck() =>
        uiInvoker.BeginInvoke(async () => await RunFirstRunSetupCheckAsync());

    private static Icon LoadTrayIcon()
    {
        var assembly = typeof(AgentApplicationContext).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(TrayIconResourceName)
            ?? throw new InvalidOperationException($"Missing tray icon resource '{TrayIconResourceName}'.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;
        using var icon = new Icon(memory);
        return (Icon)icon.Clone();
    }

    private void RefreshMenuState()
    {
        startWithWindowsItem.Checked = startupRegistrationEnabled;
        activateCouchModeItem.Enabled = !operationService.IsOperationRunning;
        restoreDesktopModeItem.Enabled = !operationService.IsOperationRunning;
        pairDeviceItem.Enabled = !operationService.IsOperationRunning;
        saveDesktopSnapshotItem.Enabled = !operationService.IsOperationRunning;
        clearDesktopSnapshotItem.Enabled = !operationService.IsOperationRunning;
        startWithWindowsItem.Enabled = !operationService.IsOperationRunning;
        exitItem.Enabled = !operationService.IsOperationRunning;
    }

    private async Task RunFirstRunSetupCheckAsync()
    {
        try
        {
            if (firstRunSetupShown || File.Exists(paths.ConfigurationFilePath))
            {
                return;
            }

            firstRunSetupShown = true;

            logger.LogInformation(
                "Configuration file was not found at {ConfigurationFilePath}; showing first-run setup.",
                paths.ConfigurationFilePath);

            notifyIcon.ShowBalloonTip(
                5000,
                "Setup required",
                "Choose your couch TV and save a desktop snapshot before activating couch mode.",
                ToolTipIcon.Info);

            await ShowSettingsWindowAsync();

            MessageBox.Show(
                settingsForm,
                "CouchControl needs a first-run setup before it can switch displays safely.\n\n" +
                "1. In Display, choose the TV to use for Couch Mode.\n" +
                "2. Confirm the preferred TV resolution and refresh rate.\n" +
                "3. Save the current desktop snapshot so Desktop Mode can restore your monitor layout.\n" +
                "4. Optional: choose couch and desktop audio devices in Audio.\n" +
                "5. Click Save.\n\n" +
                $"Configuration will be saved to:\n{paths.ConfigurationFilePath}",
                "CouchControl First-Run Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "First-run setup check failed.");
            notifyIcon.ShowBalloonTip(5000, "Setup check failed", ex.Message, ToolTipIcon.Error);
        }
    }

    private void ShowOperationNotification(ProfileActivationResult result)
    {
        string title;
        string message;
        ToolTipIcon icon;

        switch (result.Status)
        {
            case ProfileActivationStatus.Success when result.Mode == AgentMode.Couch:
                title = "Couch Mode ready";
                message = result.SteamResult?.Message ?? result.DisplayResult.Message;
                icon = ToolTipIcon.Info;
                break;
            case ProfileActivationStatus.Success:
                title = "Desktop Mode restored";
                message = result.DisplayResult.Message;
                icon = ToolTipIcon.Info;
                break;
            case ProfileActivationStatus.PartialSuccess:
                title = "Partial success";
                message = result.SteamResult?.Message ?? result.DisplayResult.Message;
                icon = ToolTipIcon.Warning;
                break;
            default:
                title = "Failure";
                message = result.SteamResult?.Message ?? result.DisplayResult.Message;
                icon = ToolTipIcon.Error;
                break;
        }

        notifyIcon.ShowBalloonTip(5000, title, message, icon);
    }

    private async Task RunStartupRecoveryCheckAsync()
    {
        try
        {
            var check = await recoveryCoordinator.CheckAsync();
            if (check.Issue == DisplayRecoveryIssue.None)
            {
                return;
            }

            notifyIcon.ShowBalloonTip(
                5000,
                "Display recovery required",
                check.Message,
                check.Issue == DisplayRecoveryIssue.InterruptedOperation ? ToolTipIcon.Warning : ToolTipIcon.Error);

            using var dialog = new RecoveryDialogForm(check.Message, check.AutomaticRecoveryConfigured);
            while (true)
            {
                var choice = dialog.ShowDialog();
                if (choice == DialogResult.Retry)
                {
                    OpenLogs();
                    continue;
                }

                if (choice == DialogResult.Yes)
                {
                    var recovery = await recoveryCoordinator.RecoverAsync();
                    notifyIcon.ShowBalloonTip(
                        5000,
                        recovery.Succeeded ? "Desktop restored" : "Recovery failed",
                        recovery.Message,
                        recovery.Succeeded ? ToolTipIcon.Info : ToolTipIcon.Error);

                    if (!recovery.Succeeded)
                    {
                        MessageBox.Show(
                            $"{recovery.Message}{Environment.NewLine}{Environment.NewLine}Open the latest logs and restore the desktop layout manually before retrying.",
                            "Recovery Failed",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }

                    return;
                }

                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Startup recovery check failed.");
            notifyIcon.ShowBalloonTip(5000, "Recovery check failed", ex.Message, ToolTipIcon.Error);
        }
    }

    private Task ToggleStartupRegistration()
    {
        try
        {
            startupRegistration.SetEnabled(
                ApplicationName,
                startupCommandLine,
                startWithWindowsItem.Checked);
            startupRegistrationEnabled = startWithWindowsItem.Checked;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update startup registration.");
            notifyIcon.ShowBalloonTip(5000, "Failure", ex.Message, ToolTipIcon.Error);
            startWithWindowsItem.Checked = startupRegistrationEnabled;
        }

        return Task.CompletedTask;
    }

    private void ShowStatusWindow()
    {
        statusForm.Show();
        statusForm.BringToFront();
        _ = statusForm.RefreshNowAsync();
    }

    private async Task ShowSettingsWindowAsync()
    {
        settingsForm.Show();
        settingsForm.BringToFront();
        await settingsForm.LoadCurrentValuesAsync();
    }

    private async Task ShowDiagnosticsWindowAsync()
    {
        diagnosticsForm.Show();
        diagnosticsForm.BringToFront();
        await diagnosticsForm.RefreshNowAsync();
    }

    private async Task StartPairingAsync()
    {
        try
        {
            var session = await pairingService.StartAsync();
            await pairingCodeForm.ShowSessionAsync(session);
            notifyIcon.ShowBalloonTip(5000, "Pairing enabled", $"Code {session.PairingCode} is active for five minutes.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start pairing.");
            notifyIcon.ShowBalloonTip(5000, "Failure", ex.Message, ToolTipIcon.Error);
        }
    }

    private async Task SaveCurrentDesktopSnapshotAsync()
    {
        try
        {
            var existingSnapshot = await snapshotStore.LoadLastDesktopSnapshotAsync();
            if (existingSnapshot is not null)
            {
                var overwrite = MessageBox.Show(
                    $"A desktop snapshot from {existingSnapshot.CapturedAtUtc.LocalDateTime:g} already exists. Replace it with the current desktop layout?",
                    "Replace Desktop Snapshot",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (overwrite != DialogResult.Yes)
                {
                    return;
                }
            }

            var snapshot = await displayManager.CaptureSnapshotAsync();
            await snapshotStore.SaveAsync(snapshot);
            notifyIcon.ShowBalloonTip(
                5000,
                "Desktop snapshot saved",
                $"Saved current desktop layout from {snapshot.CapturedAtUtc.LocalDateTime:g}.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save desktop snapshot.");
            notifyIcon.ShowBalloonTip(5000, "Failure", ex.Message, ToolTipIcon.Error);
        }
    }

    private async Task ClearDesktopSnapshotAsync()
    {
        try
        {
            var existingSnapshot = await snapshotStore.LoadLastDesktopSnapshotAsync();
            if (existingSnapshot is null)
            {
                notifyIcon.ShowBalloonTip(5000, "No snapshot saved", "There is no saved desktop snapshot to clear.", ToolTipIcon.Info);
                return;
            }

            var confirmed = MessageBox.Show(
                $"Delete the saved desktop snapshot from {existingSnapshot.CapturedAtUtc.LocalDateTime:g}?",
                "Clear Desktop Snapshot",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirmed != DialogResult.Yes)
            {
                return;
            }

            await snapshotStore.ClearAsync();
            notifyIcon.ShowBalloonTip(5000, "Desktop snapshot cleared", "The saved desktop restore snapshot was removed.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clear desktop snapshot.");
            notifyIcon.ShowBalloonTip(5000, "Failure", ex.Message, ToolTipIcon.Error);
        }
    }

    private void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        OpenWithShell(path);
    }

    private void OpenLogs()
    {
        string logFilePath = logFileAccessor.CurrentLogFilePath;
        if (File.Exists(logFilePath))
        {
            OpenWithShell(logFilePath);
            return;
        }

        OpenDirectory(paths.LogsDirectory);
    }

    private void OpenWithShell(string path)
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open shell target {Target}.", path);
            notifyIcon.ShowBalloonTip(5000, "Failure", ex.Message, ToolTipIcon.Error);
        }
    }

    private void ExitApplication()
    {
        ExitThread();
    }

    private void QueueUiAction(Action action)
    {
        if (uiInvoker.IsDisposed)
        {
            return;
        }

        _ = uiInvoker.BeginInvoke(action);
    }

    private void QueueUiAction(Func<Task> action)
    {
        if (uiInvoker.IsDisposed)
        {
            return;
        }

        _ = uiInvoker.BeginInvoke(async () => await action());
    }

    private void StartCouchMode()
    {
        if (!operationService.TryStartActivateCouchMode(out _))
        {
            return;
        }

        RefreshMenuState();
    }

    private void StartDesktopMode()
    {
        if (!operationService.TryStartActivateDesktopMode(out _))
        {
            return;
        }

        RefreshMenuState();
    }

    private void OnActivationRequested(object? sender, EventArgs e)
    {
        if (uiInvoker.IsDisposed)
        {
            return;
        }

        _ = uiInvoker.BeginInvoke(() =>
        {
            ShowStatusWindow();
            notifyIcon.ShowBalloonTip(3000, "Couch Control", "The existing agent instance is already running.", ToolTipIcon.Info);
        });
    }

    private void OnOperationCompleted(object? sender, AgentOperationRecord operation)
    {
        if (uiInvoker.IsDisposed)
        {
            return;
        }

        _ = uiInvoker.BeginInvoke(async () =>
        {
            RefreshMenuState();

            if (operation.Result is not null)
            {
                logger.LogInformation(
                    "Profile operation completed with status {Status}: {Message}",
                    operation.Result.Status,
                    operation.Result.SteamResult?.Message ?? operation.Result.DisplayResult.Message);
                ShowOperationNotification(operation.Result);
            }
            else
            {
                notifyIcon.ShowBalloonTip(5000, "Failure", operation.Message ?? "Operation failed.", ToolTipIcon.Error);
            }

            await statusForm.RefreshNowAsync();
        });
    }
}
