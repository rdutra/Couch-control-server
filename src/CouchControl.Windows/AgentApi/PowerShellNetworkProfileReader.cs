using System.Diagnostics;
using System.Text.Json;

namespace CouchControl.Windows.AgentApi;

internal interface INetworkProfileReader
{
    IReadOnlyList<NetworkProfileSnapshot> ReadProfiles();
}

internal sealed class PowerShellNetworkProfileReader : INetworkProfileReader
{
    private readonly ICommandRunner commandRunner;

    public PowerShellNetworkProfileReader()
        : this(new CommandRunner())
    {
    }

    internal PowerShellNetworkProfileReader(ICommandRunner commandRunner)
    {
        this.commandRunner = commandRunner;
    }

    public IReadOnlyList<NetworkProfileSnapshot> ReadProfiles()
    {
        const string script =
            "Get-NetConnectionProfile | Select-Object InterfaceIndex,InterfaceAlias,NetworkCategory | ConvertTo-Json -Compress";

        try
        {
            var result = commandRunner.Run(new CommandSpec("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\""));
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return Array.Empty<NetworkProfileSnapshot>();
            }

            return ParseProfiles(result.StandardOutput);
        }
        catch
        {
            return Array.Empty<NetworkProfileSnapshot>();
        }
    }

    internal static IReadOnlyList<NetworkProfileSnapshot> ParseProfiles(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => document.RootElement.EnumerateArray().Select(ParseProfile).ToArray(),
            JsonValueKind.Object => [ParseProfile(document.RootElement)],
            _ => Array.Empty<NetworkProfileSnapshot>()
        };
    }

    private static NetworkProfileSnapshot ParseProfile(JsonElement element)
    {
        var categoryValue = element.TryGetProperty("NetworkCategory", out var categoryElement)
            ? categoryElement.GetString()
            : null;

        return new NetworkProfileSnapshot(
            element.GetProperty("InterfaceIndex").GetInt32(),
            element.GetProperty("InterfaceAlias").GetString() ?? string.Empty,
            categoryValue?.Equals("Private", StringComparison.OrdinalIgnoreCase) == true
                ? NetworkCategory.Private
                : categoryValue?.Equals("Public", StringComparison.OrdinalIgnoreCase) == true
                    ? NetworkCategory.Public
                    : NetworkCategory.Unknown);
    }
}
