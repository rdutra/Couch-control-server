using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;

namespace CouchControl.Windows.AgentApi;

public sealed class AgentApiNetworkChangeMonitor : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PeriodicDelay = TimeSpan.FromSeconds(30);
    private readonly AgentApiRuntimeOptionsProvider runtimeOptionsProvider;
    private readonly IAgentApiHealthState healthState;
    private readonly IAgentMdnsAdvertisementService advertisementService;
    private readonly ILogger<AgentApiNetworkChangeMonitor> logger;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly object scheduleLock = new();
    private readonly Timer debounceTimer;
    private long networkChangeGeneration;
    private bool started;

    public AgentApiNetworkChangeMonitor(
        AgentApiRuntimeOptionsProvider runtimeOptionsProvider,
        IAgentApiHealthState healthState,
        IAgentMdnsAdvertisementService advertisementService,
        ILogger<AgentApiNetworkChangeMonitor> logger)
    {
        this.runtimeOptionsProvider = runtimeOptionsProvider;
        this.healthState = healthState;
        this.advertisementService = advertisementService;
        this.logger = logger;
        debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Start()
    {
        if (started)
        {
            return;
        }

        started = true;
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        ScheduleRefresh(DebounceDelay);
    }

    public void Dispose()
    {
        if (started)
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            started = false;
        }

        cancellationTokenSource.Cancel();
        debounceTimer.Dispose();
        cancellationTokenSource.Dispose();
        refreshLock.Dispose();
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs eventArgs)
    {
        lock (scheduleLock)
        {
            networkChangeGeneration++;
            ScheduleRefreshCore(DebounceDelay);
        }
    }

    private void ScheduleRefresh(TimeSpan delay)
    {
        lock (scheduleLock)
        {
            ScheduleRefreshCore(delay);
        }
    }

    private void ScheduleRefreshCore(TimeSpan delay)
    {
        try
        {
            if (!cancellationTokenSource.IsCancellationRequested)
            {
                debounceTimer.Change(delay, Timeout.InfiniteTimeSpan);
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async void OnDebounceElapsed(object? state)
    {
        var retry = false;
        long observedGeneration;
        lock (scheduleLock)
        {
            observedGeneration = networkChangeGeneration;
        }
        try
        {
            await refreshLock.WaitAsync(cancellationTokenSource.Token);
            try
            {
                var changed = runtimeOptionsProvider.RefreshNetworkBindingPlan();
                var bindingPlan = runtimeOptionsProvider.BindingPlan;
                if (changed)
                {
                    healthState.MarkListening(bindingPlan.ListenUrls);
                }
                await advertisementService.UpdateAsync(bindingPlan, cancellationTokenSource.Token);
                if (changed)
                {
                    logger.LogInformation(
                        "Refreshed CouchCTRL network endpoints after a network address change: {StatusMessage}",
                        bindingPlan.StatusMessage);
                }
            }
            finally
            {
                refreshLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            retry = true;
            logger.LogWarning(ex, "Failed to refresh CouchCTRL network endpoints after an address change.");
        }
        finally
        {
            lock (scheduleLock)
            {
                ScheduleRefreshCore(networkChangeGeneration != observedGeneration
                    ? DebounceDelay
                    : retry ? RetryDelay : PeriodicDelay);
            }
        }
    }
}
