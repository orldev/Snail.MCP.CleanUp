namespace Snail.MCP.CleanUp.Extensions;

/// <summary>Log lines written straight to stderr, one line each, from information up.</summary>
/// <remarks>
/// stdout carries the JSON-RPC conversation, so a log line there breaks the protocol. The console logger of
/// Microsoft.Extensions.Logging writes to stderr just as well, but brings options bound from configuration,
/// formatters and a thread of its own to queue lines on — for a server that logs a handful of lines in a day.
/// </remarks>
public sealed class StderrLogging : ILoggerProvider
{
    private static readonly Lock Writing = new();

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new Line(categoryName);

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    private sealed class Line(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var text = $"{Level(logLevel)}: {category}[{eventId.Id}] {formatter(state, exception)}{(exception is null ? string.Empty : $"{Environment.NewLine}{exception}")}";

            lock (Writing)
                Console.Error.WriteLine(text);
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            _ => "crit"
        };
    }
}
