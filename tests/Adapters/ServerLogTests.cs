using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Application;

namespace Snail.MCP.CleanUp.Tests.Adapters;

/// <summary>A cleanup_plan was silent for 31 minutes on 2026-11-09, and nothing the server said about it was kept: the client keeps only what a server writes while it starts.</summary>
public sealed class ServerLogTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 9, 13, 23, 53, TimeSpan.Zero));

    public void Dispose() => _scratch.Dispose();

    private string LogPath => _scratch.File("data/server.log");

    [Fact]
    public void Warning_IsWrittenWithItsProcessAndCategory_AndInformationIsNot()
    {
        using var log = new ServerLog(LogPath, _clock);
        var logger = log.CreateLogger("Snail.MCP.CleanUp.Tools.Patience");

        Say(logger, LogLevel.Warning, "cleanup_plan was answered as overdue");
        Say(logger, LogLevel.Information, "a census was taken");

        var line = Assert.Single(File.ReadAllLines(LogPath));
        Assert.Contains($" {Environment.ProcessId} Warning Snail.MCP.CleanUp.Tools.Patience: cleanup_plan was answered as overdue", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_PastItsSize_IsMovedAsideAndBegunAgain()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        File.WriteAllText(LogPath, new string('x', (int)ServerLog.MostBytes + 1));
        using var log = new ServerLog(LogPath, _clock);

        Say(log.CreateLogger("test"), LogLevel.Warning, "after the move");

        Assert.True(File.Exists($"{LogPath}.1"));
        Assert.Single(File.ReadAllLines(LogPath));
    }

    /// <summary>A log is never the reason a tool fails.</summary>
    [Fact]
    public void Line_ThatCannotBeWritten_IsDropped()
    {
        var blocked = _scratch.File("blocked");
        File.WriteAllText(blocked, "a file where the folder should be");
        using var log = new ServerLog(Path.Combine(blocked, "server.log"), _clock);

        var writing = Record.Exception(() => Say(log.CreateLogger("test"), LogLevel.Error, "nowhere to go"));

        Assert.Null(writing);
    }

    [Fact]
    public void Probe_OfAPoolWithThreadsToSpare_AnswersAtOnce()
    {
        var waited = PoolWatch.Probe(TimeSpan.FromSeconds(5));

        Assert.NotNull(waited);
        Assert.True(waited < PoolWatch.Slow);
    }

    private static void Say(ILogger logger, LogLevel level, string message) => logger.Log(level, default, message, null, static (said, _) => said);
}
