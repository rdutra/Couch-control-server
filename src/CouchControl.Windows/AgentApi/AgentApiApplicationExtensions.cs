using System.Reflection;
using System.Linq;
using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CouchControl.Windows.AgentApi;

public static class AgentApiApplicationExtensions
{
    public static IServiceCollection AddCouchControlAgentApi(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(static options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        });

        services.AddSingleton<IProtectedDataService, DpapiProtectedDataService>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IApiTokenStore, ApiTokenStore>();
        services.AddSingleton<IPairingService, PairingService>();
        services.AddSingleton<ILocalNetworkInterfaceProvider, LocalNetworkInterfaceProvider>();
        services.AddSingleton<IWindowsFirewallRuleManager, WindowsFirewallRuleManager>();
        services.AddSingleton<IAgentApiHealthState, AgentApiHealthState>();
        services.AddSingleton<IAgentNetworkDiagnosticsService, AgentNetworkDiagnosticsService>();
        services.AddSingleton<AgentApiRuntimeOptionsProvider>();
        services.AddSingleton<IAgentApiOperationService, AgentApiOperationService>();
        services.AddSingleton<IMouseInputService, WindowsMouseInputService>();

        return services;
    }

    public static WebApplication MapCouchControlAgentApi(this WebApplication app)
    {
        app.UseAgentApiNoStore();

        app.MapGet("/api/v1/health", () => Results.Ok(new HealthResponse(true, GetVersion())));

        app.UseAgentApiCors();
        app.UseAgentApiRequestLogging();

        app.MapPost("/api/v1/pair", PairAsync);

        var protectedApi = app.MapGroup("/api/v1");
        protectedApi.AddEndpointFilter<AgentApiAuthorizationFilter>();

        protectedApi.MapGet("/status", GetStatusAsync);
        protectedApi.MapGet("/displays", GetDisplaysAsync);
        protectedApi.MapGet("/launchers", GetLaunchersAsync);
        protectedApi.MapPut("/launchers/selected", SetSelectedLauncherAsync);
        protectedApi.MapPost("/modes/couch", StartCouchModeAsync);
        protectedApi.MapPost("/modes/desktop", StartDesktopModeAsync);
        protectedApi.MapPost("/input/mouse", SendMouseInput);
        protectedApi.MapGet("/operations/{operationId:guid}", GetOperationAsync);
        protectedApi.MapGet("/paired-devices", GetPairedDevicesAsync)
            .AddEndpointFilter<AdministrativeApiAuthorizationFilter>();
        protectedApi.MapDelete("/paired-devices/{deviceId}", DeletePairedDeviceAsync)
            .AddEndpointFilter<AdministrativeApiAuthorizationFilter>();

        return app;
    }

    public static async Task InitializeAgentApiAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await services.GetRequiredService<AgentApiRuntimeOptionsProvider>().LoadAsync(cancellationToken);
        await services.GetRequiredService<IApiTokenStore>().EnsureTokenExistsAsync(cancellationToken);
    }

    private static async Task<IResult> GetStatusAsync(
        IAgentConfigurationStore configurationStore,
        IDisplaySnapshotStore snapshotStore,
        IDisplayManager displayManager,
        IProfileOrchestrator orchestrator,
        ISteamLauncher steamLauncher,
        ILocalNetworkInterfaceProvider networkInterfaceProvider,
        CancellationToken cancellationToken)
    {
        var configuration = await configurationStore.LoadAsync(cancellationToken);
        var snapshot = await snapshotStore.LoadLastDesktopSnapshotAsync(cancellationToken);
        var displays = await displayManager.GetDisplaysAsync(cancellationToken);
        var status = orchestrator.GetStatus();
        var bindingPlan = networkInterfaceProvider.CreateBindingPlan(configuration);

        bool tvConnected = configuration.CouchDisplayIdentifier is not null &&
            displays.Any(display => display.Identifier.Matches(configuration.CouchDisplayIdentifier));

        return Results.Ok(new StatusResponse(
            configuration.AgentName,
            GetVersion(),
            ToModeValue(status.CurrentMode),
            ToOperationValue(status.CurrentOperation, status.State),
            ToStepValue(status.CurrentStep, status.State),
            configuration.CouchDisplayIdentity?.FriendlyName ?? configuration.CouchDisplayIdentifier?.Value,
            ToModeSummary(
                configuration.CouchDisplayIdentity?.FriendlyName ?? configuration.CouchDisplayIdentifier?.Value,
                configuration.PreferredCouchMode),
            ToDesktopSnapshotSummary(snapshot),
            tvConnected,
            steamLauncher.IsInstalled(configuration),
            steamLauncher.IsRunning(),
            bindingPlan.ListenUrls.FirstOrDefault(),
            bindingPlan.LanIpv4Addresses,
            bindingPlan.LanIpv4SubnetMasks,
            bindingPlan.PreferredWakeOnLanBroadcastAddress,
            bindingPlan.WakeOnLanBroadcastAddresses,
            bindingPlan.MacAddress,
            status.LastOperationResult?.Message));
    }

    private static async Task<IResult> GetDisplaysAsync(
        IDisplayManager displayManager,
        CancellationToken cancellationToken)
    {
        var displays = await displayManager.GetDisplaysAsync(cancellationToken);
        return Results.Ok(displays.Select(static display => new DisplayResponse(
            display.Identifier.Value,
            display.FriendlyName,
            display.IsActive,
            display.IsPrimary,
            display.CurrentMode is null
                ? null
                : new DisplayModeResponse(
                    display.CurrentMode.Width,
                    display.CurrentMode.Height,
                    display.CurrentMode.RefreshRateHz),
            display.OutputTechnology ?? "Unknown")));
    }

    private static async Task<IResult> GetLaunchersAsync(
        IAgentConfigurationStore configurationStore,
        ISteamLauncher launcherService,
        CancellationToken cancellationToken)
    {
        var configuration = await configurationStore.LoadAsync(cancellationToken);
        return Results.Ok(CreateLauncherSettings(configuration, launcherService));
    }

    private static async Task<IResult> SetSelectedLauncherAsync(
        LauncherSelectionRequest request,
        IAgentConfigurationStore configurationStore,
        ISteamLauncher launcherService,
        CancellationToken cancellationToken)
    {
        if (!TryParseLauncher(request.Launcher, out var launcher))
        {
            return Results.BadRequest(new ErrorResponse(
                "Launcher must be one of: none, steam, heroic."));
        }

        var configuration = await configurationStore.LoadAsync(cancellationToken);
        var updatedConfiguration = configuration with
        {
            CouchLauncher = launcher,
            LaunchSteamAutomatically = launcher == CouchLauncher.SteamBigPicture
        };
        await configurationStore.SaveAsync(updatedConfiguration, cancellationToken);

        return Results.Ok(CreateLauncherSettings(updatedConfiguration, launcherService));
    }

    private static LauncherSettingsResponse CreateLauncherSettings(
        AgentConfiguration configuration,
        ISteamLauncher launcherService) =>
        new(
            ToLauncherValue(configuration.CouchLauncher),
            [
                new LauncherOptionResponse("none", "None", true),
                new LauncherOptionResponse(
                    "steam",
                    "Steam — Big Picture",
                    launcherService.IsInstalled(configuration)),
                new LauncherOptionResponse(
                    "heroic",
                    "Heroic — Console Mode",
                    launcherService.IsHeroicInstalled(configuration))
            ]);

    private static bool TryParseLauncher(string? value, out CouchLauncher launcher)
    {
        launcher = value?.Trim().ToLowerInvariant() switch
        {
            "none" => CouchLauncher.None,
            "steam" => CouchLauncher.SteamBigPicture,
            "heroic" => CouchLauncher.HeroicConsole,
            _ => (CouchLauncher)(-1)
        };
        return Enum.IsDefined(launcher);
    }

    private static string ToLauncherValue(CouchLauncher launcher) => launcher switch
    {
        CouchLauncher.SteamBigPicture => "steam",
        CouchLauncher.HeroicConsole => "heroic",
        _ => "none"
    };

    private static async Task<IResult> PairAsync(
        PairRequest request,
        IPairingService pairingService,
        IAgentConfigurationStore configurationStore,
        ILocalNetworkInterfaceProvider networkInterfaceProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceName) || string.IsNullOrWhiteSpace(request.PairingCode))
        {
            return Results.BadRequest(new ErrorResponse("Pairing failed."));
        }

        var result = await pairingService.PairAsync(request.PairingCode, request.DeviceName, cancellationToken);
        if (!result.Succeeded || result.Token is null)
        {
            return result.FailureReason == PairingFailureReason.RateLimited
                ? Results.StatusCode(StatusCodes.Status429TooManyRequests)
                : Results.BadRequest(new ErrorResponse("Pairing failed."));
        }

        var configuration = await configurationStore.LoadAsync(cancellationToken);
        var bindingPlan = networkInterfaceProvider.CreateBindingPlan(configuration);
        return Results.Ok(new PairResponse(
            result.Token.Token,
            configuration.AgentName,
            "v1",
            bindingPlan.ListenUrls.FirstOrDefault(),
            bindingPlan.LanIpv4Addresses,
            bindingPlan.LanIpv4SubnetMasks,
            bindingPlan.PreferredWakeOnLanBroadcastAddress,
            bindingPlan.WakeOnLanBroadcastAddresses,
            bindingPlan.MacAddress));
    }

    private static IResult StartCouchModeAsync(IAgentApiOperationService operationService)
    {
        if (!operationService.TryStartActivateCouchMode(out var operationId))
        {
            return Results.Conflict(new ErrorResponse("Another profile operation is already running or the previous display change is still settling."));
        }

        return Results.Accepted($"/api/v1/operations/{operationId}", new OperationAcceptedResponse(true, operationId));
    }

    private static IResult StartDesktopModeAsync(IAgentApiOperationService operationService)
    {
        if (!operationService.TryStartActivateDesktopMode(out var operationId))
        {
            return Results.Conflict(new ErrorResponse("Another profile operation is already running or the previous display change is still settling."));
        }

        return Results.Accepted($"/api/v1/operations/{operationId}", new OperationAcceptedResponse(true, operationId));
    }

    private static IResult SendMouseInput(MouseInputRequest request, IMouseInputService mouse)
    {
        const int maximumMovement = 500;
        const int maximumScroll = 1200;

        switch (request.Type?.Trim().ToLowerInvariant())
        {
            case "move":
                mouse.Move(
                    Math.Clamp((int)Math.Round(request.DeltaX), -maximumMovement, maximumMovement),
                    Math.Clamp((int)Math.Round(request.DeltaY), -maximumMovement, maximumMovement));
                break;
            case "scroll":
                mouse.Scroll(Math.Clamp(request.Delta, -maximumScroll, maximumScroll));
                break;
            case "button":
                if (!Enum.TryParse<MouseButton>(request.Button, true, out var button) || request.Pressed is null)
                {
                    return Results.BadRequest(new ErrorResponse("A supported button and pressed state are required."));
                }

                mouse.Button(button, request.Pressed.Value);
                break;
            default:
                return Results.BadRequest(new ErrorResponse("Mouse input type must be move, scroll, or button."));
        }

        return Results.NoContent();
    }

    private static IResult GetOperationAsync(Guid operationId, IAgentApiOperationService operationService)
    {
        if (!operationService.TryGetOperation(operationId, out var operation) || operation is null)
        {
            return Results.NotFound(new ErrorResponse("Unknown operation ID."));
        }

        return Results.Ok(ToOperationResponse(operation));
    }

    private static async Task<IResult> GetPairedDevicesAsync(
        IApiTokenStore tokenStore,
        CancellationToken cancellationToken)
    {
        var devices = await tokenStore.GetPairedDevicesAsync(cancellationToken);
        return Results.Ok(devices.Select(static device => new PairedDeviceResponse(
            device.DeviceId,
            device.DeviceName,
            device.PairedAtUtc,
            device.LastSeenAtUtc)));
    }

    private static async Task<IResult> DeletePairedDeviceAsync(
        string deviceId,
        IApiTokenStore tokenStore,
        CancellationToken cancellationToken)
    {
        var removed = await tokenStore.RevokeDeviceAsync(deviceId, cancellationToken);
        return removed
            ? Results.NoContent()
            : Results.NotFound(new ErrorResponse("Unknown device ID."));
    }

    private static OperationResponse ToOperationResponse(AgentOperationRecord operation) =>
        new(
            operation.OperationId,
            operation.Mode == AgentMode.Couch ? "couch" : "desktop",
            operation.OperationType == ProfileOperationType.ActivateCouchMode ? "activateCouchMode" : "activateDesktopMode",
            operation.State switch
            {
                AgentApiOperationState.Running => "running",
                AgentApiOperationState.Succeeded => "succeeded",
                AgentApiOperationState.PartiallySucceeded => "partialSuccess",
                _ => "failed"
            },
            operation.AcceptedAtUtc,
            operation.StartedAtUtc,
            operation.CompletedAtUtc,
            operation.Message,
            operation.ErrorCode,
            operation.Result is null
                ? null
                : new OperationResultResponse(
                    operation.Result.Status.ToString(),
                    operation.Result.DisplayResult.Message,
                    operation.Result.SteamResult?.Message,
                    operation.Result.DisplayResult.Outcome,
                    operation.Result.SteamResult?.Outcome));

    private static string GetVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AgentApiApplicationExtensions).Assembly;
        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static string ToModeValue(AgentMode? mode) =>
        mode switch
        {
            AgentMode.Couch => "couch",
            AgentMode.Desktop => "desktop",
            _ => "desktop"
        };

    private static string ToOperationValue(ProfileOperationType operation, AgentOperationState state) =>
        state == AgentOperationState.Idle || operation == ProfileOperationType.None
            ? "idle"
            : operation == ProfileOperationType.ActivateCouchMode
                ? "activateCouchMode"
                : "activateDesktopMode";

    private static string? ToStepValue(ProfileOperationStep step, AgentOperationState state) =>
        state == AgentOperationState.Idle || step == ProfileOperationStep.None
            ? null
            : step.ToString();

    private static ModeSummaryResponse? ToModeSummary(string? displayName, DisplayMode? mode) =>
        mode is null
            ? null
            : new ModeSummaryResponse(displayName, mode.Width, mode.Height, mode.RefreshRateHz);

    private static ModeSummaryResponse? ToDesktopSnapshotSummary(DisplaySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var primaryPath = snapshot.Paths.FirstOrDefault(static path => path.IsPrimary && path.IsActive);
        var selectedPath = primaryPath ?? snapshot.Paths.FirstOrDefault(static path => path.IsActive);
        if (selectedPath is null || selectedPath.SourceMode is null)
        {
            return null;
        }

        var displayName = snapshot.Displays
            .FirstOrDefault(display => display.Identifier.Matches(selectedPath.Identifier))
            ?.FriendlyName;

        return new ModeSummaryResponse(
            displayName,
            (int)selectedPath.SourceMode.Width,
            (int)selectedPath.SourceMode.Height,
            selectedPath.RefreshRate.Hertz);
    }
}

