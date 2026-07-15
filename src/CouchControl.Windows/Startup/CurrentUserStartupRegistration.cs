using Microsoft.Win32;

namespace CouchControl.Windows.Startup;

public sealed class CurrentUserStartupRegistration : IStartupRegistration
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly IRegistryValueStore registryValueStore;

    public CurrentUserStartupRegistration()
        : this(new RegistryValueStore())
    {
    }

    internal CurrentUserStartupRegistration(IRegistryValueStore registryValueStore)
    {
        this.registryValueStore = registryValueStore;
    }

    public bool IsEnabled(string applicationName) =>
        !string.IsNullOrWhiteSpace(
            registryValueStore.GetStringValue(RegistryHive.CurrentUser, RunKeyPath, applicationName));

    public void SetEnabled(string applicationName, string commandLine, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(applicationName))
        {
            throw new ArgumentException("An application name must be provided.", nameof(applicationName));
        }

        if (enabled)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                throw new ArgumentException("A startup command line must be provided when enabling startup registration.", nameof(commandLine));
            }

            registryValueStore.SetStringValue(
                RegistryHive.CurrentUser,
                RunKeyPath,
                applicationName,
                commandLine);

            return;
        }

        registryValueStore.DeleteValue(RegistryHive.CurrentUser, RunKeyPath, applicationName);
    }

    public static string BuildCommandLine(string executablePath, params string[] arguments)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("An executable path must be provided.", nameof(executablePath));
        }

        var segments = new List<string>
        {
            Quote(executablePath)
        };

        foreach (var argument in arguments)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            segments.Add(Quote(argument));
        }

        return string.Join(" ", segments);
    }

    private static string Quote(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}

internal interface IRegistryValueStore
{
    string? GetStringValue(RegistryHive hive, string subKey, string valueName);

    void SetStringValue(RegistryHive hive, string subKey, string valueName, string value);

    void DeleteValue(RegistryHive hive, string subKey, string valueName);
}

internal sealed class RegistryValueStore : IRegistryValueStore
{
    public string? GetStringValue(RegistryHive hive, string subKey, string valueName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var key = baseKey.OpenSubKey(subKey, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void SetStringValue(RegistryHive hive, string subKey, string valueName, string value)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var key = baseKey.CreateSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException($"Failed to open registry key '{subKey}' for writing.");

        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void DeleteValue(RegistryHive hive, string subKey, string valueName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var key = baseKey.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
