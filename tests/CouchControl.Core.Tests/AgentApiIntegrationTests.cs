using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using CouchControl.Core.Orchestration;
using CouchControl.Windows;
using CouchControl.Windows.AgentApi;
using CouchControl.Windows.Persistence;
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
        Assert.False(string.IsNullOrWhiteSpace(payload.Version));
    }

    [Fact]
    public async Task Health_DisablesCaching()
    {
        await using var host = await AgentApiTestHost.StartAsync();

        var response = await host.Client.GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("no-cache", response.Headers.Pragma.ToString());
        Assert.True(response.Content.Headers.Expires <= DateTimeOffset.UtcNow);
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
    public async Task Pair_ReturnsPermanentDeviceToken_AndClosesSessionAfterOneUse()
    {
        await using var host = await AgentApiTestHost.StartAsync();
        var session = await host.StartPairingSessionAsync();

        var pairResponse = await host.Client.PostAsJsonAsync("/api/v1/pair", new PairRequest(session.PairingCode, "Rodrigo's iPhone"));
        Assert.Equal(HttpStatusCode.OK, pairResponse.StatusCode);

        var payload = await pairResponse.Content.ReadFromJsonAsync<PairResponse>();
        Assert.NotNull(payload);
        Assert.Equal("Living Room Gaming PC", payload.AgentName);
        Assert.False(string.IsNullOrWhiteSpace(payload.ComputerName));
        Assert.Equal("v1", payload.ApiVersion);
        Assert.Equal("http://192.168.1.40:47981", payload.AgentBaseUrl);
        Assert.Equal(["192.168.1.40"], payload.LanIpv4Addresses);
        Assert.Equal(["255.255.255.0"], payload.LanIpv4SubnetMasks);
        Assert.Equal("192.168.1.255", payload.BroadcastAddress);
        Assert.Equal("192.168.1.255", payload.PreferredWakeOnLanBroadcastAddress);
        Assert.Equal(["192.168.1.255", "255.255.255.255"], payload.WakeOnLanBroadcastAddresses);
        Assert.Equal("00:11:22:33:44:55", payload.MacAddress);
        Assert.False(string.IsNullOrWhiteSpace(payload.Token));
        Assert.NotEqual(host.Token, payload.Token);

        var secondResponse = await host.Client.PostAsJsonAsync("/api/v1/pair", new PairRequest(session.PairingCode, "Another Device"));
        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Pair_RejectsExpiredCode()
    {
        await using var host = await AgentApiTestHost.StartAsync();
        var session = await host.StartPairingSessionAsync();
        host.AdvanceTime(TimeSpan.FromMinutes(6));

        var response = await host.Client.PostAsJsonAsync("/api/v1/pair", new PairRequest(session.PairingCode, "Rodrigo's iPhone"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pair_IsRateLimitedAfterRepeatedFailures()
    {
        await using var host = await AgentApiTestHost.StartAsync();
        _ = await host.StartPairingSessionAsync();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var response = await host.Client.PostAsJsonAsync("/api/v1/pair", new PairRequest("000000", "Rodrigo's iPhone"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var rateLimited = await host.Client.PostAsJsonAsync("/api/v1/pair", new PairRequest("111111", "Rodrigo's iPhone"));
        Assert.Equal((HttpStatusCode)429, rateLimited.StatusCode);
    }

    [Fact]
    public async Task Pair_InvalidCodesReturnGenericError()
    {
        await using var host = await AgentApiTestHost.StartAsync();
        _ = await host.StartPairingSessionAsync();

        var malformed = await host.Client.PostAsJsonAsync("/api/v1/pair", new PairRequest("abc", "Rodrigo's iPhone"));
        var wrong = await host.Client.PostAsJsonAsync("/api/v1/pair", new PairRequest("999999", "Rodrigo's iPhone"));

        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var malformedPayload = await malformed.Content.ReadFromJsonAsync<ErrorResponse>();
        var wrongPayload = await wrong.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(malformedPayload);
        Assert.NotNull(wrongPayload);
        Assert.Equal(malformedPayload.Message, wrongPayload.Message);
    }

    [Fact]
    public async Task StatusAndDisplays_ReturnDtos()
    {
        await using var host = await AgentApiTestHost.StartAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);

        var status = await host.Client.GetFromJsonAsync<StatusResponse>("/api/v1/status");
        Assert.NotNull(status);
        Assert.Equal("Living Room Gaming PC", status.AgentName);
        Assert.False(string.IsNullOrWhiteSpace(status.ComputerName));
        Assert.False(string.IsNullOrWhiteSpace(status.Version));
        Assert.Equal("desktop", status.Mode);
        Assert.Equal("idle", status.Operation);
        Assert.True(status.TvConnected);
        Assert.True(status.SteamInstalled);
        Assert.False(status.SteamRunning);
        Assert.Equal("http://192.168.1.40:47981", status.AgentBaseUrl);
        Assert.Equal(["192.168.1.40"], status.LanIpv4Addresses);
        Assert.Equal(["255.255.255.0"], status.LanIpv4SubnetMasks);
        Assert.Equal("192.168.1.255", status.BroadcastAddress);
        Assert.Equal("192.168.1.255", status.PreferredWakeOnLanBroadcastAddress);
        Assert.Equal(["192.168.1.255", "255.255.255.255"], status.WakeOnLanBroadcastAddresses);
        Assert.Equal("00:11:22:33:44:55", status.MacAddress);

        var displays = await host.Client.GetFromJsonAsync<List<DisplayResponse>>("/api/v1/displays");
        Assert.NotNull(displays);
        Assert.Equal(2, displays.Count);
        Assert.Contains(displays, static display => display.FriendlyName == "GS34WQC" && display.Primary);
        Assert.Contains(displays, static display => display.FriendlyName == "SAMSUNG");
    }

    [Fact]
    public async Task Status_UsesComputerNameWhenAgentNameIsDefaultPlaceholder()
    {
        await using var host = await AgentApiTestHost.StartAsync(agentName: "CouchControl Agent");
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);

        var status = await host.Client.GetFromJsonAsync<StatusResponse>("/api/v1/status");

        Assert.NotNull(status);
        Assert.False(string.IsNullOrWhiteSpace(status.ComputerName));
        Assert.Equal(status.ComputerName, status.AgentName);
    }

    [Fact]
    public async Task LauncherSelection_ReturnsRadioOptions_AndPersistsOneSelection()
    {
        await using var host = await AgentApiTestHost.StartAsync();
        host.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", host.Token);

        var initial = await host.Client.GetFromJsonAsync<LauncherSettingsResponse>(
            "/api/v1/launchers");
        Assert.NotNull(initial);
        Assert.Equal("none", initial.SelectedLauncher);
        Assert.Equal(["none", "steam", "heroic"], initial.Launchers.Select(static option => option.Id));

        var response = await host.Client.PutAsJsonAsync(
            "/api/v1/launchers/selected",
            new LauncherSelectionRequest("heroic"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<LauncherSettingsResponse>();
        Assert.NotNull(updated);
        Assert.Equal("heroic", updated.SelectedLauncher);

        var reloaded = await host.Client.GetFromJsonAsync<LauncherSettingsResponse>(
            "/api/v1/launchers");
        Assert.Equal("heroic", reloaded!.SelectedLauncher);
    }

    [Fact]
    public async Task LauncherSelection_RejectsUnknownLauncher()
    {
        await using var host = await AgentApiTestHost.StartAsync();
        host.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", host.Token);

        var response = await host.Client.PutAsJsonAsync(
            "/api/v1/launchers/selected",
            new LauncherSelectionRequest("both"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ActivateCouchMode_ReturnsAccepted_AndOperationCanBeFetched()
    {
        await using var host = await AgentApiTestHost.StartAsync(seedDesktopSnapshot: true);
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
    public async Task MouseInput_RequiresAuthentication_AndForwardsTrackpadEvents()
    {
        await using var host = await AgentApiTestHost.StartAsync();

        var unauthorized = await host.Client.PostAsJsonAsync(
            "/api/v1/input/mouse",
            new MouseInputRequest("move", DeltaX: 12, DeltaY: -7));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await host.Client.PostAsJsonAsync(
                "/api/v1/input/mouse",
                new MouseInputRequest("move", DeltaX: 12.4, DeltaY: -7.4))).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await host.Client.PostAsJsonAsync(
                "/api/v1/input/mouse",
                new MouseInputRequest("button", Button: "left", Pressed: true))).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await host.Client.PostAsJsonAsync(
                "/api/v1/input/mouse",
                new MouseInputRequest("button", Button: "left", Pressed: false))).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await host.Client.PostAsJsonAsync(
                "/api/v1/input/mouse",
                new MouseInputRequest("scroll", Delta: -120))).StatusCode);

        Assert.Equal(
            [
                "move:12,-7",
                "button:Left,True",
                "button:Left,False",
                "scroll:-120"
            ],
            host.MouseInput.Events);
    }

    [Fact]
    public async Task MouseInput_RejectsMalformedCommands()
    {
        await using var host = await AgentApiTestHost.StartAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);

        var unknownType = await host.Client.PostAsJsonAsync(
            "/api/v1/input/mouse",
            new MouseInputRequest("teleport"));
        var missingButtonState = await host.Client.PostAsJsonAsync(
            "/api/v1/input/mouse",
            new MouseInputRequest("button", Button: "left"));

        Assert.Equal(HttpStatusCode.BadRequest, unknownType.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingButtonState.StatusCode);
        Assert.Empty(host.MouseInput.Events);
    }

    [Fact]
    public async Task PairedDeviceToken_CanAccessProtectedEndpoints_ButNotAdministrativeEndpoints()
    {
        await using var host = await AgentApiTestHost.StartAsync();
        var pairedToken = await host.PairDeviceAsync("Rodrigo's iPhone");

        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pairedToken);
        var statusResponse = await host.Client.GetAsync("/api/v1/status");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

        var devicesResponse = await host.Client.GetAsync("/api/v1/paired-devices");
        Assert.Equal(HttpStatusCode.Unauthorized, devicesResponse.StatusCode);
    }

    [Fact]
    public async Task ActivateMode_Returns409_WhenAnotherOperationIsRunning()
    {
        var activationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentApiTestHost.StartAsync(seedDesktopSnapshot: true, activationGate: activationGate);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);

        var firstResponse = await host.Client.PostAsync("/api/v1/modes/couch", content: null);
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);

        var secondResponse = await host.Client.PostAsync("/api/v1/modes/desktop", content: null);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        activationGate.SetResult();
    }

    [Fact]
    public async Task ActivateMode_Returns409_DuringDisplaySettleWindowAfterOperationCompletes()
    {
        await using var host = await AgentApiTestHost.StartAsync(seedDesktopSnapshot: true);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);

        var firstResponse = await host.Client.PostAsync("/api/v1/modes/couch", content: null);
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        var accepted = await firstResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        Assert.NotNull(accepted);

        var operation = await host.WaitForOperationAsync(accepted.OperationId);
        Assert.Equal("succeeded", operation.State);

        var immediateResponse = await host.Client.PostAsync("/api/v1/modes/desktop", content: null);
        Assert.Equal(HttpStatusCode.Conflict, immediateResponse.StatusCode);

        host.AdvanceTime(TimeSpan.FromSeconds(16));

        var laterResponse = await host.Client.PostAsync("/api/v1/modes/desktop", content: null);
        Assert.Equal(HttpStatusCode.Accepted, laterResponse.StatusCode);
    }

    [Fact]
    public async Task GetOperation_Returns404_ForUnknownOperation()
    {
        await using var host = await AgentApiTestHost.StartAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);

        var response = await host.Client.GetAsync($"/api/v1/operations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RevokedDeviceToken_StopsWorkingImmediately()
    {
        await using var host = await AgentApiTestHost.StartAsync();
        var pairedToken = await host.PairDeviceAsync("Rodrigo's iPhone");

        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);
        var devices = await host.Client.GetFromJsonAsync<List<PairedDeviceResponse>>("/api/v1/paired-devices");
        Assert.NotNull(devices);
        var device = Assert.Single(devices);

        var deleteResponse = await host.Client.DeleteAsync($"/api/v1/paired-devices/{device.DeviceId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pairedToken);
        var statusResponse = await host.Client.GetAsync("/api/v1/status");
        Assert.Equal(HttpStatusCode.Unauthorized, statusResponse.StatusCode);
    }

    [Fact]
    public async Task MultiplePairedDevices_AreTrackedIndependently()
    {
        await using var host = await AgentApiTestHost.StartAsync();
        var iphoneToken = await host.PairDeviceAsync("Rodrigo's iPhone");
        var ipadToken = await host.PairDeviceAsync("Rodrigo's iPad");

        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token);
        var devices = await host.Client.GetFromJsonAsync<List<PairedDeviceResponse>>("/api/v1/paired-devices");
        Assert.NotNull(devices);
        Assert.Equal(2, devices.Count);
        Assert.Contains(devices, static device => device.DeviceName == "Rodrigo's iPhone");
        Assert.Contains(devices, static device => device.DeviceName == "Rodrigo's iPad");

        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", iphoneToken);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/v1/status")).StatusCode);

        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ipadToken);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/v1/status")).StatusCode);
    }

    private sealed class AgentApiTestHost : IAsyncDisposable
    {
        private readonly WebApplication app;
        private readonly MutableTimeProvider timeProvider;

        private AgentApiTestHost(
            WebApplication app,
            HttpClient client,
            string token,
            MutableTimeProvider timeProvider,
            FakeMouseInputService mouseInput)
        {
            this.app = app;
            this.timeProvider = timeProvider;
            Client = client;
            Token = token;
            MouseInput = mouseInput;
        }

        public HttpClient Client { get; }

        public string Token { get; }

        public FakeMouseInputService MouseInput { get; }

        public static async Task<AgentApiTestHost> StartAsync(
            bool seedDesktopSnapshot = false,
            string agentName = "Living Room Gaming PC",
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
            var timeProvider = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-15T12:00:00Z"));

            var configStore = new InMemoryAgentConfigurationStore();
            await configStore.SaveAsync(new AgentConfiguration
            {
                AgentName = agentName,
                CouchDisplayIdentifier = new DisplayIdentifier(TvPath),
                CouchDisplayIdentity = new CouchDisplayIdentity(TvPath, "SAMSUNG", "SAM", "735A", "UID33029", "00000000:000135B1", 33029),
                LaunchSteamAutomatically = false,
                CouchLauncher = CouchLauncher.None,
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
            builder.Services.AddSingleton<IDisplayOperationJournalStore, JsonDisplayOperationJournalStore>();
            builder.Services.AddSingleton<IDisplayManager>(new FakeDisplayManager(activationGate));
            builder.Services.AddSingleton<ISteamLauncher>(new FakeSteamLauncher());
            builder.Services.AddSingleton<IModeAutomationService, FakeModeAutomationService>();
            builder.Services.AddSingleton<IDisplayMatchingService, DisplayMatchingService>();
            builder.Services.AddSingleton<ProfileOrchestrator>();
            builder.Services.AddSingleton<IProfileOrchestrator>(static services => services.GetRequiredService<ProfileOrchestrator>());
            builder.Services.AddCouchControlAgentApi();
            var mouseInput = new FakeMouseInputService();
            builder.Services.AddSingleton<IMouseInputService>(mouseInput);
            builder.Services.AddSingleton<IProtectedDataService, PassthroughProtectedDataService>();
            builder.Services.AddSingleton<TimeProvider>(timeProvider);
            builder.Services.AddSingleton<ILocalNetworkInterfaceProvider, FakeLocalNetworkInterfaceProvider>();

            var app = builder.Build();
            app.MapCouchControlAgentApi();
            await AgentApiApplicationExtensions.InitializeAgentApiAsync(app.Services);
            await app.StartAsync();

            var token = await app.Services.GetRequiredService<IApiTokenStore>().GetTokenAsync();
            return new AgentApiTestHost(app, app.GetTestClient(), token, timeProvider, mouseInput);
        }

        public void AdvanceTime(TimeSpan by) => timeProvider.Advance(by);

        public async Task<PairingSession> StartPairingSessionAsync() =>
            await app.Services.GetRequiredService<IPairingService>().StartAsync();

        public async Task<string> PairDeviceAsync(string deviceName)
        {
            var session = await StartPairingSessionAsync();
            var response = await Client.PostAsJsonAsync("/api/v1/pair", new PairRequest(session.PairingCode, deviceName));
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<PairResponse>();
            Assert.NotNull(payload);
            return payload.Token;
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

        public Task<OperationResult> PrepareForCouchModeAsync(
            AgentConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success("Prepared TV."));

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

        public Task<OperationResult> ExitBigPictureAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success("Steam exited.", outcome: "Success"));
    }

    private sealed class FakeModeAutomationService : IModeAutomationService
    {
        public Task<OperationResult> RunPostActivationAsync(
            AgentMode mode,
            AgentConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success("No audio switch command configured."));
    }

    public sealed class FakeMouseInputService : IMouseInputService
    {
        public List<string> Events { get; } = [];

        public void Move(int deltaX, int deltaY) => Events.Add($"move:{deltaX},{deltaY}");

        public void Scroll(int delta) => Events.Add($"scroll:{delta}");

        public void Button(MouseButton button, bool pressed) => Events.Add($"button:{button},{pressed}");
    }

    private sealed class FakeLocalNetworkInterfaceProvider : ILocalNetworkInterfaceProvider
    {
        private static readonly AgentNetworkInterface Adapter = new(
            "ethernet",
            "Ethernet",
            "Intel Ethernet Adapter",
            "00:11:22:33:44:55",
            NetworkInterfaceType.Ethernet,
            7,
            true,
            false,
            false,
            false,
            true,
            false,
            ["192.168.1.40"],
            ["255.255.255.0"],
            ["192.168.1.255", "255.255.255.255"],
            true);

        public IReadOnlyList<AgentNetworkInterface> GetInterfaces() => [Adapter];

        public AgentApiBindingPlan CreateBindingPlan(AgentConfiguration configuration) =>
            new(
                configuration.ApiPort,
                AgentApiListeningInterface.Automatic,
                Adapter.Id,
                Adapter.Name,
                Adapter.MacAddress,
                [$"http://192.168.1.40:{configuration.ApiPort}"],
                Adapter.LanIpv4Addresses,
                Adapter.LanIpv4SubnetMasks,
                "192.168.1.255",
                Adapter.WakeOnLanBroadcastAddresses,
                true,
                "Ethernet: 192.168.1.40");

        public string GetNetworkProfileStatus(string? selectedInterfaceId = null) => "Private";
    }

    private sealed class PassthroughProtectedDataService : IProtectedDataService
    {
        public byte[] Protect(byte[] plaintext) => plaintext;

        public byte[] Unprotect(byte[] ciphertext) => ciphertext;
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset now;

        public MutableTimeProvider(DateTimeOffset now)
        {
            this.now = now;
        }

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now = now.Add(by);
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
