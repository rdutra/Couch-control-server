using CouchControl.Core.Abstractions;
using CouchControl.Core.Orchestration;
using CouchControl.Windows.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CouchControl.Windows;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCouchControlWindows(this IServiceCollection services)
    {
        services.AddSingleton<IDisplayManager, WindowsDisplayManager>();
        services.AddSingleton<ISteamLauncher, WindowsSteamLauncher>();
        services.AddSingleton<IDisplaySnapshotStore, JsonDisplaySnapshotStore>();
        services.AddSingleton<IAgentConfigurationStore, JsonAgentConfigurationStore>();
        services.AddSingleton<IDisplayMatchingService, DisplayMatchingService>();
        services.AddSingleton<ProfileOrchestrator>();
        services.AddSingleton<IProfileOrchestrator>(static serviceProvider =>
            serviceProvider.GetRequiredService<ProfileOrchestrator>());

        return services;
    }
}
