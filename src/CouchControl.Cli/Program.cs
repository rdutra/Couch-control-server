using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using CouchControl.Core.Orchestration;
using CouchControl.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

var argsList = args.ToList();
bool isJson = argsList.Contains("--json");
bool isDisplays = argsList.Contains("displays");
bool isConfigure = argsList.Contains("configure");
bool isSnapshot = argsList.Contains("snapshot");
bool isCouch = argsList.Contains("couch");
bool isDesktop = argsList.Contains("desktop");
bool isSteam = argsList.Contains("steam");
bool isDryRun = argsList.Contains("--dry-run");
bool isForceFallback = argsList.Contains("--force-fallback");

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
if (isJson)
{
    builder.Logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });
}
else
{
    builder.Logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
}

builder.Services.AddCouchControlWindows();

using var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("CouchControl.Cli");

if (!isDisplays && !isConfigure && !isSnapshot && !isCouch && !isDesktop && !isSteam)
{
    if (isJson)
    {
        Console.Error.WriteLine("Error: Unsupported command. Supported commands: displays, configure, snapshot, couch, desktop, steam.");
        return 1;
    }

    Console.WriteLine("CouchControl CLI");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  CouchControl.Cli displays [--json]                         List active and connected displays");
    Console.WriteLine("  CouchControl.Cli couch [--dry-run] [--json]                Switch to couch mode using the configured TV");
    Console.WriteLine("  CouchControl.Cli desktop [--dry-run] [--force-fallback] [--json] Restore the saved desktop topology");
    Console.WriteLine("  CouchControl.Cli steam [--json]                            Launch Steam Big Picture mode");
    Console.WriteLine("  CouchControl.Cli configure <subcommand> [options] [--json] Configure agent settings");
    Console.WriteLine("  CouchControl.Cli snapshot <capture|show> [--json]         Capture or inspect the saved desktop snapshot");
    Console.WriteLine();
    Console.WriteLine("Use 'CouchControl.Cli configure' or 'CouchControl.Cli snapshot' for more options.");
    return 0;
}

if (isDisplays)
{
    return await HandleDisplaysCommand(host, isJson, logger);
}

if (isCouch)
{
    return await HandleCouchCommand(host, isJson, isDryRun, logger);
}

if (isDesktop)
{
    return await HandleDesktopCommand(host, isJson, isDryRun, isForceFallback, logger);
}

if (isSteam)
{
    return await HandleSteamCommand(host, isJson, logger);
}

if (isConfigure)
{
    if (argsList.Count < 2 || argsList[1].StartsWith("-"))
    {
        PrintConfigureUsage();
        return 1;
    }

    string subCommand = argsList[1].ToLower();
    switch (subCommand)
    {
        case "list-displays":
            return await HandleListDisplays(host, isJson, logger);
        case "set-tv":
            int idIdx = argsList.IndexOf("--display-id");
            if (idIdx == -1 || idIdx + 1 >= argsList.Count)
            {
                Console.Error.WriteLine("Error: Missing --display-id argument.");
                return 1;
            }
            string displayId = argsList[idIdx + 1];
            return await HandleSetTv(host, displayId, isJson, logger);
        case "set-mode":
            int wIdx = argsList.IndexOf("--width");
            int hIdx = argsList.IndexOf("--height");
            int rIdx = argsList.IndexOf("--refresh-rate");
            if (wIdx == -1 || wIdx + 1 >= argsList.Count ||
                hIdx == -1 || hIdx + 1 >= argsList.Count ||
                rIdx == -1 || rIdx + 1 >= argsList.Count)
            {
                Console.Error.WriteLine("Error: Missing --width, --height, or --refresh-rate arguments.");
                return 1;
            }
            if (!int.TryParse(argsList[wIdx + 1], out int width) ||
                !int.TryParse(argsList[hIdx + 1], out int height) ||
                !decimal.TryParse(argsList[rIdx + 1], out decimal refreshRate))
            {
                Console.Error.WriteLine("Error: Invalid numeric values for width, height, or refresh-rate.");
                return 1;
            }
            return await HandleSetMode(host, width, height, refreshRate, isJson, logger);
        case "set-steam":
            int eIdx = argsList.IndexOf("--enabled");
            if (eIdx == -1 || eIdx + 1 >= argsList.Count)
            {
                Console.Error.WriteLine("Error: Missing --enabled argument.");
                return 1;
            }
            if (!bool.TryParse(argsList[eIdx + 1], out bool enabled))
            {
                Console.Error.WriteLine("Error: Invalid boolean value for --enabled (must be true or false).");
                return 1;
            }
            return await HandleSetSteam(host, enabled, isJson, logger);
        case "show":
            return await HandleShow(host, isJson, logger);
        default:
            PrintConfigureUsage();
            return 1;
    }
}

