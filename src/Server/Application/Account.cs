using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Tools.Prompts;

namespace Snail.MCP.CleanUp.Application;

/// <summary>What the tools ask of the account: what is there, what a cleanup would do, and doing it.</summary>
/// <remarks>
/// A cleanup is two calls on purpose. The plan is decided and shown first, kept under an id, and only that
/// id is carried out — so what is removed is exactly what was shown, never what a second census happens to
/// find a moment later.
/// </remarks>
public sealed class Account(MeasuredCensus census, ISessions sessions, IPlans plans, Cleaner cleaner, ILedger ledger, IDiscoveries discoveries, IRules rules, DiskTrend disk, ServerConfig config, TimeProvider clock)
{
    /// <summary>What is on the machine, grouped by whose it is.</summary>
    public async Task<Answer> InventoryAsync(CancellationToken cancellationToken)
    {
        var asker = await sessions.CurrentAsync(cancellationToken);
        var inventory = await census.TakeAsync(asker, cancellationToken);
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

    /// <summary>The plan for a scope, kept under an id until it is carried out or goes stale.</summary>
    public async Task<Answer> PlanAsync(Scope scope, CancellationToken cancellationToken)
    {
        var asker = await sessions.CurrentAsync(cancellationToken);

        if (!asker.IsKnown && scope == Scope.Mine)
            return Answer.Failed(Messages.NoSession, Messages.NoSessionHint);

        var inventory = await census.TakeAsync(asker, cancellationToken);
        var plan = Planner.Decide(inventory, scope, Guid.NewGuid().ToString("n")[..10]);

        await plans.SaveAsync(plan, cancellationToken);

        var shown = Shown(plan);

        if (Warned(inventory) is { } warnings)
            shown.Insert(0, "warnings", warnings);

        return Answer.Ok(shown);
    }

    /// <summary>Carries out the removals of a plan that was shown, or only the ones named.</summary>
    public async Task<Answer> ApplyAsync(string planId, IReadOnlyList<string>? only, CancellationToken cancellationToken)
    {
        var plan = await plans.FindAsync(planId, cancellationToken);

        if (plan is null)
            return Answer.Failed($"there is no plan {planId}", Messages.PlanHint);

        if (!plan.IsFresh(clock.GetUtcNow()))
            return Answer.Failed($"plan {planId} was made {(clock.GetUtcNow() - plan.MadeAt).TotalMinutes:0} minutes ago and is stale", Messages.PlanHint);

        var chosen = only is { Count: > 0 } ? plan with { Decisions = [.. plan.Decisions.Where(decision => only.Contains(decision.Resource.Id))] } : plan;
        var releases = await cleaner.ApplyAsync(chosen, cancellationToken);

        return Answer.Ok(Released(releases));
    }

    /// <summary>Releases one resource now, if a plan over everything would remove it.</summary>
    public async Task<Answer> ReleaseAsync(string id, CancellationToken cancellationToken)
    {
        var asker = await sessions.CurrentAsync(cancellationToken);
        var plan = Planner.Decide(await census.TakeAsync(asker, cancellationToken), Scope.Everything, "release");
        var decision = plan.Decisions.FirstOrDefault(candidate => candidate.Resource.Id == id);

        if (decision is null)
            return Answer.Failed($"there is no {id} on this machine now", Messages.ReleaseHint);

        if (decision.Verdict != Verdict.Remove)
            return Answer.Failed($"{id} is not released: {decision.Reason}", Messages.ReleaseHint);

        return Answer.Ok(Released(await cleaner.ApplyAsync(plan with { Decisions = [decision] }, cancellationToken)));
    }

    /// <summary>Asks for a resource to be left alone, or withdraws that.</summary>
    public async Task<Answer> KeepAsync(string id, string? reason, bool withdraw, CancellationToken cancellationToken)
    {
        if (withdraw)
            return Answer.Ok(new JsonObject { ["id"] = id, ["isKept"] = false, ["wasKept"] = await ledger.TryUnkeepAsync(id, cancellationToken) });

        if (string.IsNullOrWhiteSpace(reason))
            return Answer.Failed("a resource is kept for a reason, and none was given", Messages.KeepHint);

        await ledger.KeepAsync(id, reason, cancellationToken);

        return Answer.Ok(new JsonObject { ["id"] = id, ["isKept"] = true, ["reason"] = reason });
    }

    /// <summary>Turns what was discovered into a known kind: a rule written to <c>rules.json</c>, and the discoveries it covers settled.</summary>
    public async Task<Answer> LearnAsync(Rule rule, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rule.Pattern))
            return Answer.Failed("a rule needs a pattern", Messages.LearnHint);

        if (rule.Action != RuleAction.Ignore && rule.Trail != "file")
            return Answer.Failed($"only files can be reported or cached by a rule; {rule.Trail} can only be ignored", Messages.LearnHint);

        await rules.AddAsync(rule, cancellationToken);

        return Answer.Ok(new JsonObject
        {
            ["rule"] = $"{rule.Trail} {rule.Pattern} → {rule.Action.ToString().ToLowerInvariant()}",
            ["settled"] = await discoveries.SettleAsync(rule, cancellationToken)
        });
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
        ["remove"] = Views.Listed(plan.Removals, Views.Of),
        ["report"] = Views.Listed([.. plan.Decisions.Where(decision => decision.Verdict == Verdict.Report)], Views.Of),
        ["keep"] = Views.Listed([.. plan.Decisions.Where(decision => decision.Verdict == Verdict.Keep)], Views.Of),
        ["discoveries"] = Views.Listed(plan.Discoveries, Views.Of)
    };

    private static JsonObject Released(IReadOnlyList<Release> releases) => new()
    {
        ["freed"] = Views.Size(
            releases.Where(release => release.Outcome == Outcome.Released).Sum(release => release.Bytes ?? 0),
            releases.Any(release => release.Outcome == Outcome.Released && release.IsAtLeast)),
        ["released"] = releases.Count(release => release.Outcome == Outcome.Released),
        ["results"] = new JsonArray([.. releases.Select(release => (JsonNode)new JsonObject
        {
            ["id"] = release.ResourceId,
            ["outcome"] = release.Outcome.ToString().ToLowerInvariant(),
            ["detail"] = release.Detail
        })])
    };
}
