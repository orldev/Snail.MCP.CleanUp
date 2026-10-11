using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Core.Application;

namespace Snail.MCP.CleanUp.Application;

/// <summary>What this server does when Claude Code calls it as a hook rather than as an MCP server.</summary>
/// <remarks>
/// A hook reaches every session on the machine, including the ones whose agent has no MCP server of this
/// kind configured — and only a hook hears that a session has ended. So the sessions are followed here:
/// a census when one starts, a census after every turn, and a sweep of the session's own proven leftovers
/// when it ends. A hook never blocks the agent and never prints to it: whatever happens is written to the
/// sweep log, and the hook exits cleanly.
/// </remarks>
public sealed class Hooks(MeasuredCensus census, Cleaner cleaner, ServerConfig config, ILogger<Hooks> logger, TimeProvider clock)
{
    /// <summary>The verb Claude Code calls the server with as a hook.</summary>
    public const string Verb = "hook";

    /// <summary>The verb a detached census is started with.</summary>
    public const string CensusVerb = "census";

    /// <summary>Handles one hook event, as Claude Code writes it to stdin.</summary>
    public async Task HandleAsync(string input, CancellationToken cancellationToken)
    {
        var (name, session, reason) = Read(input);

        switch (name)
        {
            case "SessionStart":
                await census.TakeAsync(session, cancellationToken);
                break;
            case "Stop":
                Detach(CensusVerb);
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
        var plan = Planner.Decide(await census.TakeAsync(session, cancellationToken), Scope.Mine, $"sweep-{Guid.NewGuid():n}"[..16]);
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
    private void Detach(string verb)
    {
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        var (program, arguments) = Invocation.Self();

        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("\"$0\" \"$@\" >/dev/null 2>&1 </dev/null &");
        start.ArgumentList.Add(program);

        foreach (var argument in arguments.Append(verb))
            start.ArgumentList.Add(argument);

        try
        {
            using var started = Process.Start(start);
            started?.WaitForExit(5_000);
        }
        catch (System.ComponentModel.Win32Exception failure)
        {
            logger.LogWarning(failure, "could not start a detached {Verb}", verb);
        }
    }

    private async Task WriteAsync(Owner session, Plan plan, IReadOnlyList<Release> releases, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(config.DataDirectory);

        var lines = releases.Select(release => $"  {release.Outcome.ToString().ToLowerInvariant()} {release.ResourceId}: {release.Detail}")
            .Concat(plan.Decisions.Where(decision => decision.Verdict != Verdict.Remove).Select(decision => $"  {decision.Verdict.ToString().ToLowerInvariant()} {decision.Resource.Id}: {decision.Reason}"));
        var entry = $"{clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} sweep of {session}\n{string.Join('\n', lines)}\n";

        await File.AppendAllTextAsync(Path.Combine(config.DataDirectory, "sweeps.log"), entry, cancellationToken);
    }
}

/// <summary>How this program is started again: as the tool it was installed as, or as a dll run by dotnet.</summary>
/// <remarks>
/// It is started again the way it was started, and a dll run by dotnet is not traded for the native launcher next to
/// it. Measured on 2026-10-12, a hook costs 67 ms either way once it has run, but the launcher is signed only ad hoc, so
/// macOS looks into every new copy at its first start: 0.4 s for a copy, 3 s after a build — paid by the first hook
/// after every update, and gaining nothing after.
/// </remarks>
public static class Invocation
{
    /// <summary>The program and the arguments that start this server, before any verb.</summary>
    public static (string Program, IReadOnlyList<string> Arguments) Self()
    {
        var process = Environment.ProcessPath ?? "dotnet";
        var assembly = typeof(Invocation).Assembly.Location;

        return Path.GetFileNameWithoutExtension(process) == "dotnet" ? (process, [assembly]) : (process, []);
    }

    /// <summary>The same, as one command line for a hook entry.</summary>
    public static string CommandLine(string verb)
    {
        var (program, arguments) = Self();

        return Quoted([program, .. arguments, verb]);
    }

    /// <summary>The command line of a hook run by the native forwarder: the socket first, then this program as it runs when no server answers.</summary>
    public static string Forwarded(string forwarder, string socket, string verb)
    {
        var (program, arguments) = Self();

        return Quoted([forwarder, socket, program, .. arguments, verb]);
    }

    private static string Quoted(IEnumerable<string> words) =>
        string.Join(' ', words.Select(word => word.Contains(' ', StringComparison.Ordinal) ? $"\"{word}\"" : word));
}
