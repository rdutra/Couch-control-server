using System.Linq;
using CouchControl.Core.Abstractions;

namespace CouchControl.Windows.AgentApi;

public sealed class AgentApiRuntimeOptionsProvider
{
    private readonly IAgentConfigurationStore configurationStore;

    public AgentApiRuntimeOptionsProvider(IAgentConfigurationStore configurationStore)
    {
        this.configurationStore = configurationStore;
    }

    public int Port { get; private set; } = 47981;

    public IReadOnlyList<string> AllowedOrigins { get; private set; } = Array.Empty<string>();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await configurationStore.LoadAsync(cancellationToken);
        Port = configuration.ApiPort;
        AllowedOrigins = configuration.CorsAllowedOrigins
            .Where(static origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
