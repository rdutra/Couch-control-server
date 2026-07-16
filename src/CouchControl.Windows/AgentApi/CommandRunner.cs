using System.Diagnostics;
using System.Text;

namespace CouchControl.Windows.AgentApi;

internal sealed record CommandSpec(string FileName, string Arguments, bool Elevate = false);

internal sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

internal interface ICommandRunner
{
    CommandResult Run(CommandSpec command);
}

internal sealed class CommandRunner : ICommandRunner
{
    public CommandResult Run(CommandSpec command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            Arguments = command.Arguments,
            UseShellExecute = command.Elevate,
            CreateNoWindow = !command.Elevate,
            Verb = command.Elevate ? "runas" : string.Empty,
            RedirectStandardOutput = !command.Elevate,
            RedirectStandardError = !command.Elevate
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start '{command.FileName}'.");
        if (command.Elevate)
        {
            process.WaitForExit();
            return new CommandResult(process.ExitCode, string.Empty, string.Empty);
        }

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        outputBuilder.Append(process.StandardOutput.ReadToEnd());
        errorBuilder.Append(process.StandardError.ReadToEnd());
        process.WaitForExit();
        return new CommandResult(process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
    }
}
