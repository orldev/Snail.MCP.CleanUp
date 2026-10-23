using Microsoft.Extensions.Hosting;

namespace Snail.MCP.CleanUp.Application;

/// <summary>Takes stock when the server starts, then whenever the machine says something changed, while it holds the machine's watch.</summary>
/// <remarks>
/// <para>
/// The first census is what finds orphans: a session that died left its servers and simulators behind, and
/// the next session to start is the first one able to notice. The ones after it are what make ancestry
/// worth anything — a process started under the agent and handed to launchd a minute later is only known
/// to be the agent's because a census saw it while it was still a child.
/// </para>
/// <para>
/// A census sees the whole machine — every session's programs, every session's baseline — and writes what it learns
/// to the ledger they share, so the census of any one server does for all of them. Every session starts a server,
/// and five of them took five identical censuses every half a minute; now the server holding the watch takes the
/// timed census, and the others take only their first. A watch not renewed for two intervals passes to the next.
/// </para>
/// <para>
/// The census is taken when the machine says something changed (<see cref="IChanges"/>): a watched directory gained or
/// lost an entry, an agent started a command or ended. An idle machine took a census every half a minute to find
/// nothing; a busy one is now counted within seconds of the change, while the program an agent left is still its
/// child. A census waits for a burst of changes to settle, comes at most every <see cref="Quiet"/>, and every
/// <see cref="Fallback"/> regardless, for what the kernel is not asked about.
/// </para>
/// </remarks>
public sealed class Watch(MeasuredCensus census, IHolders sessions, IShifts shifts, IChanges changes, Upkeep upkeep, ILogger<Watch> logger, TimeProvider clock) : BackgroundService
{
    /// <summary>How long after a change the census waits, so that a burst of changes is counted once.</summary>
    public static TimeSpan Settle { get; } = TimeSpan.FromSeconds(2);

    /// <summary>The least time between two censuses taken on word of a change.</summary>
    public static TimeSpan Quiet { get; } = TimeSpan.FromSeconds(10);

    /// <summary>The longest the watch goes without a census when nothing is said to have changed.</summary>
    public static TimeSpan Fallback { get; } = TimeSpan.FromMinutes(5);

    /// <summary>The duty the timed census is held as.</summary>
    public const string Duty = "census";

    private readonly string _watcher = $"{Environment.ProcessId}-{Guid.NewGuid():n}";

    /// <summary>How long the watch waits for word of a change before it asks again whether it still holds the watch, which it holds for two of these.</summary>
    /// <remarks>Not how often a census is taken: that is on word of a change, at most every <see cref="Quiet"/>, and every <see cref="Fallback"/> regardless.</remarks>
    public static TimeSpan Interval { get; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a stop waits for a census under way before it leaves the census to the exit.</summary>
    /// <remarks>
    /// Claude Code gives a server 100 ms after SIGINT and 400 ms after SIGTERM before it kills it. A stop that waited for
    /// the census under way to finish took 70 ms at the median and up to 384 ms, and 33 of 289 servers were killed
    /// (2026-10-23). A census only reads, and what it writes the ledger writes whole or not at all, so it is left to end
    /// with the process.
    /// </remarks>
    public static TimeSpan Letting { get; } = TimeSpan.FromMilliseconds(20);

    /// <summary>How long a server lives before its first census of its own.</summary>
    /// <remarks>
    /// Half the servers a client starts are closed within five seconds — 41 % within two, of 290 connections in the logs of
    /// 2026-10-11 to 10-23 — and each took a census of hundreds of milliseconds and 40 to 84 MB for nobody, and was closed in
    /// the middle of it: the census's threads, deep in the file system, held the process past the client's 100 ms. A tool
    /// call or a hook in those seconds takes the census it needs; the watch's own begins after them.
    /// </remarks>
    public static TimeSpan Warmup { get; } = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    /// <remarks>It waits first, which also leaves the host's start, which the transport waits for, to the transport.</remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(Warmup, clock, stoppingToken);

        var agents = await TakeAsync(await HoldsTheWatchAsync(stoppingToken), stoppingToken);
        var last = clock.GetUtcNow();

        while (!stoppingToken.IsCancellationRequested)
        {
            await changes.WaitAsync(Interval, stoppingToken);

            if (!await HoldsTheWatchAsync(stoppingToken))
            {
                changes.Leave();
                changes.TryTake();

                continue;
            }

            changes.Follow(agents);

            var isChanged = changes.TryTake();
            var since = clock.GetUtcNow() - last;

            if (!isChanged && since < Fallback)
                continue;

            if (isChanged)
                await Task.Delay(Settle > Quiet - since ? Settle : Quiet - since, clock, stoppingToken);

            changes.TryTake();
            agents = await TakeAsync(isWatching: true, stoppingToken);
            last = clock.GetUtcNow();
            changes.Follow(agents);
        }
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var stopped = base.StopAsync(cancellationToken);

        await Task.WhenAny(stopped, Task.Delay(Letting, clock, cancellationToken));

        try
        {
            await shifts.LeaveAsync(Duty, _watcher, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            Log.WatchNotHandedBack(logger, failure);
        }
    }

    private async Task<bool> HoldsTheWatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await shifts.TryTakeAsync(Duty, _watcher, Interval * 2, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            Log.WatchNotAskedFor(logger, failure);

            return true;
        }
    }

    /// <summary>Takes a census, and the upkeep when this server holds the watch and one is due; the processes of the agents it found running, for the kernel to follow.</summary>
    private async Task<IReadOnlyList<int>> TakeAsync(bool isWatching, CancellationToken cancellationToken)
    {
        try
        {
            var asker = await sessions.CurrentAsync(cancellationToken);
            var inventory = await census.TakeAsync(asker, cancellationToken);
            var alive = inventory.Alive;
            var orphans = inventory.Resources.Count(resource => resource.Owner.IsKnown && resource.Owner != asker && !alive.Contains(resource.Owner));

            Log.CensusTaken(logger, asker, inventory.Resources.Count, orphans);

            if (isWatching && upkeep.IsDue)
                await KeepUpAsync(inventory, cancellationToken);

            return [.. inventory.Holders.OfType<Session>().Where(session => session.IsAlive && session.Pid is > 0).Select(session => session.Pid!.Value)];
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            Log.CensusFailed(logger, failure, Interval);

            return [];
        }
    }

    /// <summary>Clears what may go without asking; a failure of it is no failure of the census.</summary>
    private async Task KeepUpAsync(Inventory inventory, CancellationToken cancellationToken)
    {
        try
        {
            var releases = await upkeep.SweepAsync(inventory, cancellationToken);

            if (releases.Count > 0)
                Log.UpkeepDone(logger, releases.Count(release => release.Outcome == Outcome.Released), releases.Count);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            Log.UpkeepFailed(logger, failure);
        }
    }
}
