using CouchControl.Core.Models;

namespace CouchControl.Core.Abstractions;

public interface IModeAutomationService
{
    Task<OperationResult> RunPostActivationAsync(
        AgentMode mode,
        AgentConfiguration configuration,
        CancellationToken cancellationToken = default);
}
