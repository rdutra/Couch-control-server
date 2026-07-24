using CouchControl.Agent.Hosting;
using CouchControl.Agent.Logging;
using CouchControl.Agent.Status;
using CouchControl.Windows;
using CouchControl.Windows.AgentApi;
using CouchControl.Windows.Runtime;
using CouchControl.Windows.Startup;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

ApplicationConfiguration.Initialize();

using var app = CreateApp(args);
using var scope = app.Services.CreateScope();
var services = scope.ServiceProvider;

var singleInstanceCoordinator = services.GetRequiredService<ISingleInstanceCoordinator>();
if (!singleInstanceCoordinator.TryAcquirePrimaryInstance())
{
    _ = singleInstanceCoordinator.NotifyPrimaryInstance();
    return;
}

var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("CouchControl.Agent");

Application.ThreadException += (_, eventArgs) =>
    logger.LogError(eventArgs.Exception, "An unhandled UI exception occurred.");

AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    if (eventArgs.ExceptionObject is Exception exception)
    {
        logger.LogError(exception, "A non-UI unhandled exception occurred.");
    }
};

await AgentApiApplicationExtensions.InitializeAgentApiAsync(app.Services);
var apiOptions = app.Services.GetRequiredService<AgentApiRuntimeOptionsProvider>();
var apiHealthState = app.Services.GetRequiredService<IAgentApiHealthState>();
if (apiOptions.BindingPlan.ListenUrls.Count > 0)
{
    foreach (var listenUrl in apiOptions.BindingPlan.ListenUrls)
    {
        app.Urls.Add(listenUrl);
    }

    await app.StartAsync();
    apiHealthState.MarkListening(apiOptions.BindingPlan.ListenUrls);
}
else
{
    apiHealthState.MarkNotListening(apiOptions.BindingPlan.StatusMessage);
}

try
{
    var applicationContext = services.GetRequiredService<AgentApplicationContext>();
    applicationContext.StartFirstRunSetupCheck();
    applicationContext.StartStartupRecoveryCheck();
    Application.Run(applicationContext);
}
finally
{
    if (app.Urls.Count > 0)
    {
        await app.StopAsync();
    }
}

static WebApplication CreateApp(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });

    builder.Services.AddCouchControlWindows();
    builder.Services.AddSingleton<IStartupRegistration, CurrentUserStartupRegistration>();
    builder.Services.AddSingleton<ISingleInstanceCoordinator>(_ => new SingleInstanceCoordinator("CouchControl.Agent"));
    builder.Services.AddSingleton<AgentFileLoggerProvider>();
    builder.Services.AddSingleton<IAgentLogFileAccessor>(static services => services.GetRequiredService<AgentFileLoggerProvider>());
    builder.Logging.Services.AddSingleton<ILoggerProvider>(static services => services.GetRequiredService<AgentFileLoggerProvider>());
    builder.Services.AddSingleton<IAgentStatusService, AgentStatusService>();
    builder.Services.AddSingleton<AgentApplicationContext>();

    var app = builder.Build();
    app.MapCouchControlAgentApi();
    return app;
}
