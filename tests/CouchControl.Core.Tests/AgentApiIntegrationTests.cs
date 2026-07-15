using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using CouchControl.Core.Orchestration;
using CouchControl.Windows;
using CouchControl.Windows.AgentApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CouchControl.Core.Tests;

public sealed class AgentApiIntegrationTests
{
    [Fact]
    public async Task Health_DoesNotRequireAuthentication()
    {
        await using var host = await AgentApiTestHost.StartAsync();

        var response = await host.Client.GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(payload);
        Assert.True(payload.Healthy);
    }

    [Fact]
    public async Task ProtectedEndpoints_Return401_ForMissingOrInvalidToken()
    {
        await using var host = await AgentApiTestHost.StartAsync();

        var missingResponse = await host.Client.GetAsync("/api/v1/status");
        Assert.Equal(HttpStatusCode.Unauthorized, missingResponse.StatusCode);

        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "BADTOKEN");
        var invalidResponse = await host.Client.GetAsync("/api/v1/status");
        Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
    }

    [Fact]
    public async Task StatusAndDisplays_ReturnDtos()
    {
        await using var host = await AgentApiTestHost.StartAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);

        var status = await host.Client.GetFromJsonAsync<StatusResponse>("/api/v1/status");
        Assert.NotNull(status);
        Assert.Equal("Living Room Gaming PC", status.AgentName);
        Assert.Equal("desktop", status.Mode);
        Assert.Equal("idle", status.Operation);
        Assert.True(status.TvConnected);
        Assert.True(status.SteamInstalled);
        Assert.False(status.SteamRunning);

        var displays = await host.Client.GetFromJsonAsync<List<DisplayResponse>>("/api/v1/displays");
        Assert.NotNull(displays);
        Assert.Equal(2, displays.Count);
        Assert.Contains(displays, static display => display.FriendlyName == "GS34WQC" && display.Primary);
        Assert.Contains(displays, static display => display.FriendlyName == "SAMSUNG");
    }

    [Fact]
    public async Task ActivateCouchMode_ReturnsAccepted_AndOperationCanBeFetched()
    {
        await using var host = await AgentApiTestHost.StartAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);

        var startResponse = await host.Client.PostAsync("/api/v1/modes/couch", content: null);

        Assert.Equal(HttpStatusCode.Accepted, startResponse.StatusCode);
        var accepted = await startResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        Assert.NotNull(accepted);
        Assert.True(accepted.Accepted);

        var operation = await host.WaitForOperationAsync(accepted.OperationId);
        Assert.Equal("succeeded", operation.State);
        Assert.Equal("couch", operation.Mode);
    }

    [Fact]
    public async Task ActivateDesktopMode_ReturnsAccepted_WhenSnapshotExists()
    {
        await using var host = await AgentApiTestHost.StartAsync(seedDesktopSnapshot: true);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);

        var startResponse = await host.Client.PostAsync("/api/v1/modes/desktop", content: null);

        Assert.Equal(HttpStatusCode.Accepted, startResponse.StatusCode);
        var accepted = await startResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        Assert.NotNull(accepted);

        var operation = await host.WaitForOperationAsync(accepted.OperationId);
        Assert.Equal("succeeded", operation.State);
        Assert.Equal("desktop", operation.Mode);
    }

    [Fact]
    public async Task ActivateMode_Returns409_WhenAnotherOperationIsRunning()
    {
        var activationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentApiTestHost.StartAsync(activationGate: activationGate);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);

        var firstResponse = await host.Client.PostAsync("/api/v1/modes/couch", content: null);
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);

        var secondResponse = await host.Client.PostAsync("/api/v1/modes/desktop", content: null);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        activationGate.SetResult();
    }

    [Fact]
    public async Task GetOperation_Returns404_ForUnknownOperation()
    {
        await using var host = await AgentApiTestHost.StartAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);

        var response = await host.Client.GetAsync($"/api/v1/operations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class AgentApiTestHost : IAsyncDisposable
    {
        private readonly WebApplication app;

        private AgentApiTestHost(WebApplication app, HttpClient client, string token)
        {
            this.app = app;
            Client = client;
            Token = token;
        }

        public HttpClient Client { get; }

        public string Token { get; }

        public static async Task<AgentApiTestHost> StartAsync(
            bool seedDesktopSnapshot = false,
            TaskCompletionSource? activationGate = null)
        {
            string root = Path.Combine(Path.GetTempPath(), "couchcontrol-agent-api-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            var configStore = new InMemoryAgentConfigurationStore();
            await configStore.SaveAsync(new AgentConfiguration
            {
                AgentName = "Living Room Gaming PC",
                CouchDisplayIdentifier = new DisplayIdentifier(TvPath),
                CouchDisplayIdentity = new CouchDisplayIdentity(TvPath, "SAMSUNG", "SAM", "735A", "UID33029", "00000000:000135B1", 33029),
                LaunchSteamAutomatically = false,
                ApiPort = 47981,
                CorsAllowedOrigins = ["http://localhost:3000"]
            });

            var displaySnapshotStore = new InMemoryDisplaySnapshotStore();
            if (seedDesktopSnapshot)
            {
                await displaySnapshotStore.SaveAsync(CreateSnapshot(UltrawidePath, "GS34WQC", "00000000:000135B1", 0, 33024));
            }

            builder.Services.AddSingleton(new CouchControlPaths(root));
            builder.Services.AddSingleton<IAgentConfigurationStore>(configStore);
            builder.Services.AddSingleton<IDisplaySnapshotStore>(displaySnapshotStore);
            builder.Services.AddSingleton<IDisplayManager>(new FakeDisplayManager(activationGate));
            builder.Services.AddSingleton<ISteamLauncher>(new FakeSteamLauncher());
            builder.Services.AddSingleton<IDisplayMatchingService, DisplayMatchingService>();
            builder.Services.AddSingleton<ProfileOrchestrator>();
            builder.Services.AddSingleton<IProfileOrchestrator>(static services => services.GetRequiredService<ProfileOrchestrator>());
            builder.Services.AddCouchControlAgentApi();
            builder.Services.AddSingleton<IProtectedDataService, PassthroughProtectedDataService>();

            var app = builder.Build();
            app.MapCouchControlAgentApi();
            await AgentApiApplicationExtensions.InitializeAgentApiAsync(app.Services);
            await app.StartAsync();

            var token = await app.Services.GetRequiredService<IApiTokenStore>().GetTokenAsync();
            return new AgentApiTestHost(app, app.GetTestClient(), token);
        }

        public async Task<OperationResponse> WaitForOperationAsync(Guid operationId)
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var response = await Client.GetFromJsonAsync<OperationResponse>($"/api/v1/operations/{operationId}");
                if (response is not null && response.State != "running")
                {
                    return response;
                }

                await Task.Delay(20);
            }

            throw new TimeoutException($"Operation {operationId} did not finish in time.");
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private sealed class FakeDisplayManager : IDisplayManager
    {
        private readonly TaskCompletionSource? activationGate;

        public FakeDisplayManager(TaskCompletionSource? activationGate)
        {
            this.activationGate = activationGate;
        }

        public Task<IReadOnlyList<DisplayDevice>> GetDisplaysAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DisplayDevice>>(
            [
                new DisplayDevice(new DisplayIdentifier(UltrawidePath), "GS34WQC", true, true, new DisplayMode(3440, 1440, 100), UltrawidePath, "00000000:000135B1", 0, 33024, "DisplayPort"),
                new DisplayDevice(new DisplayIdentifier(TvPath), "SAMSUNG", false, false, new DisplayMode(3840, 2160, 60), TvPath, "00000000:000135B1", 1, 33029, "HDMI")
            ]);

        public Task<DisplaySnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateSnapshot(UltrawidePath, "GS34WQC", "00000000:000135B1", 0, 33024));

        public async Task<OperationResult> ActivateOnlyAsync(DisplayIdentifier display, DisplayMode? preferredMode, bool dryRun = false, CancellationToken cancellationToken = default)
        {
            if (activationGate is not null)
            {
                await activationGate.Task.WaitAsync(cancellationToken);
            }

            return OperationResult.Success("Activated couch display.", outcome: "Success");
        }

        public Task<OperationResult> RestoreSnapshotAsync(DisplaySnapshot snapshot, RestoreSnapshotOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success("Desktop restored.", outcome: "Success"));
    }

    private sealed class FakeSteamLauncher : ISteamLauncher
    {
        public bool IsInstalled(AgentConfiguration configuration) => true;

        public bool IsRunning() => false;

        public Task<OperationResult> StartBigPictureAsync(AgentConfiguration configuration, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success("Steam started.", outcome: "Success"));
    }

    private sealed class PassthroughProtectedDataService : IProtectedDataService
    {
        public byte[] Protect(byte[] plaintext) => plaintext;

        public byte[] Unprotect(byte[] ciphertext) => ciphertext;
    }

    private static DisplaySnapshot CreateSnapshot(
        string devicePath,
        string friendlyName,
        string adapterLuid,
        uint sourceId,
        uint targetId) =>
        new(
            "snapshot-1",
            DateTimeOffset.UtcNow,
            [
                new DisplayDevice(
                    new DisplayIdentifier(devicePath),
                    friendlyName,
                    true,
                    true,
                    new DisplayMode(3440, 1440, 100),
                    devicePath,
                    adapterLuid,
                    sourceId,
                    targetId,
                    "DisplayPort")
            ],
            [
                new DisplayPathSnapshot(
                    new DisplayIdentifier(devicePath),
                    adapterLuid,
                    sourceId,
                    targetId,
                    true,
                    true,
                    new DisplayPoint(0, 0),
                    3440,
                    1440,
                    "32Bpp",
                    new DisplayRefreshRate(100, 1),
                    "Identity",
                    "Identity",
                    "DisplayPort",
                    new DisplaySourceModeSnapshot(3440, 1440, "32Bpp", new DisplayPoint(0, 0)),
                    new DisplayTargetModeSnapshot(new DisplayRefreshRate(100, 1), 3440, 1440, "Progressive"))
            ]);

    private const string UltrawidePath = @"\\?\DISPLAY#GBT3406#5&371a1502&0&UID33024#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string TvPath = @"\\?\DISPLAY#SAM735A#5&371a1502&0&UID33029#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
}
