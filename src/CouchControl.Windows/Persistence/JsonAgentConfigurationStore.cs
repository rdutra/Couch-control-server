using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;

namespace CouchControl.Windows.Persistence;

public sealed class JsonAgentConfigurationStore : IAgentConfigurationStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public JsonAgentConfigurationStore()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _filePath = Path.Combine(localAppData, "CouchControl", "config.json");
    }

    public JsonAgentConfigurationStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<AgentConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return new AgentConfiguration();
        }

        var persisted = await AtomicJsonFile.ReadAsync<PersistedAgentConfiguration>(
            _filePath,
            JsonOptions,
            cancellationToken);

        if (persisted == null)
        {
            throw new InvalidOperationException(
                $"Failed to load configuration from '{_filePath}': the file is empty.");
        }

        if (persisted.SchemaVersion <= 0)
        {
            throw new InvalidOperationException(
                $"Failed to load configuration from '{_filePath}': schemaVersion must be a positive integer.");
        }

        return persisted.ToDomain();
    }

    public async Task SaveAsync(AgentConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));

        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(Path.Combine(directory, "snapshots"));
            Directory.CreateDirectory(Path.Combine(directory, "logs"));
        }

        var persisted = PersistedAgentConfiguration.FromDomain(configuration);
        await AtomicJsonFile.WriteAsync(_filePath, persisted, JsonOptions, cancellationToken);
    }

    private sealed record PersistedAgentConfiguration
    {
        public int SchemaVersion { get; init; } = CurrentSchemaVersion;

        public string AgentName { get; init; } = "CouchControl Agent";

        public string? CouchDisplayDevicePath { get; init; }

        public PersistedCouchDisplayIdentity? CouchDisplay { get; init; }

        public PersistedDisplayMode PreferredCouchMode { get; init; } = new();

        public bool LaunchSteamAutomatically { get; init; } = true;

        public bool AutomaticallyRecoverInterruptedDisplayOperations { get; init; }

        public string? SteamExecutablePath { get; init; }

        public string? TvPreparationCommand { get; init; }

        public int TvPreparationDelayMs { get; init; } = 4000;

        public int ApiPort { get; init; } = 47981;

        public string? ApiListeningInterfaceId { get; init; }

        public string[] CorsAllowedOrigins { get; init; } = [];

        public AgentConfiguration ToDomain()
        {
            return new AgentConfiguration
            {
                SchemaVersion = SchemaVersion,
                AgentName = string.IsNullOrWhiteSpace(AgentName) ? "CouchControl Agent" : AgentName,
                CouchDisplayIdentifier = ToDisplayIdentifier(),
                CouchDisplayIdentity = CouchDisplay?.ToDomain(),
                PreferredCouchWidth = PreferredCouchMode.Width,
                PreferredCouchHeight = PreferredCouchMode.Height,
                PreferredCouchRefreshRateHz = PreferredCouchMode.RefreshRateHz,
                LaunchSteamAutomatically = LaunchSteamAutomatically,
                AutomaticallyRecoverInterruptedDisplayOperations = AutomaticallyRecoverInterruptedDisplayOperations,
                SteamExecutablePath = SteamExecutablePath,
                TvPreparationCommand = string.IsNullOrWhiteSpace(TvPreparationCommand)
                    ? null
                    : TvPreparationCommand.Trim(),
                TvPreparationDelayMs = TvPreparationDelayMs is >= 0 and <= 60000 ? TvPreparationDelayMs : 4000,
                ApiPort = ApiPort is >= 1 and <= 65535 ? ApiPort : 47981,
                ApiListeningInterfaceId = string.IsNullOrWhiteSpace(ApiListeningInterfaceId)
                    ? null
                    : ApiListeningInterfaceId.Trim(),
                CorsAllowedOrigins = CorsAllowedOrigins
                    .Where(static origin => !string.IsNullOrWhiteSpace(origin))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        public static PersistedAgentConfiguration FromDomain(AgentConfiguration configuration)
        {
            return new PersistedAgentConfiguration
            {
                SchemaVersion = configuration.SchemaVersion <= 0 ? CurrentSchemaVersion : configuration.SchemaVersion,
                AgentName = configuration.AgentName,
                CouchDisplayDevicePath = configuration.CouchDisplayIdentity?.DevicePath ?? configuration.CouchDisplayIdentifier?.Value,
                CouchDisplay = PersistedCouchDisplayIdentity.FromDomain(configuration.CouchDisplayIdentity),
                PreferredCouchMode = PersistedDisplayMode.FromDomain(configuration.PreferredCouchMode),
                LaunchSteamAutomatically = configuration.LaunchSteamAutomatically,
                AutomaticallyRecoverInterruptedDisplayOperations = configuration.AutomaticallyRecoverInterruptedDisplayOperations,
                SteamExecutablePath = configuration.SteamExecutablePath,
                TvPreparationCommand = string.IsNullOrWhiteSpace(configuration.TvPreparationCommand)
                    ? null
                    : configuration.TvPreparationCommand.Trim(),
                TvPreparationDelayMs = configuration.TvPreparationDelayMs,
                ApiPort = configuration.ApiPort,
                ApiListeningInterfaceId = string.IsNullOrWhiteSpace(configuration.ApiListeningInterfaceId)
                    ? null
                    : configuration.ApiListeningInterfaceId.Trim(),
                CorsAllowedOrigins = configuration.CorsAllowedOrigins
                    .Where(static origin => !string.IsNullOrWhiteSpace(origin))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        private DisplayIdentifier? ToDisplayIdentifier()
        {
            if (CouchDisplay?.ToDisplayIdentifier() is { } configuredIdentity)
            {
                return configuredIdentity;
            }

            return string.IsNullOrWhiteSpace(CouchDisplayDevicePath)
                ? null
                : new DisplayIdentifier(CouchDisplayDevicePath);
        }
    }

    private sealed record PersistedCouchDisplayIdentity
    {
        public string StableId { get; init; } = "unknown";

        public string DevicePath { get; init; } = string.Empty;

        public string FriendlyName { get; init; } = string.Empty;

        public string Manufacturer { get; init; } = string.Empty;

        public string ProductCode { get; init; } = string.Empty;

        public string SerialOrInstance { get; init; } = string.Empty;

        public string AdapterLuid { get; init; } = string.Empty;

        public uint TargetId { get; init; }

        public DisplayIdentifier? ToDisplayIdentifier() =>
            string.IsNullOrWhiteSpace(DevicePath) ? null : new DisplayIdentifier(DevicePath);

        public CouchDisplayIdentity ToDomain()
        {
            return new CouchDisplayIdentity(
                DevicePath,
                FriendlyName,
                Manufacturer,
                ProductCode,
                SerialOrInstance,
                AdapterLuid,
                TargetId)
            {
                StableId = string.IsNullOrWhiteSpace(StableId)
                    ? DisplayStableId.FromDevicePath(DevicePath)
                    : StableId
            };
        }

        public static PersistedCouchDisplayIdentity? FromDomain(CouchDisplayIdentity? identity)
        {
            if (identity == null)
            {
                return null;
            }

            return new PersistedCouchDisplayIdentity
            {
                StableId = identity.StableId,
                DevicePath = identity.DevicePath,
                FriendlyName = identity.FriendlyName,
                Manufacturer = identity.Manufacturer,
                ProductCode = identity.ProductCode,
                SerialOrInstance = identity.SerialOrInstance,
                AdapterLuid = identity.AdapterLuid,
                TargetId = identity.TargetId
            };
        }
    }

    private sealed record PersistedDisplayMode
    {
        public int Width { get; init; } = 3840;

        public int Height { get; init; } = 2160;

        public decimal RefreshRateHz { get; init; } = 60;

        public static PersistedDisplayMode FromDomain(DisplayMode mode) =>
            new()
            {
                Width = mode.Width,
                Height = mode.Height,
                RefreshRateHz = mode.RefreshRateHz
            };
    }
}
