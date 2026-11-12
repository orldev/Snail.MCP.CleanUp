using System.Globalization;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>The server's warnings in a file of its data folder, <c>server.log</c>, one line each, with the process that wrote it.</summary>
/// <remarks>
/// A stdio server's stderr is the client's to keep, and Claude Code keeps only what a server writes while it starts: on
/// 2026-11-09 a cleanup_plan stayed silent for 31 minutes and nothing the server said about it was anywhere — not the
/// call it held as overdue, not the census it waited for. Every session runs a server of its own and all of them share
/// the data folder, so a line names its process. The file is opened for each line, which a warning can afford, and
/// moved aside to <c>server.log.1</c> past <see cref="MostBytes"/>; a line that cannot be written is dropped, since
/// a log must never be the reason a tool fails.
/// </remarks>
public sealed class ServerLog(string path, TimeProvider clock) : ILoggerProvider
{
    /// <summary>How large the log grows before it is moved aside and begun again.</summary>
    public const long MostBytes = 1_048_576;

    private readonly Lock _writing = new();

    /// <summary>The level from which lines are written.</summary>
    public static LogLevel Least => LogLevel.Warning;

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new Writer(this, categoryName);

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    private void Write(string category, LogLevel level, string message, Exception? failure)
    {
        var at = clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var line = $"{at} {Environment.ProcessId} {level} {category}: {message}{(failure is null ? string.Empty : $"{Environment.NewLine}{failure}")}{Environment.NewLine}";

        lock (_writing)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                if (File.Exists(path) && new FileInfo(path).Length > MostBytes)
                    File.Move(path, $"{path}.1", overwrite: true);

                File.AppendAllText(path, line);
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    private sealed class Writer(ServerLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= Least && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                log.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
