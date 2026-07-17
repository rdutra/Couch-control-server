using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using Microsoft.Win32;
using System.Diagnostics;

namespace CouchControl.Windows;

public sealed class WindowsSteamLauncher : ISteamLauncher
{
    private static readonly RegistryLocation[] RegistryLocations =
    [
        new(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamExe", false),
        new(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath", true),
        new(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Valve\Steam", "InstallPath", true),
        new(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath", true)
    ];

    private readonly IFileSystemAdapter fileSystem;
    private readonly IProcessAdapter processAdapter;
    private readonly IRegistryAdapter registryAdapter;
    private readonly IEnvironmentAdapter environmentAdapter;

    public WindowsSteamLauncher()
        : this(
            new FileSystemAdapter(),
            new ProcessAdapter(),
            new RegistryAdapter(),
            new EnvironmentAdapter())
    {
    }

    internal WindowsSteamLauncher(
        IFileSystemAdapter fileSystem,
        IProcessAdapter processAdapter,
        IRegistryAdapter registryAdapter,
        IEnvironmentAdapter environmentAdapter)
    {
        this.fileSystem = fileSystem;
        this.processAdapter = processAdapter;
        this.registryAdapter = registryAdapter;
        this.environmentAdapter = environmentAdapter;
    }

    public bool IsInstalled(AgentConfiguration configuration)
    {
        return TryResolveSteamPath(configuration, out _);
    }

    public bool IsRunning()
    {
        return processAdapter.IsProcessRunning("steam");
    }

    public Task<OperationResult> StartBigPictureAsync(
        AgentConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveSteamPath(configuration, out var steamPath))
        {
            return Task.FromResult(OperationResult.Failure(
                "Steam installation was not found.",
                "steam_not_installed",
                outcome: "Failure",
                details:
                [
                    "Steam installation not found"
                ]));
        }

        var details = new List<string>
        {
            "Steam installation found"
        };

        var isRunning = processAdapter.IsProcessRunning("steam");
        var startInfo = isRunning
            ? new ProcessStartInfo
            {
                FileName = "steam://open/bigpicture",
                UseShellExecute = true
            }
            : new ProcessStartInfo
            {
                FileName = steamPath,
                Arguments = "-bigpicture",
                UseShellExecute = true
            };

        details.Add("Opening Big Picture mode");

        try
        {
            processAdapter.Start(startInfo);
            return Task.FromResult(OperationResult.Success(
                "Steam Big Picture mode launch requested.",
                outcome: "Success",
                details: details));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            details.Add($"Steam launch failed: {ex.Message}");
            return Task.FromResult(OperationResult.Failure(
                $"Failed to launch Steam Big Picture mode: {ex.Message}",
                "steam_launch_failed",
                outcome: "Failure",
                details: details));
        }
    }

    public async Task<OperationResult> ExitBigPictureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!processAdapter.IsProcessRunning("steam"))
        {
            return OperationResult.Success(
                "Steam is not running.",
                outcome: "Success",
                details:
                [
                    "Steam is not running"
                ]);
        }

        var windowHandle = processAdapter.GetMainWindowHandle("steam");
        if (windowHandle == IntPtr.Zero)
        {
            return OperationResult.PartialSuccess(
                "Steam is running but no main window was found to exit Big Picture mode.",
                outcome: "Partial success",
                details:
                [
                    "Steam is running",
                    "Steam main window not found"
                ]);
        }

        processAdapter.SetForegroundWindow(windowHandle);
        await Task.Delay(250, cancellationToken);
        processAdapter.SendAltEnter();

        return OperationResult.Success(
            "Steam fullscreen mode toggle requested.",
            outcome: "Success",
            details:
            [
                "Steam is running",
                "Requested Big Picture exit via Alt+Enter"
            ]);
    }

    internal bool TryResolveSteamPath(AgentConfiguration configuration, out string steamPath)
    {
        foreach (var candidate in EnumerateCandidates(configuration))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (fileSystem.FileExists(candidate))
            {
                steamPath = candidate;
                return true;
            }
        }

        steamPath = string.Empty;
        return false;
    }

    private IEnumerable<string?> EnumerateCandidates(AgentConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.SteamExecutablePath))
        {
            yield return configuration.SteamExecutablePath;
        }

        foreach (var registryLocation in RegistryLocations)
        {
            var value = registryAdapter.GetValue(
                registryLocation.Hive,
                registryLocation.View,
                registryLocation.SubKey,
                registryLocation.ValueName);

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            yield return registryLocation.AppendSteamExe
                ? Path.Combine(value, "steam.exe")
                : value;
        }

        yield return CombineFromEnvironment("ProgramFiles(x86)");
        yield return CombineFromEnvironment("ProgramFiles");
    }

    private string? CombineFromEnvironment(string variableName)
    {
        var basePath = environmentAdapter.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(basePath)
            ? null
            : Path.Combine(basePath, "Steam", "steam.exe");
    }

    private sealed record RegistryLocation(
        RegistryHive Hive,
        RegistryView View,
        string SubKey,
        string ValueName,
        bool AppendSteamExe);
}

internal interface IFileSystemAdapter
{
    bool FileExists(string path);
}

internal sealed class FileSystemAdapter : IFileSystemAdapter
{
    public bool FileExists(string path) => File.Exists(path);
}

internal interface IProcessAdapter
{
    bool IsProcessRunning(string processName);

    IntPtr GetMainWindowHandle(string processName);

    bool SetForegroundWindow(IntPtr windowHandle);

    void SendAltEnter();

    void Start(ProcessStartInfo startInfo);
}

internal sealed class ProcessAdapter : IProcessAdapter
{
    public bool IsProcessRunning(string processName) =>
        Process.GetProcessesByName(processName).Length > 0;

    public IntPtr GetMainWindowHandle(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process.MainWindowHandle;
            }
        }

        return IntPtr.Zero;
    }

    public bool SetForegroundWindow(IntPtr windowHandle) =>
        NativeMethods.SetForegroundWindow(windowHandle);

    public void SendAltEnter()
    {
        NativeMethods.keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(VkReturn, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(VkReturn, 0, KeyEventKeyUp, UIntPtr.Zero);
        NativeMethods.keybd_event(VkMenu, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    public void Start(ProcessStartInfo startInfo)
    {
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Process launch returned no process handle.");
    }

    private const byte VkMenu = 0x12;
    private const byte VkReturn = 0x0D;
    private const uint KeyEventKeyUp = 0x0002;

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    }
}

internal interface IRegistryAdapter
{
    string? GetValue(RegistryHive hive, RegistryView view, string subKey, string valueName);
}

internal sealed class RegistryAdapter : IRegistryAdapter
{
    public string? GetValue(RegistryHive hive, RegistryView view, string subKey, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }
}

internal interface IEnvironmentAdapter
{
    string? GetEnvironmentVariable(string variableName);
}

internal sealed class EnvironmentAdapter : IEnvironmentAdapter
{
    public string? GetEnvironmentVariable(string variableName) =>
        Environment.GetEnvironmentVariable(variableName);
}