if (isSnapshot)
{
    if (argsList.Count < 2 || argsList[1].StartsWith("-"))
    {
        PrintSnapshotUsage();
        return 1;
    }

    string subCommand = argsList[1].ToLowerInvariant();
    return subCommand switch
    {
        "capture" => await HandleSnapshotCapture(host, isJson, logger),
        "show" => await HandleSnapshotShow(host, isJson, logger),
        _ => PrintSnapshotUsageAndReturnError()
    };
}

return 0;

static void PrintConfigureUsage()
{
    Console.WriteLine("CouchControl CLI Configuration Commands");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  CouchControl.Cli configure list-displays                             List connected displays with stable IDs");
    Console.WriteLine("  CouchControl.Cli configure set-tv --display-id \"<stable-id>\"          Set the couch display (TV)");
    Console.WriteLine("  CouchControl.Cli configure set-mode --width W --height H --refresh-rate R   Set the preferred couch mode");
    Console.WriteLine("  CouchControl.Cli configure set-steam --enabled [true|false]          Enable/disable launching Steam automatically");
    Console.WriteLine("  CouchControl.Cli configure show                                      Show current configuration");
}

static void PrintSnapshotUsage()
{
    Console.WriteLine("CouchControl CLI Snapshot Commands");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  CouchControl.Cli snapshot capture                            Capture and save the current desktop topology");
    Console.WriteLine("  CouchControl.Cli snapshot show [--json]                      Show the saved desktop snapshot");
}

static int PrintSnapshotUsageAndReturnError()
{
    PrintSnapshotUsage();
    return 1;
}

