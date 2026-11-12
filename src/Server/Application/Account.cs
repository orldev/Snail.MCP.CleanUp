using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Tools.Prompts;
using Snail.Toolkit.Cleanup.Application;

namespace Snail.MCP.CleanUp.Application;

/// <summary>What the tools ask of the account: what is there, what a cleanup would do, and doing it.</summary>
/// <remarks>
/// A cleanup is two calls on purpose. The plan is decided and shown first, kept under an id, and only that
/// id is carried out — so what is removed is exactly what was shown, never what a second census happens to
/// find a moment later.
/// </remarks>
public sealed class Account(MeasuredCensus census, IHolders sessions, IPlans plans, Cleaner cleaner, ILedger ledger, IDiscoveries discoveries, IRules rules, DiskTrend disk, ServerConfig config, TimeProvider clock)
{
    /// <summary>What is on the machine, grouped by whose it is.</summary>
    public async Task<Answer<JsonObject>> InventoryAsync(CancellationToken cancellationToken)
    {
        var asker = await sessions.CurrentAsync(cancellationToken);

        return await CountedAsync(asker, inventory => ListedAsync(asker, inventory, cancellationToken), cancellationToken);
    }

    /// <summary>The plan for a scope, kept under an id until it is carried out or goes stale.</summary>
    public async Task<Answer<JsonObject>> PlanAsync(Scope scope, CancellationToken cancellationToken)
    {
        var asker = await sessions.CurrentAsync(cancellationToken);

        if (!asker.IsKnown && scope == Scope.Mine)
            return Answer.Failed(Messages.NoSession, Messages.NoSessionHint, Failures.NoSession);

        return await CountedAsync(asker, inventory => PlannedAsync(inventory, scope, cancellationToken), cancellationToken);
    }

    /// <summary>Carries out the removals of a plan that was shown, or only the ones named.</summary>
    public async Task<Answer<JsonObject>> ApplyAsync(string planId, IReadOnlyList<string>? only, CancellationToken cancellationToken)
    {
        var plan = await plans.FindAsync(planId, cancellationToken);

        if (plan is null)
            return Answer.Failed($"there is no plan {planId}", Messages.PlanHint, Failures.NoSuchPlan);

        if (!plan.IsFresh(clock.GetUtcNow()))
            return Answer.Failed($"plan {planId} was made {(clock.GetUtcNow() - plan.MadeAt).TotalMinutes:0} minutes ago and is stale", Messages.PlanHint, Failures.StalePlan);

        var chosen = only is { Count: > 0 } ? plan with { Decisions = [.. plan.Decisions.Where(decision => only.Contains(decision.Resource.Id))] } : plan;
        var releases = await cleaner.ApplyAsync(chosen, cancellationToken);

        return Answer.Ok(Released(releases));
    }

    /// <summary>Releases one resource now, if a plan over everything would remove it.</summary>
    public async Task<Answer<JsonObject>> ReleaseAsync(string id, CancellationToken cancellationToken)
    {
        var asker = await sessions.CurrentAsync(cancellationToken);

        return await CountedAsync(asker, inventory => ReleasedAsync(id, inventory, cancellationToken), cancellationToken);
    }

    /// <summary>Asks for a resource to be left alone, or withdraws that.</summary>
    public async Task<Answer<JsonObject>> KeepAsync(string id, string? reason, bool withdraw, CancellationToken cancellationToken)
    {
        if (withdraw)
            return Answer.Ok(new JsonObject { ["id"] = id, ["isKept"] = false, ["wasKept"] = await ledger.TryUnkeepAsync(id, cancellationToken) });

        if (string.IsNullOrWhiteSpace(reason))
            return Answer.Failed("a resource is kept for a reason, and none was given", Messages.KeepHint, Failures.Arguments);

        await ledger.KeepAsync(id, reason, cancellationToken);

        return Answer.Ok(new JsonObject { ["id"] = id, ["isKept"] = true, ["reason"] = reason });
    }