internal sealed class AgentApiAuthorizationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        var header = request.Headers.Authorization.ToString();
        if (!TryExtractBearerToken(header, out var token))
        {
            return Results.Unauthorized();
        }

        var tokenStore = context.HttpContext.RequestServices.GetRequiredService<IApiTokenStore>();
        var principal = await tokenStore.AuthenticateAsync(token, context.HttpContext.RequestAborted);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        context.HttpContext.Items[AuthenticatedApiTokenHttpContextExtensions.ItemKey] = principal;
        return await next(context);
    }

    private static bool TryExtractBearerToken(string authorizationHeader, out string token)
    {
        const string prefix = "Bearer ";
        if (authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            token = authorizationHeader[prefix.Length..].Trim();
            return !string.IsNullOrWhiteSpace(token);
        }

        token = string.Empty;
        return false;
    }
}

internal sealed class AdministrativeApiAuthorizationFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var principal = context.HttpContext.GetAuthenticatedApiToken();
        if (principal is null || !principal.IsAdministrative)
        {
            return ValueTask.FromResult<object?>(Results.Unauthorized());
        }

        return next(context);
    }
}

internal static class AuthenticatedApiTokenHttpContextExtensions
{
    public const string ItemKey = "__couchcontrol_authenticated_token";

    public static AuthenticatedApiToken? GetAuthenticatedApiToken(this HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value)
            ? value as AuthenticatedApiToken
            : null;
}

