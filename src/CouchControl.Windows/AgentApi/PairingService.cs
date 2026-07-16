using System.Security.Cryptography;

namespace CouchControl.Windows.AgentApi;

public interface IPairingService
{
    Task<PairingSession> StartAsync(CancellationToken cancellationToken = default);

    Task<PairingSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default);

    Task<PairingResult> PairAsync(string pairingCode, string deviceName, CancellationToken cancellationToken = default);

    Task DisableAsync(CancellationToken cancellationToken = default);
}

public sealed class PairingService : IPairingService
{
    private const int PairingCodeLength = 6;
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(5);

    private readonly IApiTokenStore tokenStore;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim gate = new(1, 1);

    private PairingSessionState? currentSession;

    public PairingService(IApiTokenStore tokenStore, TimeProvider timeProvider)
    {
        this.tokenStore = tokenStore;
        this.timeProvider = timeProvider;
    }

    public async Task<PairingSession> StartAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var expiresAtUtc = timeProvider.GetUtcNow().Add(PairingLifetime);
            currentSession = new PairingSessionState(GenerateCode(), expiresAtUtc, FailedAttempts: 0);
            return currentSession.ToPublicModel();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PairingSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (currentSession is null)
            {
                return null;
            }

            if (IsExpired(currentSession))
            {
                currentSession = null;
                return null;
            }

            return currentSession.ToPublicModel();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PairingResult> PairAsync(string pairingCode, string deviceName, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (currentSession is null || IsExpired(currentSession))
            {
                currentSession = null;
                return PairingResult.Failure(PairingFailureReason.InvalidCode);
            }

            if (currentSession.FailedAttempts >= MaxFailedAttempts)
            {
                return PairingResult.Failure(PairingFailureReason.RateLimited);
            }

            if (!IsValidPairingAttempt(pairingCode, currentSession.PairingCode))
            {
                currentSession = currentSession with { FailedAttempts = currentSession.FailedAttempts + 1 };
                return currentSession.FailedAttempts >= MaxFailedAttempts
                    ? PairingResult.Failure(PairingFailureReason.RateLimited)
                    : PairingResult.Failure(PairingFailureReason.InvalidCode);
            }

            var issuedToken = await tokenStore.CreateDeviceTokenAsync(deviceName, cancellationToken);
            currentSession = null;
            return PairingResult.Success(issuedToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            currentSession = null;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool IsExpired(PairingSessionState session) =>
        timeProvider.GetUtcNow() >= session.ExpiresAtUtc;

    private static bool IsValidPairingAttempt(string providedCode, string expectedCode) =>
        !string.IsNullOrWhiteSpace(providedCode) &&
        providedCode.Length == PairingCodeLength &&
        providedCode.All(char.IsDigit) &&
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(providedCode),
            System.Text.Encoding.UTF8.GetBytes(expectedCode));

    private static string GenerateCode()
    {
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6");
    }

    private sealed record PairingSessionState(
        string PairingCode,
        DateTimeOffset ExpiresAtUtc,
        int FailedAttempts)
    {
        public PairingSession ToPublicModel() =>
            new(
                PairingCode,
                ExpiresAtUtc,
                FailedAttempts,
                Math.Max(0, MaxFailedAttempts - FailedAttempts));
    }
}

public enum PairingFailureReason
{
    InvalidCode = 0,
    RateLimited = 1
}

public sealed record PairingResult(
    bool Succeeded,
    PairingFailureReason? FailureReason,
    IssuedDeviceToken? Token)
{
    public static PairingResult Success(IssuedDeviceToken token) => new(true, null, token);

    public static PairingResult Failure(PairingFailureReason reason) => new(false, reason, null);
}
