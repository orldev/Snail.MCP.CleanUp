using System.Globalization;
using System.Text.Json;
using Snail.MCP.CleanUp.Configuration;
using Snail.Toolkit.Cleanup.Application;

namespace Snail.MCP.CleanUp.Application;

/// <summary>What this server does when Claude Code calls it as a hook rather than as an MCP server.</summary>
/// <remarks>
/// A hook reaches every session on the machine, including the ones whose agent has no MCP server of this
/// kind configured — and only a hook hears that a session has ended. So the sessions are followed here:
/// a census when one starts, a census after every turn, and a sweep of the session's own proven leftovers
/// when it ends. A hook never blocks the agent and never prints to it: whatever happens is written to the
/// sweep log, and the hook exits cleanly.
/// </remarks>
public sealed class Hooks(MeasuredCensus census, Cleaner cleaner, IPrograms programs, ServerConfig config, ILogger<Hooks> logger, TimeProvider clock)
{
    /// <summary>The verb Claude Code calls the server with as a hook.</summary>
    public const string Verb = "hook";

    /// <summary>The verb a detached census is started with.</summary>
    public const string CensusVerb = "census";

    /// <summary>Handles one hook event, as Claude Code writes it to stdin.</summary>
    /// <remarks>A machine whose sessions cannot be told is no reason to fail the agent's hook: nothing is decided, and the sweep log says why.</remarks>
    public async Task HandleAsync(string input, CancellationToken cancellationToken)
    {
        var (name, session, reason) = Read(input);

        try
        {
            await HandledAsync(name, session, reason, cancellationToken);
        }
        catch (SessionsUnknownException unknown)
        {
            await NoteAsync($"{name} of {session} not handled: {unknown.Message}", cancellationToken);
        }
    }

    private async Task HandledAsync(string name, Owner session, string? reason, CancellationToken cancellationToken)
    {
        switch (name)
        {
            case "SessionStart":
                await census.TakeAsync(session, cancellationToken);
                break;
            case "Stop":
                await DetachAsync(CensusVerb, cancellationToken);
                break;
            case "SessionEnd" when reason != "clear" && session.IsKnown:
                await SweepAsync(session, cancellationToken);
                break;
        }
    }

    /// <summary>Takes a census on its own, for a hook that must not wait for one.</summary>
    public Task CensusAsync(CancellationToken cancellationToken) => census.TakeAsync(Owner.Nobody, cancellationToken);

    /// <summary>Releases what the ending session is proven to have left, and writes what happened to the sweep log.</summary>
    /// <remarks>
    /// A session that ends with <c>/clear</c> goes on in the same process under a new id, with the dev server
    /// it started still in use — so a clear is not an end, and nothing is swept for it.
    /// </remarks>
    public async Task<IReadOnlyList<Release>> SweepAsync(Owner session, CancellationToken cancellationToken)
    {
        var plan = Planner.Decide(await census.TakeAsync(session, cancellationToken), Scope.Mine, $"sweep-{Guid.NewGuid():n}"[..16], MachineKinds.Order);
        var releases = await cleaner.ApplyAsync(plan, cancellationToken);

        await WriteAsync(session, plan, releases, cancellationToken);

        return releases;
    }

    /// <summary>The event, the session and the reason in a hook's input; nobody when the input says none.</summary>
    internal static (string Name, Owner Session, string? Reason) Read(string input)
    {
        try
        {
            using var document = JsonDocument.Parse(input);
            var root = document.RootElement;

            return (Text(root, "hook_event_name") ?? string.Empty, Text(root, "session_id") is { } id ? Owner.Session(id) : Owner.Nobody, Text(root, "reason"));
        }
        catch (JsonException)
        {
            return (string.Empty, Owner.Nobody, null);
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Starts this program again with a verb, in the background, so that the hook returns at once.</summary>
    private async Task DetachAsync(string verb, CancellationToken cancellationToken)
    {
        var (program, arguments) = Invocation.Self();

        if (!await programs.TryDetachAsync([program, .. arguments, verb], cancellationToken))
            Log.DetachedNotStarted(logger, verb);
    }

    private async Task WriteAsync(Owner session, Plan plan, IReadOnlyList<Release> releases, CancellationToken cancellationToken)
    {
        var lines = releases.Select(release => $"  {release.Outcome.ToString().ToLowerInvariant()} {release.ResourceId}: {release.Detail}")
            .Concat(plan.Decisions.Where(decision => decision.Verdict != Verdict.Remove).Select(decision => $"  {decision.Verdict.ToString().ToLowerInvariant()} {decision.Resource.Id}: {decision.Reason}"));
        await NoteAsync($"sweep of {session}\n{string.Join('\n', lines)}", cancellationToken);
    }

    /// <summary>Writes a line of what a hook did, or why it did nothing, to the sweep log.</summary>
    private async Task NoteAsync(string said, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(config.DataDirectory);

        var entry = $"{clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {said}\n";

        await File.AppendAllTextAsync(Path.Combine(config.DataDirectory, "sweeps.log"), entry, cancellationToken);
    }
}