internal static class AgentApiMiddlewareExtensions
{
    public static IApplicationBuilder UseAgentApiNoStore(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/v1"))
            {
                context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
                context.Response.Headers.Pragma = "no-cache";
                context.Response.Headers.Expires = "0";
            }

            await next();
        });

    public static IApplicationBuilder UseAgentApiRequestLogging(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("CouchControl.Agent.Api");
            var method = context.Request.Method;
            var path = context.Request.Path;
            await next();
            logger.LogInformation("HTTP {Method} {Path} responded {StatusCode}", method, path, context.Response.StatusCode);
        });

    public static IApplicationBuilder UseAgentApiCors(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var optionsProvider = context.RequestServices.GetRequiredService<AgentApiRuntimeOptionsProvider>();
            var origin = context.Request.Headers.Origin.ToString();

            if (!string.IsNullOrWhiteSpace(origin) &&
                optionsProvider.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            {
                context.Response.Headers.AccessControlAllowOrigin = origin;
                context.Response.Headers.AccessControlAllowHeaders = "Authorization, Content-Type";
                context.Response.Headers.AccessControlAllowMethods = "GET, POST, DELETE, OPTIONS";
                context.Response.Headers.Vary = "Origin";

                if (HttpMethods.IsOptions(context.Request.Method))
                {
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return;
                }
            }

            await next();
        });
}

