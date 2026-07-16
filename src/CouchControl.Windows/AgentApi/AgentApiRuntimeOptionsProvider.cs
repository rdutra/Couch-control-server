using System.Linq;
using CouchControl.Core.Abstractions;

namespace CouchControl.Windows.AgentApi;

public sealed class AgentApiRuntimeOptionsProvider
{
    private readonly IAgentConfigurationStore configurationStore;
    private readonly ILocalNetworkInterfaceProvider networkInterfaceProvider;

    public AgentApiRuntimeOptionsProvider(
        IAgentConfigurationStore configurationStore,
        ILocalNetworkInterfaceProvider networkInterfaceProvider)
    {
        this.configurationStore = configurationStore;
        this.networkInterfaceProvider = networkInterfaceProvider;
    }

    public int Port { get; private set; } = 47981;

    public string ListeningInterfaceId { get; private set; } = AgentApiListeningInterface.Automatic;

    public AgentApiBindingPlan BindingPlan { get; private set; } =
        new(47981, AgentApiListeningInterface.Automatic, string.Empty, string.Empty, Array.Empty<string>(), Array.Empty<string>(), false, "Not loaded.");

    public IReadOnlyList<string> AllowedOrigins { get; private set; } = Array.Empty<string>();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await configurationStore.LoadAsync(cancellationToken);
        Port = configuration.ApiPort;
        ListeningInterfaceId = string.IsNullOrWhiteSpace(configuration.ApiListeningInterfaceId)
            ? AgentApiListeningInterface.Automatic
            : configuration.ApiListeningInterfaceId.Trim();
        BindingPlan = networkInterfaceProvider.CreateBindingPlan(configuration);
        AllowedOrigins = configuration.CorsAllowedOrigins
            .Where(static origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
