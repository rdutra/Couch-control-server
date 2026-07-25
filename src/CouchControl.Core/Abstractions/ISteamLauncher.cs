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

    bool IsHeroicInstalled(AgentConfiguration configuration) => false;

    Task<OperationResult> StartHeroicConsoleAsync(
        AgentConfiguration configuration,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Failure(
            "Heroic Games Launcher is not available.",
            "heroic_not_installed"));
}
