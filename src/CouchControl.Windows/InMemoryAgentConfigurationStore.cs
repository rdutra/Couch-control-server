using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;

namespace CouchControl.Windows;

public sealed class InMemoryAgentConfigurationStore : IAgentConfigurationStore
{
    private AgentConfiguration configuration = new();

    public Task<AgentConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(configuration);

    public Task SaveAsync(AgentConfiguration configuration, CancellationToken cancellationToken = default)
    {
        this.configuration = configuration;
        return Task.CompletedTask;
    }
}
