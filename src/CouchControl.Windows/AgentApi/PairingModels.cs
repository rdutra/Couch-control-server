namespace CouchControl.Windows.AgentApi;

public enum ApiTokenKind
{
    Master = 0,
    Device = 1
}

public sealed record AuthenticatedApiToken(
    ApiTokenKind Kind,
    bool IsAdministrative,
    string? DeviceId,
    string? DeviceName);

public sealed record PairedDevice(
    string DeviceId,
    string DeviceName,
    DateTimeOffset PairedAtUtc,
    DateTimeOffset? LastSeenAtUtc);

public sealed record IssuedDeviceToken(
    string DeviceId,
    string DeviceName,
    string Token,
    DateTimeOffset PairedAtUtc);

public sealed record PairingSession(
    string PairingCode,
    DateTimeOffset ExpiresAtUtc,
    int FailedAttempts,
    int RemainingAttempts);

