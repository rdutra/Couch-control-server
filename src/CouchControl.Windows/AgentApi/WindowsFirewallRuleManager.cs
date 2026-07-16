using System.ComponentModel;
using System.Text.Json;

namespace CouchControl.Windows.AgentApi;

public interface IWindowsFirewallRuleManager
{
    FirewallRuleStatus GetStatus(int port);

    FirewallRuleChangeResult EnsureRule(int port);

    FirewallRuleChangeResult RemoveRule(int port);

    FirewallRuleChangeResult RecreateRule(int port);
}

public sealed class WindowsFirewallRuleManager : IWindowsFirewallRuleManager
{
    internal const string RuleName = "CouchControl Agent API (Private TCP)";

    private readonly ICommandRunner commandRunner;

    public WindowsFirewallRuleManager()
        : this(new CommandRunner())
    {
    }

    internal WindowsFirewallRuleManager(ICommandRunner commandRunner)
    {
        this.commandRunner = commandRunner;
    }

    public FirewallRuleStatus GetStatus(int port)
    {
        var result = commandRunner.Run(FirewallCommandBuilder.BuildQueryCommand(RuleName));
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return new FirewallRuleStatus(false, false, "Missing", true, RuleName);
        }

        var entries = FirewallCommandBuilder.ParseFirewallRules(result.StandardOutput);
        if (entries.Count == 0)
        {
            return new FirewallRuleStatus(false, false, "Missing", true, RuleName);
        }

        var matches = entries.Any(entry =>
            entry.Enabled &&
            string.Equals(entry.Direction, "Inbound", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.Action, "Allow", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.Profile, "Private", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.Protocol, "TCP", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.LocalPort, port.ToString(), StringComparison.OrdinalIgnoreCase));

        return new FirewallRuleStatus(
            true,
            matches,
            matches ? "Present (private TCP rule)" : "Present but needs repair",
            true,
            RuleName);
    }

    public FirewallRuleChangeResult EnsureRule(int port)
    {
        var status = GetStatus(port);
        if (status.MatchesExpectedConfiguration)
        {
            return new FirewallRuleChangeResult(true, "Firewall rule already matches the expected private TCP configuration.", true, false);
        }

        return status.Exists ? RecreateRule(port) : RunElevated(FirewallCommandBuilder.BuildCreateCommand(RuleName, port), "Firewall rule created.");
    }

    public FirewallRuleChangeResult RemoveRule(int port)
    {
        var status = GetStatus(port);
        if (!status.Exists)
        {
            return new FirewallRuleChangeResult(true, "Firewall rule is already absent.", true, false);
        }

        return RunElevated(FirewallCommandBuilder.BuildRemoveCommand(RuleName), "Firewall rule removed.");
    }

    public FirewallRuleChangeResult RecreateRule(int port)
    {
        var removeResult = RunElevated(FirewallCommandBuilder.BuildRemoveCommand(RuleName), null, allowMissingRule: true);
        if (!removeResult.Succeeded)
        {
            return removeResult;
        }

        return RunElevated(FirewallCommandBuilder.BuildCreateCommand(RuleName, port), "Firewall rule recreated.");
    }

    private FirewallRuleChangeResult RunElevated(CommandSpec command, string? successMessage, bool allowMissingRule = false)
    {
        try
        {
            var result = commandRunner.Run(command);
            if (result.ExitCode == 0 || allowMissingRule)
            {
                return new FirewallRuleChangeResult(true, successMessage ?? "Firewall command completed.", true, false);
            }

            return new FirewallRuleChangeResult(false, string.IsNullOrWhiteSpace(result.StandardError) ? "Firewall command failed." : result.StandardError.Trim(), true, false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new FirewallRuleChangeResult(false, "Firewall update was cancelled at the elevation prompt.", true, true);
        }
    }
}

internal sealed record FirewallRuleInfo(
    bool Enabled,
    string Direction,
    string Action,
    string Profile,
    string Protocol,
    string LocalPort);

internal static class FirewallCommandBuilder
{
    internal static CommandSpec BuildQueryCommand(string ruleName)
    {
        var script =
            $"$rule = Get-NetFirewallRule -DisplayName '{ruleName.Replace("'", "''")}' -ErrorAction SilentlyContinue; " +
            "if ($null -eq $rule) { return }; " +
            "$rule | ForEach-Object { " +
            "$port = $_ | Get-NetFirewallPortFilter; " +
            "[PSCustomObject]@{ Enabled = ($_.Enabled -eq 'True'); Direction = $_.Direction.ToString(); Action = $_.Action.ToString(); Profile = $_.Profile.ToString(); Protocol = $port.Protocol.ToString(); LocalPort = $port.LocalPort.ToString() } " +
            "} | ConvertTo-Json -Compress";

        return new CommandSpec("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\"");
    }

    internal static CommandSpec BuildCreateCommand(string ruleName, int port)
    {
        var script =
            $"New-NetFirewallRule -DisplayName '{ruleName.Replace("'", "''")}' -Direction Inbound -Action Allow -Protocol TCP -LocalPort {port} -Profile Private";
        return new CommandSpec("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\"", Elevate: true);
    }

    internal static CommandSpec BuildRemoveCommand(string ruleName)
    {
        var script =
            $"Remove-NetFirewallRule -DisplayName '{ruleName.Replace("'", "''")}' -ErrorAction SilentlyContinue";
        return new CommandSpec("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\"", Elevate: true);
    }

    internal static IReadOnlyList<FirewallRuleInfo> ParseFirewallRules(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => document.RootElement.EnumerateArray().Select(ParseRule).ToArray(),
            JsonValueKind.Object => [ParseRule(document.RootElement)],
            _ => Array.Empty<FirewallRuleInfo>()
        };
    }

    private static FirewallRuleInfo ParseRule(JsonElement element) =>
        new(
            element.GetProperty("Enabled").GetBoolean(),
            element.GetProperty("Direction").GetString() ?? string.Empty,
            element.GetProperty("Action").GetString() ?? string.Empty,
            element.GetProperty("Profile").GetString() ?? string.Empty,
            element.GetProperty("Protocol").GetString() ?? string.Empty,
            element.GetProperty("LocalPort").GetString() ?? string.Empty);
}
