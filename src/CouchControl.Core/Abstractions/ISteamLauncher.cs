using CouchControl.Core.Models;

namespace CouchControl.Core.Abstractions;

public interface ISteamLauncher
{
    bool IsInstalled(AgentConfiguration configuration);
    bool IsRunning();

    Task<OperationResult> StartBigPictureAsync(
        AgentConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<OperationResult> ExitBigPictureAsync(
        CancellationToken cancellationToken = default);
}
