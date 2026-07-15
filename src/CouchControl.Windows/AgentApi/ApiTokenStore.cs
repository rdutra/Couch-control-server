using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace CouchControl.Windows.AgentApi;

public interface IApiTokenStore
{
    Task EnsureTokenExistsAsync(CancellationToken cancellationToken = default);

    Task<bool> ValidateAsync(string token, CancellationToken cancellationToken = default);

    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);
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
    private readonly CouchControlPaths paths;
    private readonly IProtectedDataService protectedDataService;
    private readonly ILogger<ApiTokenStore> logger;
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private byte[]? cachedTokenBytes;

    public ApiTokenStore(
        CouchControlPaths paths,
        IProtectedDataService protectedDataService,
        ILogger<ApiTokenStore> logger)
    {
        this.paths = paths;
        this.protectedDataService = protectedDataService;
        this.logger = logger;
    }

    public async Task EnsureTokenExistsAsync(CancellationToken cancellationToken = default)
    {
        _ = await GetTokenBytesAsync(cancellationToken);
    }

    public async Task<bool> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        var expected = await GetTokenBytesAsync(cancellationToken);
        byte[] provided;

        try
        {
            provided = Convert.FromHexString(token);
        }
        catch (FormatException)
        {
            provided = new byte[expected.Length];
        }

        if (provided.Length != expected.Length)
        {
            provided = new byte[expected.Length];
        }

        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        var bytes = await GetTokenBytesAsync(cancellationToken);
        return Convert.ToHexString(bytes);
    }

    private async Task<byte[]> GetTokenBytesAsync(CancellationToken cancellationToken)
    {
        if (cachedTokenBytes is not null)
        {
            return cachedTokenBytes;
        }

        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (cachedTokenBytes is not null)
            {
                return cachedTokenBytes;
            }

            Directory.CreateDirectory(paths.RootDirectory);

            if (File.Exists(paths.ApiTokenFilePath))
            {
                byte[] protectedBytes = await File.ReadAllBytesAsync(paths.ApiTokenFilePath, cancellationToken);
                cachedTokenBytes = protectedDataService.Unprotect(protectedBytes);
                return cachedTokenBytes;
            }

            cachedTokenBytes = RandomNumberGenerator.GetBytes(32);
            byte[] ciphertext = protectedDataService.Protect(cachedTokenBytes);
            await File.WriteAllBytesAsync(paths.ApiTokenFilePath, ciphertext, cancellationToken);
            logger.LogInformation("Generated API token for the current Windows user.");
            return cachedTokenBytes;
        }
        finally
        {
            tokenLock.Release();
        }
    }
}
