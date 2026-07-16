using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CouchControl.Windows.AgentApi;

public interface IApiTokenStore
{
    Task EnsureTokenExistsAsync(CancellationToken cancellationToken = default);

    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);

    Task<AuthenticatedApiToken?> AuthenticateAsync(string token, CancellationToken cancellationToken = default);

    Task<IssuedDeviceToken> CreateDeviceTokenAsync(string deviceName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PairedDevice>> GetPairedDevicesAsync(CancellationToken cancellationToken = default);

    Task<bool> RevokeDeviceAsync(string deviceId, CancellationToken cancellationToken = default);
}

public interface IProtectedDataService
{
    byte[] Protect(byte[] plaintext);

    byte[] Unprotect(byte[] ciphertext);
}

public sealed class DpapiProtectedDataService : IProtectedDataService
{
    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] ciphertext) =>
        ProtectedData.Unprotect(ciphertext, optionalEntropy: null, DataProtectionScope.CurrentUser);
}

public sealed class ApiTokenStore : IApiTokenStore
{
    private const int MasterTokenLengthBytes = 32;
    private const int DeviceTokenLengthBytes = 32;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly CouchControlPaths paths;
    private readonly IProtectedDataService protectedDataService;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ApiTokenStore> logger;
    private readonly SemaphoreSlim gate = new(1, 1);

    private byte[]? cachedMasterTokenBytes;
    private PersistedPairedDevices? cachedPairedDevices;

