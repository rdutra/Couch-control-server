using System.Diagnostics;
using System.Drawing;
using CouchControl.Agent.Logging;
using CouchControl.Agent.Settings;
using CouchControl.Agent.Status;
using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using CouchControl.Windows;
using CouchControl.Windows.AgentApi;
using CouchControl.Windows.Runtime;
using CouchControl.Windows.Startup;
using Microsoft.Extensions.Logging;

namespace CouchControl.Agent.Hosting;

public sealed class AgentApplicationContext : ApplicationContext
{
    private const string ApplicationName = "CouchControl.Agent";

    private readonly NotifyIcon notifyIcon;
    private readonly ToolStripMenuItem activateCouchModeItem;
    private readonly ToolStripMenuItem restoreDesktopModeItem;
    private readonly ToolStripMenuItem startWithWindowsItem;
    private readonly ToolStripMenuItem exitItem;
    private readonly Control uiInvoker;
    private readonly IAgentApiOperationService operationService;
    private readonly IAgentStatusService agentStatusService;
    private readonly IAgentConfigurationStore configurationStore;
    private readonly IStartupRegistration startupRegistration;
    private readonly ISingleInstanceCoordinator singleInstanceCoordinator;
    private readonly IAgentLogFileAccessor logFileAccessor;
    private readonly CouchControlPaths paths;
    private readonly ILogger<AgentApplicationContext> logger;
    private readonly string startupCommandLine;
    private readonly StatusForm statusForm;
    private readonly SettingsForm settingsForm;

    public AgentApplicationContext(
        IAgentApiOperationService operationService,
        IAgentStatusService agentStatusService,
        IAgentConfigurationStore configurationStore,
        IStartupRegistration startupRegistration,
        ISingleInstanceCoordinator singleInstanceCoordinator,
        IAgentLogFileAccessor logFileAccessor,
        IApiTokenStore apiTokenStore,
        CouchControlPaths paths,
        ILogger<AgentApplicationContext> logger)
    {
        this.operationService = operationService;
        this.agentStatusService = agentStatusService;
        this.configurationStore = configurationStore;
        this.startupRegistration = startupRegistration;
        this.singleInstanceCoordinator = singleInstanceCoordinator;
        this.logFileAccessor = logFileAccessor;
        this.paths = paths;
        this.logger = logger;

        startupCommandLine = CouchControl.Windows.Startup.CurrentUserStartupRegistration.BuildCommandLine(
            Environment.ProcessPath ?? throw new InvalidOperationException("Process path is unavailable."));

        uiInvoker = new Control();
        uiInvoker.CreateControl();

        activateCouchModeItem = new ToolStripMenuItem("Activate Couch Mode", null, (_, _) => StartCouchMode());
        restoreDesktopModeItem = new ToolStripMenuItem("Restore Desktop Mode", null, (_, _) => StartDesktopMode());
        startWithWindowsItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartupRegistration())
        {
            CheckOnClick = true
        };
        exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication());

        statusForm = new StatusForm(agentStatusService);
        settingsForm = new SettingsForm(configurationStore, startupRegistration, startupCommandLine, apiTokenStore);

        var contextMenu = new ContextMenuStrip();
        contextMenu.Opening += (_, _) => RefreshMenuState();
        contextMenu.Items.Add(new ToolStripMenuItem("Couch Control") { Enabled = false });
        contextMenu.Items.Add(activateCouchModeItem);
        contextMenu.Items.Add(restoreDesktopModeItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(new ToolStripMenuItem("Status", null, (_, _) => ShowStatusWindow()));
        contextMenu.Items.Add(new ToolStripMenuItem("Settings", null, async (_, _) => await ShowSettingsWindowAsync()));
        contextMenu.Items.Add(new ToolStripMenuItem("Show Configuration Folder", null, (_, _) => OpenDirectory(paths.RootDirectory)));
        contextMenu.Items.Add(new ToolStripMenuItem("View Logs", null, (_, _) => OpenLogs()));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(startWithWindowsItem);
        contextMenu.Items.Add(exitItem);

        notifyIcon = new NotifyIcon
        {
            Text = "Couch Control",
            Icon = SystemIcons.Application,
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
            settingsForm.Dispose();
            uiInvoker.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RefreshMenuState()
    {
        startWithWindowsItem.Checked = startupRegistration.IsEnabled(ApplicationName);
        activateCouchModeItem.Enabled = !operationService.IsOperationRunning;
        restoreDesktopModeItem.Enabled = !operationService.IsOperationRunning;
        startWithWindowsItem.Enabled = !operationService.IsOperationRunning;
        exitItem.Enabled = !operationService.IsOperationRunning;
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

    private void ToggleStartupRegistration()
    {
        try
        {
            startupRegistration.SetEnabled(
                ApplicationName,
                startupCommandLine,
                startWithWindowsItem.Checked);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update startup registration.");
            notifyIcon.ShowBalloonTip(5000, "Failure", ex.Message, ToolTipIcon.Error);
            startWithWindowsItem.Checked = startupRegistration.IsEnabled(ApplicationName);
        }
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
