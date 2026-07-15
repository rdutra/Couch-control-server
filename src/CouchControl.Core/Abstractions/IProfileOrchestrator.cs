using CouchControl.Core.Models;

namespace CouchControl.Core.Abstractions;

public interface IProfileOrchestrator
{
    Task<ProfileActivationResult> ActivateCouchModeAsync(
        CancellationToken cancellationToken = default);

    Task<ProfileActivationResult> ActivateDesktopModeAsync(
        CancellationToken cancellationToken = default);

    AgentOperationStatus GetStatus();
}
