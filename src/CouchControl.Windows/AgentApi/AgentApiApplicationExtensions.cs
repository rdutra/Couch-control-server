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
        services.AddSingleton<IApiTokenStore, ApiTokenStore>();
        services.AddSingleton<AgentApiRuntimeOptionsProvider>();
        services.AddSingleton<IAgentApiOperationService, AgentApiOperationService>();

        return services;
    }

    public static WebApplication MapCouchControlAgentApi(this WebApplication app)
    {
        app.MapGet("/api/v1/health", () => Results.Ok(new HealthResponse(true)));

        app.UseAgentApiCors();
        app.UseAgentApiRequestLogging();

        var protectedApi = app.MapGroup("/api/v1");
        protectedApi.AddEndpointFilter<AgentApiAuthorizationFilter>();

        protectedApi.MapGet("/status", GetStatusAsync);
        protectedApi.MapGet("/displays", GetDisplaysAsync);
        protectedApi.MapPost("/modes/couch", StartCouchModeAsync);
        protectedApi.MapPost("/modes/desktop", StartDesktopModeAsync);
        protectedApi.MapGet("/operations/{operationId:guid}", GetOperationAsync);

        return app;
    }

    public static async Task InitializeAgentApiAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await services.GetRequiredService<AgentApiRuntimeOptionsProvider>().LoadAsync(cancellationToken);
        await services.GetRequiredService<IApiTokenStore>().EnsureTokenExistsAsync(cancellationToken);
    }

    private static async Task<IResult> GetStatusAsync(
        IAgentConfigurationStore configurationStore,
        IDisplayManager displayManager,
        IProfileOrchestrator orchestrator,
        ISteamLauncher steamLauncher,
        CancellationToken cancellationToken)
    {
        var configuration = await configurationStore.LoadAsync(cancellationToken);
        var displays = await displayManager.GetDisplaysAsync(cancellationToken);
        var status = orchestrator.GetStatus();

        bool tvConnected = configuration.CouchDisplayIdentifier is not null &&
            displays.Any(display => display.Identifier.Matches(configuration.CouchDisplayIdentifier));

        return Results.Ok(new StatusResponse(
            configuration.AgentName,
            GetVersion(),
            ToModeValue(status.CurrentMode),
            ToOperationValue(status.CurrentOperation, status.State),
            ToStepValue(status.CurrentStep, status.State),
            configuration.CouchDisplayIdentity?.FriendlyName ?? configuration.CouchDisplayIdentifier?.Value,
            tvConnected,
            steamLauncher.IsInstalled(configuration),
            steamLauncher.IsRunning(),
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

    private static IResult StartCouchModeAsync(IAgentApiOperationService operationService)
    {
        if (!operationService.TryStartActivateCouchMode(out var operationId))
        {
            return Results.Conflict(new ErrorResponse("Another profile operation is already running."));
        }

        return Results.Accepted($"/api/v1/operations/{operationId}", new OperationAcceptedResponse(true, operationId));
    }

    private static IResult StartDesktopModeAsync(IAgentApiOperationService operationService)
    {
        if (!operationService.TryStartActivateDesktopMode(out var operationId))
        {
            return Results.Conflict(new ErrorResponse("Another profile operation is already running."));
        }

        return Results.Accepted($"/api/v1/operations/{operationId}", new OperationAcceptedResponse(true, operationId));
    }

    private static IResult GetOperationAsync(Guid operationId, IAgentApiOperationService operationService)
    {
        if (!operationService.TryGetOperation(operationId, out var operation) || operation is null)
        {
            return Results.NotFound(new ErrorResponse("Unknown operation ID."));
        }

        return Results.Ok(ToOperationResponse(operation));
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
        if (!await tokenStore.ValidateAsync(token, context.HttpContext.RequestAborted))
        {
            return Results.Unauthorized();
        }

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

internal static class AgentApiMiddlewareExtensions
{
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
                context.Response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
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

public sealed record HealthResponse(bool Healthy);

public sealed record ErrorResponse(string Message);

public sealed record OperationAcceptedResponse(bool Accepted, Guid OperationId);

public sealed record StatusResponse(
    string AgentName,
    string Version,
    string Mode,
    string Operation,
    string? CurrentStep,
    string? ConfiguredTv,
    bool TvConnected,
    bool SteamInstalled,
    bool SteamRunning,
    string? LastResult);

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
