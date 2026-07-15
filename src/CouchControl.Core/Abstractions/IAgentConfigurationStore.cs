using CouchControl.Core.Models;

namespace CouchControl.Core.Abstractions;

public interface IAgentConfigurationStore
{
    Task<AgentConfiguration> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        AgentConfiguration configuration,
        CancellationToken cancellationToken = default);
}
