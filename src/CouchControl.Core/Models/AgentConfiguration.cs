using System.Linq;

namespace CouchControl.Core.Models;

public sealed record AgentConfiguration
{
    public int SchemaVersion { get; init; } = 1;

    public string AgentName { get; init; } = "CouchControl Agent";

    public DisplayIdentifier? CouchDisplayIdentifier { get; init; }

    public CouchDisplayIdentity? CouchDisplayIdentity { get; init; }

    public int PreferredCouchWidth { get; init; } = 3840;

    public int PreferredCouchHeight { get; init; } = 2160;

    public decimal PreferredCouchRefreshRateHz { get; init; } = 60;

    public bool LaunchSteamAutomatically { get; init; } = true;

    public string? SteamExecutablePath { get; init; }

    public int ApiPort { get; init; } = 47981;

    public IReadOnlyList<string> CorsAllowedOrigins { get; init; } = Array.Empty<string>();

    public DisplayMode PreferredCouchMode =>
        new(PreferredCouchWidth, PreferredCouchHeight, PreferredCouchRefreshRateHz);

    public OperationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(AgentName))
        {
            return OperationResult.Failure("Agent name must be provided.", "agent_name_missing");
        }

        if (!PreferredCouchMode.IsValid)
        {
            return OperationResult.Failure("Preferred couch display mode must be valid.", "invalid_couch_mode");
        }

        if (SteamExecutablePath is { Length: > 0 } path && string.IsNullOrWhiteSpace(path))
        {
            return OperationResult.Failure("Steam executable path cannot be whitespace.", "invalid_steam_path");
        }

        if (ApiPort is < 1 or > 65535)
        {
            return OperationResult.Failure("API port must be between 1 and 65535.", "invalid_api_port");
        }

        if (CorsAllowedOrigins.Any(static origin => string.IsNullOrWhiteSpace(origin)))
        {
            return OperationResult.Failure("CORS origins cannot contain blank values.", "invalid_cors_origins");
        }

        return OperationResult.Success("Configuration is valid.");
    }
}