    /// <summary>Turns what was discovered into a known kind: a rule written to <c>rules.json</c>, and the discoveries it covers settled.</summary>
    public async Task<Answer<JsonObject>> LearnAsync(Rule rule, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rule.Pattern))
            return Answer.Failed("a rule needs a pattern", Messages.LearnHint, Failures.Arguments);

        if (rule.Action != RuleAction.Ignore && rule.Trail != "file")
            return Answer.Failed($"only files can be reported or cached by a rule; {rule.Trail} can only be ignored", Messages.LearnHint, Failures.Arguments);

        await rules.AddAsync(rule, cancellationToken);

        return Answer.Ok(new JsonObject
        {
            ["rule"] = $"{rule.Trail} {rule.Pattern} → {rule.Action.ToString().ToLowerInvariant()}",
            ["settled"] = await discoveries.SettleAsync(rule, cancellationToken)
        });
    }

    /// <summary>The answer made from a census of this moment; a refusal when the sessions of the machine cannot be told, since nothing is then decided.</summary>
    private async Task<Answer<JsonObject>> CountedAsync(Owner asker, Func<Inventory, Task<Answer<JsonObject>>> answered, CancellationToken cancellationToken)
    {
        Inventory inventory;

        try
        {
            inventory = await census.TakeAsync(asker, cancellationToken);
        }
        catch (SessionsUnknownException unknown)
        {
            return Answer.Failed(unknown.Message, Messages.SessionsUnknownHint, Failures.SessionsUnknown);
        }

        return await answered(inventory);
    }

    private async Task<Answer<JsonObject>> ListedAsync(Owner asker, Inventory inventory, CancellationToken cancellationToken)
    {
        var mine = inventory.Resources.Where(resource => resource.Owner == asker && asker.IsKnown).ToList();
        var alive = inventory.Alive;
        var orphaned = inventory.Resources.Where(resource => resource.Owner.IsKnown && resource.Owner != asker && !alive.Contains(resource.Owner)).ToList();
        var others = inventory.Resources.Count(resource => resource.Owner.IsKnown && resource.Owner != asker && alive.Contains(resource.Owner));
        var caches = inventory.Resources.Where(resource => resource.IsShared).OrderByDescending(resource => resource.Bytes ?? 0).ToList();

        var shown = new JsonObject
        {
            ["session"] = asker.ToString(),
            ["mine"] = Views.Listed(mine, Views.Of),
            ["orphaned"] = Views.Listed(orphaned, Views.Of),
            ["heldByOtherRunningSessions"] = others,
            ["caches"] = Views.Listed(caches, Views.Of),
            ["unowned"] = inventory.Resources.Count(resource => !resource.Owner.IsKnown && !resource.IsShared),
            ["discoveries"] = Views.Listed([.. inventory.Discoveries.OrderByDescending(discovery => discovery.IsPresent)], Views.Of),
            ["disk"] = await disk.ReportAsync(cancellationToken),
            ["notes"] = new JsonObject([.. inventory.Notes.Select(note => KeyValuePair.Create(note.Key, (JsonNode?)note.Value))])
        };

        if (Warned(inventory) is { } warnings)
            shown.Insert(1, "warnings", warnings);

        return Answer.Ok(shown);
    }

    private async Task<Answer<JsonObject>> PlannedAsync(Inventory inventory, Scope scope, CancellationToken cancellationToken)
    {
        var plan = Planner.Decide(inventory, scope, Guid.NewGuid().ToString("n")[..10], MachineKinds.Order);

        await plans.SaveAsync(plan, cancellationToken);

        var shown = Shown(plan);

        if (Warned(inventory) is { } warnings)
            shown.Insert(0, "warnings", warnings);

        return Answer.Ok(shown);
    }

    private async Task<Answer<JsonObject>> ReleasedAsync(string id, Inventory inventory, CancellationToken cancellationToken)
    {
        var plan = Planner.Decide(inventory, Scope.Everything, "release", MachineKinds.Order);
        var decision = plan.Decisions.FirstOrDefault(candidate => candidate.Resource.Id == id);

        if (decision is null)
            return Answer.Failed($"there is no {id} on this machine now", Messages.ReleaseHint, Failures.Absent);

        if (decision.Verdict != Verdict.Remove)
            return Answer.Failed($"{id} is not released: {decision.Reason}", Messages.ReleaseHint, Failures.NotReleased);

        return Answer.Ok(Released(await cleaner.ApplyAsync(plan with { Decisions = [decision] }, cancellationToken)));
    }

    /// <summary>The warnings of an inventory as an answer shows them; null when there are none, so that the field is left out.</summary>
    private JsonArray? Warned(Inventory inventory)
    {
        var warnings = Warnings.Of(inventory, Warnings.Bytes(config.ScratchpadWarningGigabytes));

        return warnings.Count == 0 ? null : new JsonArray([.. warnings.Select(warning => (JsonNode)warning)]);
    }

    private static JsonObject Shown(Plan plan) => new()
    {
        ["planId"] = plan.Id,
        ["scope"] = plan.Scope.ToString().ToLowerInvariant(),
        ["expiresAt"] = (plan.MadeAt + Plan.Lifetime).ToString("u", System.Globalization.CultureInfo.InvariantCulture),
        ["frees"] = Views.Size(plan.Removals.Sum(removal => removal.Resource.Bytes ?? 0), plan.Removals.Any(removal => removal.Resource.IsAtLeast)),
        ["byKind"] = ByKind(plan.Removals),
        ["remove"] = Views.Listed(plan.Removals, Views.Of),
        ["report"] = Views.Listed([.. plan.Decisions.Where(decision => decision.Verdict == Verdict.Report)], Views.Of),
        ["keep"] = Views.Listed([.. plan.Decisions.Where(decision => decision.Verdict == Verdict.Keep)], Views.Of),
        ["discoveries"] = Views.Listed(plan.Discoveries, Views.Of)
    };

    /// <summary>The removals of a plan counted by kind, largest first, the ones past the list's end included.</summary>
    /// <remarks>
    /// A plan lists twenty removals and says how many it left out. On 2026-11-02 the twenty shown were transcripts, and an
    /// agent took the 99 left out for more of the same: eleven scratchpads among them went to the Trash unnamed.
    /// </remarks>
    private static JsonArray ByKind(IReadOnlyList<Decision> removals)
    {
        var kinds = removals
            .GroupBy(removal => removal.Resource.Kind, StringComparer.Ordinal)
            .Select(kind => (Name: kind.Key, Count: kind.Count(), Bytes: kind.Sum(removal => removal.Resource.Bytes ?? 0), IsAtLeast: kind.Any(removal => removal.Resource.IsAtLeast)))
            .OrderByDescending(kind => kind.Bytes);

        return new JsonArray([.. kinds.Select(kind => (JsonNode)Views.Compact(new JsonObject
        {
            ["kind"] = kind.Name,
            ["count"] = kind.Count,
            ["size"] = Views.Size(kind.Bytes, kind.IsAtLeast)
        }))]);
    }

    /// <summary>What a cleanup released, the space it gave back apart from what went to the Trash.</summary>
    /// <remarks>
    /// The Trash is on the same volume, so what goes there gives nothing back until somebody empties it, and an agent
    /// cannot: on 2026-11-06 a cleanup said it freed 14.76 GB while 7.1 GB of it lay in the Trash and the disk had 9.6 GB.
    /// </remarks>
    private static JsonObject Released(IReadOnlyList<Release> releases)
    {
        var released = releases.Where(release => release.Outcome == Outcome.Released).ToList();
        var trashed = released.Where(Disposal.IsTrashed).ToList();
        var freed = released.Where(release => !Disposal.IsTrashed(release)).ToList();

        var answer = Views.Compact(new JsonObject
        {
            ["freed"] = Views.Size(freed.Sum(release => release.Bytes ?? 0), freed.Exists(release => release.IsAtLeast)),
            ["trashed"] = trashed.Count == 0 ? null : Views.Size(trashed.Sum(release => release.Bytes ?? 0), trashed.Exists(release => release.IsAtLeast)),
            ["note"] = trashed.Count == 0 ? null : Messages.TrashedNote,
            ["released"] = released.Count,
            ["results"] = new JsonArray([.. releases.Select(release => (JsonNode)new JsonObject
            {
                ["id"] = release.ResourceId,
                ["outcome"] = release.Outcome.ToString().ToLowerInvariant(),
                ["detail"] = release.Detail
            })])
        });

        return answer;
    }
}