static async Task<int> HandleDisplaysCommand(IHost host, bool isJson, ILogger logger)
{
    try
    {
        var configurationStore = host.Services.GetRequiredService<IAgentConfigurationStore>();
        var configuration = await configurationStore.LoadAsync();

        logger.LogInformation("CouchControl is initialized for agent '{AgentName}'.", configuration.AgentName);

        var displayManager = host.Services.GetRequiredService<IDisplayManager>();
        var displays = await displayManager.GetDisplaysAsync();

        if (isJson)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonOutput = JsonSerializer.Serialize(displays, options);
            Console.WriteLine(jsonOutput);
        }
        else
        {
            Console.WriteLine("Connected displays:");
            Console.WriteLine();

            if (displays.Count == 0)
            {
                Console.WriteLine("No displays found.");
                return 0;
            }

            for (int i = 0; i < displays.Count; i++)
            {
                var d = displays[i];
                Console.WriteLine($"[{i + 1}] {d.FriendlyName}");
                Console.WriteLine($"    Active: {(d.IsActive ? "Yes" : "No")}");
                Console.WriteLine($"    Primary: {(d.IsPrimary ? "Yes" : "No")}");

                if (d.CurrentMode != null)
                {
                    Console.WriteLine($"    Resolution: {d.CurrentMode.Width}x{d.CurrentMode.Height}");
                    Console.WriteLine($"    Refresh rate: {d.CurrentMode.RefreshRateHz:0.##} Hz");
                }
                else
                {
                    Console.WriteLine("    Resolution: N/A");
                    Console.WriteLine("    Refresh rate: N/A");
                }

                Console.WriteLine($"    Device path: {d.DevicePath}");
                Console.WriteLine();
            }
        }

        return 0;
    }
    catch (PlatformNotSupportedException ex)
    {
        logger.LogError(ex, "Platform not supported error: {Message}", ex.Message);
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred: {Message}", ex.Message);
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> HandleListDisplays(IHost host, bool isJson, ILogger logger)
{
    try
    {
        var displayManager = host.Services.GetRequiredService<IDisplayManager>();
        var displays = await displayManager.GetDisplaysAsync();

        if (isJson)
        {
            var serializedList = displays.Select(d => new
            {
                StableId = DisplayStableId.FromDevicePath(d.DevicePath),
                d.FriendlyName,
                d.DevicePath,
                d.IsActive,
                d.IsPrimary,
                CurrentMode = d.CurrentMode != null ? new { d.CurrentMode.Width, d.CurrentMode.Height, d.CurrentMode.RefreshRateHz } : null,
                d.AdapterLuid,
                d.SourceId,
                d.TargetId,
                d.OutputTechnology
            });
            Console.WriteLine(JsonSerializer.Serialize(serializedList, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        Console.WriteLine("Connected displays:");
        Console.WriteLine();
        foreach (var d in displays)
        {
            string stableId = DisplayStableId.FromDevicePath(d.DevicePath);
            Console.WriteLine($"[{stableId}] {d.FriendlyName}");
            Console.WriteLine($"    Active: {(d.IsActive ? "Yes" : "No")}");
            Console.WriteLine($"    Primary: {(d.IsPrimary ? "Yes" : "No")}");
            if (d.CurrentMode != null)
            {
                Console.WriteLine($"    Resolution: {d.CurrentMode.Width}x{d.CurrentMode.Height}");
                Console.WriteLine($"    Refresh rate: {d.CurrentMode.RefreshRateHz:0.##} Hz");
            }
            else
            {
                Console.WriteLine("    Resolution: N/A");
                Console.WriteLine("    Refresh rate: N/A");
            }
            Console.WriteLine($"    Device path: {d.DevicePath}");
            Console.WriteLine();
        }
        return 0;
    }
    catch (PlatformNotSupportedException ex)
    {
        logger.LogError(ex, "Platform not supported error: {Message}", ex.Message);
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to list displays.");
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> HandleSetTv(IHost host, string displayId, bool isJson, ILogger logger)
{
    try
    {
        var displayManager = host.Services.GetRequiredService<IDisplayManager>();
        var displays = await displayManager.GetDisplaysAsync();

        var match = displays.FirstOrDefault(d =>
            string.Equals(DisplayStableId.FromDevicePath(d.DevicePath), displayId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d.DevicePath, displayId, StringComparison.OrdinalIgnoreCase));

        if (match == null)
        {
            Console.Error.WriteLine($"Error: Display with stable ID or path '{displayId}' not found among connected displays.");
            return 1;
        }

        var parsed = DisplayMatchingService.ParseDevicePath(match.DevicePath);
        var identity = new CouchDisplayIdentity(
            DevicePath: match.DevicePath ?? "",
            FriendlyName: match.FriendlyName,
            Manufacturer: parsed?.Manufacturer ?? "",
            ProductCode: parsed?.ProductCode ?? "",
            SerialOrInstance: parsed?.SerialOrInstance ?? "",
            AdapterLuid: match.AdapterLuid ?? "",
            TargetId: match.TargetId ?? 0
        )
        {
            StableId = DisplayStableId.FromDevicePath(match.DevicePath)
        };

        var configStore = host.Services.GetRequiredService<IAgentConfigurationStore>();
        var config = await configStore.LoadAsync();

        var updatedConfig = config with
        {
            CouchDisplayIdentifier = new DisplayIdentifier(match.DevicePath ?? ""),
            CouchDisplayIdentity = identity
        };

        var validation = updatedConfig.Validate();
        if (!validation.Succeeded)
        {
            Console.Error.WriteLine($"Error: Invalid configuration - {validation.Message}");
            return 1;
        }

        await configStore.SaveAsync(updatedConfig);

        if (isJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { Success = true, Message = "TV display configured successfully.", Identity = identity }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"Successfully configured Couch TV display: {match.FriendlyName}");
            Console.WriteLine($"Stable ID: {identity.StableId}");
            Console.WriteLine($"Device Path: {match.DevicePath}");
        }

        return 0;
    }
    catch (PlatformNotSupportedException ex)
    {
        logger.LogError(ex, "Platform not supported error: {Message}", ex.Message);
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to configure TV display.");
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> HandleSetMode(IHost host, int width, int height, decimal refreshRate, bool isJson, ILogger logger)
{
    try
    {
        var configStore = host.Services.GetRequiredService<IAgentConfigurationStore>();
        var config = await configStore.LoadAsync();

        var updatedConfig = config with
        {
            PreferredCouchWidth = width,
            PreferredCouchHeight = height,
            PreferredCouchRefreshRateHz = refreshRate
        };

        var validation = updatedConfig.Validate();
        if (!validation.Succeeded)
        {
            Console.Error.WriteLine($"Error: Invalid configuration - {validation.Message}");
            return 1;
        }

        await configStore.SaveAsync(updatedConfig);

        if (isJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { Success = true, PreferredMode = $"{width}x{height} @ {refreshRate}Hz" }));
        }
        else
        {
            Console.WriteLine($"Successfully configured preferred couch mode: {width}x{height} @ {refreshRate:0.##} Hz");
        }

        return 0;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to configure couch display mode.");
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> HandleSetSteam(IHost host, bool enabled, bool isJson, ILogger logger)
{
    try
    {
        var configStore = host.Services.GetRequiredService<IAgentConfigurationStore>();
        var config = await configStore.LoadAsync();

        var updatedConfig = config with
        {
            LaunchSteamAutomatically = enabled
        };

        var validation = updatedConfig.Validate();
        if (!validation.Succeeded)
        {
            Console.Error.WriteLine($"Error: Invalid configuration - {validation.Message}");
            return 1;
        }

        await configStore.SaveAsync(updatedConfig);

        if (isJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { Success = true, LaunchSteamAutomatically = enabled }));
        }
        else
        {
            Console.WriteLine($"Successfully configured Steam launch automatically: {enabled}");
        }

        return 0;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to configure Steam settings.");
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> HandleShow(IHost host, bool isJson, ILogger logger)
{
    try
    {
        var configStore = host.Services.GetRequiredService<IAgentConfigurationStore>();
        var config = await configStore.LoadAsync();

        if (isJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        Console.WriteLine("CouchControl Current Configuration:");
        Console.WriteLine($"  Agent Name:                  {config.AgentName}");
        Console.WriteLine($"  Schema Version:              {config.SchemaVersion}");
        Console.WriteLine($"  Couch Display Identifier:    {config.CouchDisplayIdentifier?.Value ?? "None"}");
        if (config.CouchDisplayIdentity != null)
        {
            var cid = config.CouchDisplayIdentity;
            Console.WriteLine("  Couch Display Identity:");
            Console.WriteLine($"    Stable ID:                 {cid.StableId}");
            Console.WriteLine($"    Friendly Name:             {cid.FriendlyName}");
            Console.WriteLine($"    Manufacturer:              {cid.Manufacturer}");
            Console.WriteLine($"    Product Code:              {cid.ProductCode}");
            Console.WriteLine($"    Serial/Instance:           {cid.SerialOrInstance}");
            Console.WriteLine($"    Adapter LUID:              {cid.AdapterLuid}");
            Console.WriteLine($"    Target ID:                 {cid.TargetId}");
        }
        else
        {
            Console.WriteLine("  Couch Display Identity:      None configured");
        }
        Console.WriteLine($"  Preferred Couch Mode:        {config.PreferredCouchMode}");
        Console.WriteLine($"  Launch Steam Automatically:  {config.LaunchSteamAutomatically}");
        Console.WriteLine($"  Steam Executable Path:       {config.SteamExecutablePath ?? "Not configured (will search defaults)"}");

        return 0;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to show configuration.");
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> HandleSnapshotCapture(IHost host, bool isJson, ILogger logger)
{
    try
    {
        var displayManager = host.Services.GetRequiredService<IDisplayManager>();
        var snapshotStore = host.Services.GetRequiredService<IDisplaySnapshotStore>();

        var snapshot = await displayManager.CaptureSnapshotAsync();
        await snapshotStore.SaveAsync(snapshot);

        if (isJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        Console.WriteLine($"Saved desktop snapshot {snapshot.SnapshotId} captured at {snapshot.CapturedAtUtc:O}");
        PrintSnapshotSummary(snapshot);
        return 0;
    }
    catch (PlatformNotSupportedException ex)
    {
        logger.LogError(ex, "Platform not supported error: {Message}", ex.Message);
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to capture desktop snapshot.");
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> HandleSnapshotShow(IHost host, bool isJson, ILogger logger)
{
    try
    {
        var snapshotStore = host.Services.GetRequiredService<IDisplaySnapshotStore>();
        var snapshot = await snapshotStore.LoadLastDesktopSnapshotAsync();

        if (snapshot == null)
        {
            Console.Error.WriteLine("Error: No desktop snapshot has been saved yet.");
            return 1;
        }

        if (isJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        Console.WriteLine($"Desktop snapshot {snapshot.SnapshotId}");
        Console.WriteLine($"Captured: {snapshot.CapturedAtUtc:O}");
        PrintSnapshotSummary(snapshot);
        return 0;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to show desktop snapshot.");
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> HandleCouchCommand(IHost host, bool isJson, bool isDryRun, ILogger logger)
{
    try
    {
        var orchestrator = host.Services.GetRequiredService<ProfileOrchestrator>();
        var result = await orchestrator.ActivateCouchModeAsync(isDryRun);

        if (isJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                result.Succeeded,
                Status = result.Status,
                DryRun = isDryRun,
                Display = new
                {
                    result.DisplayResult.Succeeded,
                    result.DisplayResult.IsPartialSuccess,
                    result.DisplayResult.Message,
                    result.DisplayResult.ErrorCode,
                    result.DisplayResult.Outcome,
                    result.DisplayResult.Details,
                    Rollback = result.DisplayResult.RollbackResult is null
                        ? null
                        : new
                        {
                            result.DisplayResult.RollbackResult.Succeeded,
                            result.DisplayResult.RollbackResult.Message,
                            result.DisplayResult.RollbackResult.ErrorCode
                        }
                },
                Steam = result.SteamResult is null
                    ? null
                    : new
                    {
                        result.SteamResult.Succeeded,
                        result.SteamResult.IsPartialSuccess,
                        result.SteamResult.Message,
                        result.SteamResult.ErrorCode,
                        result.SteamResult.Outcome,
                        result.SteamResult.Details
                    }
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            var emittedDetails = new HashSet<string>(StringComparer.Ordinal);

            foreach (var detail in result.DisplayResult.Details)
            {
                if (emittedDetails.Add(detail))
                {
                    Console.WriteLine(detail);
                }
            }

            if (result.SteamResult is not null)
            {
                foreach (var detail in result.SteamResult.Details)
                {
                    if (emittedDetails.Add(detail))
                    {
                        Console.WriteLine(detail);
                    }
                }
            }

            if (!result.Succeeded)
            {
                Console.Error.WriteLine($"Failure: {result.DisplayResult.Message}");
                if (result.DisplayResult.RollbackResult is not null)
                {
                    Console.Error.WriteLine($"Rollback: {result.DisplayResult.RollbackResult.Message}");
                }
            }
            else if (result.Status == ProfileActivationStatus.PartialSuccess)
            {
                Console.WriteLine("Couch Mode completed with partial success");
            }
            else
            {
                Console.WriteLine("Couch Mode ready");
            }
        }

        return result.Succeeded ? 0 : 1;
    }
    catch (PlatformNotSupportedException ex)
    {
        logger.LogError(ex, "Platform not supported error: {Message}", ex.Message);
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (OperationCanceledException ex)
    {
        logger.LogWarning(ex, "Couch mode activation was canceled.");
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to activate couch mode.");
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> HandleSteamCommand(IHost host, bool isJson, ILogger logger)
{
    try
    {
        var configurationStore = host.Services.GetRequiredService<IAgentConfigurationStore>();
        var steamLauncher = host.Services.GetRequiredService<ISteamLauncher>();
        var configuration = await configurationStore.LoadAsync();

        OperationResult result;
        if (!steamLauncher.IsInstalled(configuration))
        {
            result = OperationResult.Failure(
                "Steam installation was not found.",
                "steam_not_installed",
                outcome: "Failure",
                details:
                [
                    "Steam installation not found"
                ]);
        }
        else
        {
            result = await steamLauncher.StartBigPictureAsync(configuration);
        }

        if (isJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                result.Succeeded,
                result.IsPartialSuccess,
                result.Message,
                result.ErrorCode,
                result.Outcome,
                result.Details
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            foreach (var detail in result.Details)
            {
                Console.WriteLine(detail);
            }

            Console.WriteLine(result.Succeeded ? "Steam ready" : $"Failure: {result.Message}");
        }

        return result.Succeeded ? 0 : 1;
    }
    catch (PlatformNotSupportedException ex)
    {
        logger.LogError(ex, "Platform not supported error: {Message}", ex.Message);
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (OperationCanceledException ex)
    {
        logger.LogWarning(ex, "Steam launch was canceled.");
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to launch Steam.");
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> HandleDesktopCommand(IHost host, bool isJson, bool isDryRun, bool isForceFallback, ILogger logger)
{
    try
    {
        var orchestrator = host.Services.GetRequiredService<ProfileOrchestrator>();
        var result = await orchestrator.ActivateDesktopModeAsync(isDryRun, isForceFallback);

        if (isJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                result.Succeeded,
                Status = result.Status,
                result.DisplayResult.Message,
                result.DisplayResult.ErrorCode,
                result.DisplayResult.Outcome,
                DryRun = isDryRun,
                ForceFallback = isForceFallback,
                SnapshotCapturedAtUtc = result.Snapshot?.CapturedAtUtc,
                result.DisplayResult.Details
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            foreach (var detail in result.DisplayResult.Details)
            {
                Console.WriteLine(detail);
            }

            Console.WriteLine(result.DisplayResult.Message);
        }

        return result.Succeeded ? 0 : 1;
    }
    catch (PlatformNotSupportedException ex)
    {
        logger.LogError(ex, "Platform not supported error: {Message}", ex.Message);
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (OperationCanceledException ex)
    {
        logger.LogWarning(ex, "Desktop mode restoration was canceled.");
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to restore desktop mode.");
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static void PrintSnapshotSummary(DisplaySnapshot snapshot)
{
    Console.WriteLine($"Active paths: {snapshot.Paths.Count(path => path.IsActive)} of {snapshot.Paths.Count}");
    Console.WriteLine();

    for (int i = 0; i < snapshot.Paths.Count; i++)
    {
        var path = snapshot.Paths[i];
        Console.WriteLine($"[{i + 1}] {path.Identifier.Value}");
        Console.WriteLine($"    Adapter/Source/Target: {path.AdapterLuid} / {path.SourceId} / {path.TargetId}");
        Console.WriteLine($"    Active: {(path.IsActive ? "Yes" : "No")}");
        Console.WriteLine($"    Primary: {(path.IsPrimary ? "Yes" : "No")}");
        Console.WriteLine($"    Desktop position: {path.SourceDesktopPosition?.ToString() ?? "N/A"}");
        Console.WriteLine($"    Resolution: {(path.Width.HasValue && path.Height.HasValue ? $"{path.Width}x{path.Height}" : "N/A")}");
        Console.WriteLine($"    Pixel format: {path.PixelFormat ?? "N/A"}");
        Console.WriteLine($"    Refresh rate: {path.RefreshRate}");
        Console.WriteLine($"    Rotation: {path.Rotation}");
        Console.WriteLine($"    Scaling: {path.Scaling}");
        Console.WriteLine($"    Output technology: {path.OutputTechnology}");

        if (path.TargetMode != null)
        {
            Console.WriteLine($"    Target mode: {path.TargetMode.ActiveWidth}x{path.TargetMode.ActiveHeight}, {path.TargetMode.RefreshRate}, {path.TargetMode.ScanLineOrdering}");
        }
        else
        {
            Console.WriteLine("    Target mode: N/A");
        }

        Console.WriteLine();
    }
}
