using CouchControl.Core.Models;
using CouchControl.Windows;
using Microsoft.Win32;
using System.Diagnostics;

namespace CouchControl.Core.Tests;

public sealed class WindowsSteamLauncherTests
{
    [Fact]
    public void IsInstalled_PrefersExplicitConfiguredPath()
    {
        const string configuredPath = @"C:\Custom\Steam\steam.exe";
        const string registryPath = @"C:\Registry\Steam\steam.exe";
        const string envPath = @"C:\Program Files (x86)\Steam\steam.exe";

        var launcher = CreateLauncher(
            fileSystem: new FakeFileSystem(configuredPath, registryPath, envPath),
            registry: new FakeRegistryAdapter(new Dictionary<(RegistryHive, RegistryView, string, string), string?>
            {
                [(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamExe")] = registryPath
            }),
            environment: new FakeEnvironmentAdapter(new Dictionary<string, string?>
            {
                ["ProgramFiles(x86)"] = @"C:\Program Files (x86)"
            }));

        var result = launcher.TryResolveSteamPath(
            new AgentConfiguration { SteamExecutablePath = configuredPath },
            out var resolvedPath);

        Assert.True(result);
        Assert.Equal(configuredPath, resolvedPath);
    }

    [Fact]
    public void IsInstalled_ReturnsFalseWhenSteamCannotBeFound()
    {
        var launcher = CreateLauncher();

        var result = launcher.IsInstalled(new AgentConfiguration());

        Assert.False(result);
    }

    [Fact]
    public async Task StartBigPictureAsync_UsesUriWhenSteamIsAlreadyRunning()
    {
        const string configuredPath = @"C:\Custom\Steam\steam.exe";
        var processAdapter = new FakeProcessAdapter
        {
            IsRunningResult = true
        };
        var launcher = CreateLauncher(
            fileSystem: new FakeFileSystem(configuredPath),
            processAdapter: processAdapter);

        var result = await launcher.StartBigPictureAsync(
            new AgentConfiguration { SteamExecutablePath = configuredPath });

        Assert.True(result.Succeeded);
        Assert.NotNull(processAdapter.StartCall);
        Assert.Equal("steam://open/bigpicture", processAdapter.StartCall!.FileName);
        Assert.True(processAdapter.StartCall.UseShellExecute);
    }

    [Fact]
    public async Task StartBigPictureAsync_ReturnsFailureWhenProcessLaunchThrows()
    {
        const string configuredPath = @"C:\Custom\Steam\steam.exe";
        var launcher = CreateLauncher(
            fileSystem: new FakeFileSystem(configuredPath),
            processAdapter: new FakeProcessAdapter
            {
                StartException = new InvalidOperationException("boom")
            });

        var result = await launcher.StartBigPictureAsync(
            new AgentConfiguration { SteamExecutablePath = configuredPath });

        Assert.False(result.Succeeded);
        Assert.Equal("steam_launch_failed", result.ErrorCode);
    }

    private static WindowsSteamLauncher CreateLauncher(
        IFileSystemAdapter? fileSystem = null,
        IProcessAdapter? processAdapter = null,
        IRegistryAdapter? registry = null,
        IEnvironmentAdapter? environment = null)
    {
        return new WindowsSteamLauncher(
            fileSystem ?? new FakeFileSystem(),
            processAdapter ?? new FakeProcessAdapter(),
            registry ?? new FakeRegistryAdapter(),
            environment ?? new FakeEnvironmentAdapter());
    }

    private sealed class FakeFileSystem(params string[] existingPaths) : IFileSystemAdapter
    {
        private readonly HashSet<string> existingPaths = new(existingPaths, StringComparer.OrdinalIgnoreCase);

        public bool FileExists(string path) => existingPaths.Contains(path);
    }

    private sealed class FakeProcessAdapter : IProcessAdapter
    {
        public bool IsRunningResult { get; init; }

        public Exception? StartException { get; init; }

        public ProcessStartInfo? StartCall { get; private set; }

        public bool IsProcessRunning(string processName) => IsRunningResult;

        public void Start(ProcessStartInfo startInfo)
        {
            StartCall = startInfo;

            if (StartException is not null)
            {
                throw StartException;
            }
        }
    }

    private sealed class FakeRegistryAdapter(
        IReadOnlyDictionary<(RegistryHive Hive, RegistryView View, string SubKey, string ValueName), string?>? values = null) : IRegistryAdapter
    {
        private readonly IReadOnlyDictionary<(RegistryHive Hive, RegistryView View, string SubKey, string ValueName), string?> values =
            values ?? new Dictionary<(RegistryHive Hive, RegistryView View, string SubKey, string ValueName), string?>();

        public string? GetValue(RegistryHive hive, RegistryView view, string subKey, string valueName) =>
            values.TryGetValue((hive, view, subKey, valueName), out var value)
                ? value
                : null;
    }

    private sealed class FakeEnvironmentAdapter(IReadOnlyDictionary<string, string?>? values = null) : IEnvironmentAdapter
    {
        private readonly IReadOnlyDictionary<string, string?> values =
            values ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        public string? GetEnvironmentVariable(string variableName) =>
            values.TryGetValue(variableName, out var value)
                ? value
                : null;
    }
}
