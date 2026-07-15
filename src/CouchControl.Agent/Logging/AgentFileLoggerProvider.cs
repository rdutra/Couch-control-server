using CouchControl.Windows;
using Microsoft.Extensions.Logging;

namespace CouchControl.Agent.Logging;

public interface IAgentLogFileAccessor
{
    string CurrentLogFilePath { get; }
}

public sealed class AgentFileLoggerProvider : ILoggerProvider, IAgentLogFileAccessor
{
    private readonly object gate = new();
    private readonly CouchControlPaths paths;
    private readonly TimeProvider timeProvider;
    private StreamWriter? writer;
    private string? activeLogFilePath;

    public AgentFileLoggerProvider(CouchControlPaths paths, TimeProvider? timeProvider = null)
    {
        this.paths = paths;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string CurrentLogFilePath
    {
        get
        {
            lock (gate)
            {
                return activeLogFilePath ?? EnsureWriter();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new AgentFileLogger(this, categoryName);

    public void Dispose()
    {
        lock (gate)
        {
            writer?.Dispose();
            writer = null;
        }
    }

    private void WriteLine(string categoryName, LogLevel logLevel, EventId eventId, string message, Exception? exception)
    {
        lock (gate)
        {
            EnsureWriter();

            writer!.WriteLine(
                "[{0:O}] {1,-11} {2} {3}",
                timeProvider.GetLocalNow(),
                logLevel,
                categoryName,
                message);

            if (exception is not null)
            {
                writer.WriteLine(exception);
            }
        }
    }

    private string EnsureWriter()
    {
        string logFilePath = paths.GetLogFilePath(timeProvider.GetLocalNow());
        if (writer is not null && string.Equals(activeLogFilePath, logFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return logFilePath;
        }

        Directory.CreateDirectory(paths.LogsDirectory);

        writer?.Dispose();
        writer = new StreamWriter(new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
        activeLogFilePath = logFilePath;
        return logFilePath;
    }

    private sealed class AgentFileLogger(AgentFileLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.WriteLine(categoryName, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}