public sealed record HealthResponse(bool Healthy, string Version);

public sealed record ErrorResponse(string Message);

public sealed record PairRequest(string PairingCode, string DeviceName);

public sealed record MouseInputRequest(
    string Type,
    double DeltaX = 0,
    double DeltaY = 0,
    int Delta = 0,
    string? Button = null,
    bool? Pressed = null);

public sealed record PairResponse(
    string Token,
    string AgentName,
    string ApiVersion,
    string? AgentBaseUrl,
    IReadOnlyList<string> LanIpv4Addresses,
    IReadOnlyList<string> LanIpv4SubnetMasks,
    string? PreferredWakeOnLanBroadcastAddress,
    IReadOnlyList<string> WakeOnLanBroadcastAddresses,
    string? MacAddress);

public sealed record OperationAcceptedResponse(bool Accepted, Guid OperationId);

public sealed record LauncherSelectionRequest(string? Launcher);

public sealed record LauncherSettingsResponse(
    string SelectedLauncher,
    IReadOnlyList<LauncherOptionResponse> Launchers);

public sealed record LauncherOptionResponse(
    string Id,
    string Name,
    bool Available);

public sealed record StatusResponse(
    string AgentName,
    string Version,
    string Mode,
    string Operation,
    string? CurrentStep,
    string? ConfiguredTv,
    ModeSummaryResponse? ConfiguredCouchMode,
    ModeSummaryResponse? DesktopSnapshotMode,
    bool TvConnected,
    bool SteamInstalled,
    bool SteamRunning,
    string? AgentBaseUrl,
    IReadOnlyList<string> LanIpv4Addresses,
    IReadOnlyList<string> LanIpv4SubnetMasks,
    string? PreferredWakeOnLanBroadcastAddress,
    IReadOnlyList<string> WakeOnLanBroadcastAddresses,
    string? MacAddress,
    string? LastResult);

public sealed record ModeSummaryResponse(
    string? DisplayName,
    int Width,
    int Height,
    decimal RefreshRateHz);

public sealed record DisplayResponse(
    string Identifier,
    string FriendlyName,
    bool Active,
    bool Primary,
    DisplayModeResponse? CurrentMode,
    string OutputTechnology);

public sealed record DisplayModeResponse(
    int Width,
    int Height,
    decimal RefreshRateHz);

public sealed record OperationResponse(
    Guid OperationId,
    string Mode,
    string OperationType,
    string State,
    DateTimeOffset AcceptedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Message,
    string? ErrorCode,
    OperationResultResponse? Result);

public sealed record OperationResultResponse(
    string Status,
    string DisplayMessage,
    string? SteamMessage,
    string? DisplayOutcome,
    string? SteamOutcome);

public sealed record PairedDeviceResponse(
    string DeviceId,
    string DeviceName,
    DateTimeOffset PairedAtUtc,
    DateTimeOffset? LastSeenAtUtc);
