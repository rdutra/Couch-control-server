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

    public string? TvPreparationCommand { get; init; }

    public int TvPreparationDelayMs { get; init; } = 4000;

    public string? CouchAudioCommand { get; init; }

    public string? DesktopAudioCommand { get; init; }

    public string? CouchAudioDeviceId { get; init; }

    public string? CouchAudioDeviceName { get; init; }

    public string? DesktopAudioDeviceId { get; init; }

    public string? DesktopAudioDeviceName { get; init; }

    public int ApiPort { get; init; } = 47981;

    public string? ApiListeningInterfaceId { get; init; }

    public IReadOnlyList<string> CorsAllowedOrigins { get; init; } = Array.Empty<string>();

    public bool AutomaticallyRecoverInterruptedDisplayOperations { get; init; }

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

        if (TvPreparationCommand is { Length: > 0 } tvPreparationCommand &&
            string.IsNullOrWhiteSpace(tvPreparationCommand))
        {
            return OperationResult.Failure("TV preparation command cannot be whitespace.", "invalid_tv_preparation_command");
        }

        if (TvPreparationDelayMs is < 0 or > 60000)
        {
            return OperationResult.Failure("TV preparation delay must be between 0 and 60000 milliseconds.", "invalid_tv_preparation_delay");
        }

        if (CouchAudioCommand is { Length: > 0 } couchAudioCommand &&
            string.IsNullOrWhiteSpace(couchAudioCommand))
        {
            return OperationResult.Failure("Couch audio command cannot be whitespace.", "invalid_couch_audio_command");
        }

        if (DesktopAudioCommand is { Length: > 0 } desktopAudioCommand &&
            string.IsNullOrWhiteSpace(desktopAudioCommand))
        {
            return OperationResult.Failure("Desktop audio command cannot be whitespace.", "invalid_desktop_audio_command");
        }

        if (CouchAudioDeviceId is { Length: > 0 } couchAudioDeviceId &&
            string.IsNullOrWhiteSpace(couchAudioDeviceId))
        {
            return OperationResult.Failure("Couch audio device ID cannot be whitespace.", "invalid_couch_audio_device_id");
        }

        if (DesktopAudioDeviceId is { Length: > 0 } desktopAudioDeviceId &&
            string.IsNullOrWhiteSpace(desktopAudioDeviceId))
        {
            return OperationResult.Failure("Desktop audio device ID cannot be whitespace.", "invalid_desktop_audio_device_id");
        }

        if (ApiPort is < 1 or > 65535)
        {
            return OperationResult.Failure("API port must be between 1 and 65535.", "invalid_api_port");
        }

        if (ApiListeningInterfaceId is { Length: > 0 } interfaceId && string.IsNullOrWhiteSpace(interfaceId))
        {
            return OperationResult.Failure("API listening interface cannot be whitespace.", "invalid_api_interface");
        }

        if (CorsAllowedOrigins.Any(static origin => string.IsNullOrWhiteSpace(origin)))
        {
            return OperationResult.Failure("CORS origins cannot contain blank values.", "invalid_cors_origins");
        }

        return OperationResult.Success("Configuration is valid.");
    }
}
