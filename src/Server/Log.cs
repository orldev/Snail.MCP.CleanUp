namespace Snail.MCP.CleanUp;

/// <summary>What the server says about its watch, its hooks and the programs it starts.</summary>
/// <remarks>
/// Generated rather than written through the logging extensions: a debug line costs nothing while Debug is off, where
/// the extensions boxed and formatted their arguments first. The messages are the ones the server has always written.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "the watch could not be handed back; it passes on when it runs out")]
    internal static partial void WatchNotHandedBack(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "the watch could not be asked for; this server takes the census itself")]
    internal static partial void WatchNotAskedFor(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "census for {Asker}: {Resources} resources, {Orphans} left by ended sessions")]
    internal static partial void CensusTaken(ILogger logger, Owner asker, int resources, int orphans);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "census failed; the next one is in {Interval}")]
    internal static partial void CensusFailed(ILogger logger, Exception failure, TimeSpan interval);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "could not start a detached {Verb}")]
    internal static partial void DetachedNotStarted(ILogger logger, string verb);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "hooks are not taken on {Socket}: {Reason}")]
    internal static partial void HooksNotTaken(ILogger logger, string socket, string reason);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "taking hooks on {Socket}")]
    internal static partial void TakingHooks(ILogger logger, string socket);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "a hook handed over could not be handled")]
    internal static partial void HookNotHandled(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 9, Level = LogLevel.Debug, Message = "the hook left before its answer")]
    internal static partial void HookLeft(ILogger logger, Exception gone);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "the census a stop asked for failed")]
    internal static partial void StopCensusFailed(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 11, Level = LogLevel.Debug, Message = "the hook socket could not be removed")]
    internal static partial void HookSocketNotRemoved(ILogger logger, Exception refused);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "upkeep released {Released} of {Attempted}")]
    internal static partial void UpkeepDone(ILogger logger, int released, int attempted);

    [LoggerMessage(EventId = 19, Level = LogLevel.Information, Message = "the ledger forgot {Plans} plan(s) past keeping and gave back {Megabytes} MB")]
    internal static partial void LedgerTidied(ILogger logger, int plans, long megabytes);

    [LoggerMessage(EventId = 13, Level = LogLevel.Warning, Message = "upkeep failed; the next one comes with a later census")]
    internal static partial void UpkeepFailed(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 14, Level = LogLevel.Warning, Message = "{Tool} took {Elapsed}")]
    internal static partial void SlowCall(ILogger logger, string tool, TimeSpan elapsed);

    [LoggerMessage(EventId = 15, Level = LogLevel.Warning, Message = "{Tool} was answered as overdue after {Limit}; it goes on in the background")]
    internal static partial void CallOverdue(ILogger logger, string tool, TimeSpan limit);

    [LoggerMessage(EventId = 16, Level = LogLevel.Warning, Message = "census took {Elapsed}; slowest: {Slowest}")]
    internal static partial void SlowCensus(ILogger logger, TimeSpan elapsed, string slowest);

    [LoggerMessage(EventId = 18, Level = LogLevel.Warning, Message = "the thread pool took {Waited} to start a piece of work (gave up waiting: {IsStuck}); {Threads} threads, {Pending} pieces of work waiting")]
    internal static partial void PoolSlow(ILogger logger, TimeSpan waited, bool isStuck, int threads, long pending);

    [LoggerMessage(EventId = 17, Level = LogLevel.Warning, Message = "telling the session took {Elapsed}: the process table {Table}, the sessions {Sessions}")]
    internal static partial void SlowSession(ILogger logger, TimeSpan elapsed, TimeSpan table, TimeSpan sessions);
}
