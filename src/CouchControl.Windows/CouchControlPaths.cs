using System;
using System.IO;

namespace CouchControl.Windows;

public sealed class CouchControlPaths
{
    public CouchControlPaths(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A root directory must be provided.", nameof(rootDirectory));
        }

        RootDirectory = rootDirectory;
        ConfigurationFilePath = Path.Combine(rootDirectory, "config.json");
        SnapshotsDirectory = Path.Combine(rootDirectory, "snapshots");
        LogsDirectory = Path.Combine(rootDirectory, "logs");
    }

    public string RootDirectory { get; }

    public string ConfigurationFilePath { get; }

    public string SnapshotsDirectory { get; }

    public string LogsDirectory { get; }

    public string GetLogFilePath(DateTimeOffset timestamp) =>
        Path.Combine(LogsDirectory, $"agent-{timestamp:yyyyMMdd}.log");

    public static CouchControlPaths CreateDefault()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new CouchControlPaths(Path.Combine(localAppData, "CouchControl"));
    }
}
