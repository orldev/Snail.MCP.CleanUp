using Microsoft.Extensions.Hosting;
using Snail.MCP.CleanUp.Core.Application;

namespace Snail.MCP.CleanUp.Application;

/// <summary>Takes stock when the server starts, and again every half a minute while its session runs.</summary>
/// <remarks>
/// The first census is what finds orphans: a session that died left its servers and simulators behind, and
/// the next session to start is the first one able to notice. The ones after it are what make ancestry
/// worth anything — a process started under the agent and handed to launchd a minute later is only known
/// to be the agent's because a census saw it while it was still a child.
/// </remarks>
public sealed class Watch(Census census, ISessions sessions, ILogger<Watch> logger, TimeProvider clock) : BackgroundService
{
    /// <summary>How often a census is taken while the session runs.</summary>
    public static TimeSpan Interval { get; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);

        do
        {
            await TakeAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TakeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var asker = await sessions.CurrentAsync(cancellationToken);
            var inventory = await census.TakeAsync(asker, cancellationToken);
            var orphans = inventory.Resources.Count(resource => resource.Owner.IsKnown && resource.Owner != asker && !inventory.Alive.Contains(resource.Owner));

            logger.LogDebug("census for {Asker}: {Resources} resources, {Orphans} left by ended sessions", asker, inventory.Resources.Count, orphans);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogWarning(failure, "census failed; the next one is in {Interval}", Interval);
        }
    }
}