    public ApiTokenStore(
        CouchControlPaths paths,
        IProtectedDataService protectedDataService,
        TimeProvider timeProvider,
        ILogger<ApiTokenStore> logger)
    {
        this.paths = paths;
        this.protectedDataService = protectedDataService;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public async Task EnsureTokenExistsAsync(CancellationToken cancellationToken = default)
    {
        _ = await GetMasterTokenBytesAsync(cancellationToken);
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        var bytes = await GetMasterTokenBytesAsync(cancellationToken);
        return Convert.ToHexString(bytes);
    }

    public async Task<AuthenticatedApiToken?> AuthenticateAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var providedBytes = TryParseHexToken(token);
        if (providedBytes is null)
        {
            return null;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var masterTokenBytes = await GetMasterTokenBytesUnsafeAsync(cancellationToken);
            if (CryptographicOperations.FixedTimeEquals(providedBytes, masterTokenBytes))
            {
                return new AuthenticatedApiToken(ApiTokenKind.Master, IsAdministrative: true, DeviceId: null, DeviceName: null);
            }

            var pairedDevices = await GetPairedDevicesUnsafeAsync(cancellationToken);
            foreach (var device in pairedDevices.Devices)
            {
                var deviceTokenBytes = Convert.FromHexString(device.Token);
                if (!CryptographicOperations.FixedTimeEquals(providedBytes, deviceTokenBytes))
                {
                    continue;
                }

                var now = timeProvider.GetUtcNow();
                device.LastSeenAtUtc = now;
                await SavePairedDevicesUnsafeAsync(pairedDevices, cancellationToken);

                return new AuthenticatedApiToken(ApiTokenKind.Device, IsAdministrative: false, device.DeviceId, device.DeviceName);
            }

            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IssuedDeviceToken> CreateDeviceTokenAsync(string deviceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            throw new ArgumentException("Device name must be provided.", nameof(deviceName));
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var devices = await GetPairedDevicesUnsafeAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(DeviceTokenLengthBytes));
            var persisted = new PersistedPairedDevice
            {
                DeviceId = Guid.NewGuid().ToString("N"),
                DeviceName = deviceName.Trim(),
                Token = token,
                PairedAtUtc = now,
                LastSeenAtUtc = null
            };

            devices.Devices.Add(persisted);
            await SavePairedDevicesUnsafeAsync(devices, cancellationToken);
            logger.LogInformation("Paired device {DeviceName} ({DeviceId}).", persisted.DeviceName, persisted.DeviceId);

            return new IssuedDeviceToken(persisted.DeviceId, persisted.DeviceName, token, persisted.PairedAtUtc);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<PairedDevice>> GetPairedDevicesAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var devices = await GetPairedDevicesUnsafeAsync(cancellationToken);
            return devices.Devices
                .Select(static device => new PairedDevice(
                    device.DeviceId,
                    device.DeviceName,
                    device.PairedAtUtc,
                    device.LastSeenAtUtc))
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> RevokeDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var devices = await GetPairedDevicesUnsafeAsync(cancellationToken);
            var removed = devices.Devices.RemoveAll(device => string.Equals(device.DeviceId, deviceId, StringComparison.Ordinal)) > 0;
            if (!removed)
            {
                return false;
            }

            await SavePairedDevicesUnsafeAsync(devices, cancellationToken);
            logger.LogInformation("Revoked paired device {DeviceId}.", deviceId);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<byte[]> GetMasterTokenBytesAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await GetMasterTokenBytesUnsafeAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<byte[]> GetMasterTokenBytesUnsafeAsync(CancellationToken cancellationToken)
    {
        if (cachedMasterTokenBytes is not null)
        {
            return cachedMasterTokenBytes;
        }

        Directory.CreateDirectory(paths.RootDirectory);

        if (File.Exists(paths.ApiTokenFilePath))
        {
            byte[] protectedBytes = await File.ReadAllBytesAsync(paths.ApiTokenFilePath, cancellationToken);
            cachedMasterTokenBytes = protectedDataService.Unprotect(protectedBytes);
            return cachedMasterTokenBytes;
        }

        cachedMasterTokenBytes = RandomNumberGenerator.GetBytes(MasterTokenLengthBytes);
        byte[] ciphertext = protectedDataService.Protect(cachedMasterTokenBytes);
        await File.WriteAllBytesAsync(paths.ApiTokenFilePath, ciphertext, cancellationToken);
        logger.LogInformation("Generated API token for the current Windows user.");
        return cachedMasterTokenBytes;
    }

    private async Task<PersistedPairedDevices> GetPairedDevicesUnsafeAsync(CancellationToken cancellationToken)
    {
        if (cachedPairedDevices is not null)
        {
            return cachedPairedDevices;
        }

        Directory.CreateDirectory(paths.RootDirectory);
        if (!File.Exists(paths.PairedDevicesFilePath))
        {
            cachedPairedDevices = new PersistedPairedDevices();
            return cachedPairedDevices;
        }

        var encryptedJson = await File.ReadAllBytesAsync(paths.PairedDevicesFilePath, cancellationToken);
        var json = protectedDataService.Unprotect(encryptedJson);
        cachedPairedDevices = JsonSerializer.Deserialize<PersistedPairedDevices>(json, JsonOptions) ?? new PersistedPairedDevices();
        return cachedPairedDevices;
    }

    private async Task SavePairedDevicesUnsafeAsync(PersistedPairedDevices devices, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.RootDirectory);
        var json = JsonSerializer.SerializeToUtf8Bytes(devices, JsonOptions);
        var encrypted = protectedDataService.Protect(json);
        await File.WriteAllBytesAsync(paths.PairedDevicesFilePath, encrypted, cancellationToken);
        cachedPairedDevices = devices;
    }

    private static byte[]? TryParseHexToken(string token)
    {
        try
        {
            return Convert.FromHexString(token);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed record PersistedPairedDevices
    {
        public int SchemaVersion { get; init; } = 1;

        public List<PersistedPairedDevice> Devices { get; init; } = [];
    }

    private sealed class PersistedPairedDevice
    {
        public string DeviceId { get; init; } = string.Empty;

        public string DeviceName { get; init; } = string.Empty;

        public string Token { get; init; } = string.Empty;

        public DateTimeOffset PairedAtUtc { get; init; }

        public DateTimeOffset? LastSeenAtUtc { get; set; }
    }
}
