using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Configuration;
using Snail.Toolkit.Cleanup.Application;

namespace Snail.MCP.CleanUp.Application;

/// <summary>What the server clears on its own, without being asked: what sessions over for an hour left, the remains of ended processes, Docker's build cache once it is large.</summary>
/// <remarks>
/// <para>
/// A cleanup is shown before it is carried out, and an agent or a person asks for it. The one sweep that came by itself,
/// at a session's end, found the session's scratchpad written moments before and left it as one the session might still be
/// writing into — and nothing ever came back for it. In twelve days that left 152 scratchpads of 4.4 GB, 29 GB of Docker's
/// build cache and 11 001 endpoints of ended processes for somebody to ask about (2026-10-24).
/// </para>
/// <para>
/// So the server holding the watch clears, after its census and at most every <see cref="ServerConfig.SweepMinutes"/>, what
/// a plan over everything would remove and nobody needs to see first: what a session left once the session has been over
/// for <see cref="Grace"/>, a scratchpad only once nothing was written in it for long enough that it is erased, the
/// remains of processes that ended, and Docker's build cache past <see cref="ServerConfig.DockerCacheSweepGigabytes"/>.
/// What the asker holds, what a running session holds and every other cache are left to a plan somebody asks for.
/// </para>
/// </remarks>
public sealed class Upkeep(Cleaner cleaner, SweepLog log, ServerConfig config, TimeProvider clock)
{
    /// <summary>How long a session must have been over before what it left is cleared without asking: one resumed within it finds its things.</summary>
    public static TimeSpan Grace { get; } = TimeSpan.FromHours(1);

    private DateTimeOffset? _last;

    /// <summary>Whether an upkeep is due: once every <see cref="ServerConfig.SweepMinutes"/>, never when they are zero.</summary>
    public bool IsDue => config.SweepMinutes > 0 && (_last is not { } last || clock.GetUtcNow() - last >= TimeSpan.FromMinutes(config.SweepMinutes));

    /// <summary>Clears what the inventory proves may go without asking, writes what it did to the sweep log, and answers what was released.</summary>
    public async Task<IReadOnlyList<Release>> SweepAsync(Inventory inventory, CancellationToken cancellationToken)
    {
        _last = clock.GetUtcNow();

        var plan = Planner.Decide(inventory, Scope.Everything, $"upkeep-{Guid.NewGuid():n}"[..17], MachineKinds.Order);
        var due = Due(plan, inventory);

        if (due.Removals.Count == 0)
            return [];

        var releases = await cleaner.ApplyAsync(due, cancellationToken);

        await log.WriteAsync(SweepLog.Of("upkeep", due, releases, withWhatWasLeft: false), cancellationToken);

        return releases;
    }

    /// <summary>The plan with only the removals that may go without asking.</summary>
    internal Plan Due(Plan plan, Inventory inventory)
    {
        var now = clock.GetUtcNow();
        var alive = inventory.Alive;
        var ended = inventory.Holders.GroupBy(holder => holder.Owner).ToDictionary(group => group.Key, group => group.First().EndedAt);

        return plan with { Decisions = [.. plan.Decisions.Where(decision => CanGo(decision, inventory.Asker, alive, ended, now))] };
    }

    private bool CanGo(Decision decision, Owner asker, IReadOnlySet<Owner> alive, Dictionary<Owner, DateTimeOffset?> ended, DateTimeOffset now)
    {
        if (decision.Verdict != Verdict.Remove)
            return false;

        var resource = decision.Resource;

        if (resource.IsShared)
            return IsCacheToClear(resource);

        if (!resource.Owner.IsKnown || resource.Owner == asker || alive.Contains(resource.Owner))
            return false;

        if (resource.Kind == Kinds.File)
            return resource.IsRegenerable;

        return !ended.TryGetValue(resource.Owner, out var endedAt) || endedAt is { } at && now - at >= Grace;
    }

    /// <summary>Whether a cache is one the server clears by itself: the remains of ended processes, and Docker's build cache once it is large.</summary>
    private bool IsCacheToClear(Resource resource) => resource.Kind switch
    {
        MachineKinds.Remains => true,
        MachineKinds.BuildCache => resource.Bytes >= Warnings.Bytes(config.DockerCacheSweepGigabytes),
        _ => false
    };
}
