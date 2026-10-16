using System.Collections.Frozen;

namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>The MCP servers agents run: a copy per session of every server, and the ones a session left running when it ended.</summary>
/// <remarks>
/// <para>
/// An agent's direct children are its shells and its MCP servers (see <see cref="Lineage"/>). While the session runs a
/// server is its tool, so it is listed with its owner and a blocker, and never released; the census writes it to the
/// ledger while it is still a child.
/// </para>
/// <para>
/// A stdio server whose agent ended has no client any more: nothing can reach its standard input again. It is a child of
/// launchd by then, and only the ledger still says whose it was — which makes it that session's leftover, ended like any
/// program. The process kind leaves such a program to this one.
/// </para>
/// <para>
/// Every session starts its own copy of every server it is configured with. Each copy says how many there are, which is
/// what shows a server worth running once for all of them.
/// </para>
/// </remarks>
public sealed class McpKind(IProcessTable table, ProcessKind processes, ILedger ledger, TimeProvider clock) : IResourceKind
{
    /// <summary>How long a child of an agent must have run to be taken for a server rather than a hook that is about to end.</summary>
    public static TimeSpan Settled { get; } = TimeSpan.FromSeconds(5);

    private static readonly FrozenSet<string> Runtimes = FrozenSet.Create(StringComparer.Ordinal, "dotnet", "node", "python", "python3", "uv", "uvx", "npx", "bun", "deno", "java");

    /// <inheritdoc/>
    public string Name => MachineKinds.McpServer;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Holder> sessions, CancellationToken cancellationToken)
    {
        var running = await table.ListAsync(cancellationToken);

        if (running.Count == 0)
            return Observation.Unseen(Name, "the process table could not be read");

        var children = running.ToLookup(process => process.ParentPid);
        var own = ProcessKind.Own(running, children, Environment.ProcessId);
        var now = clock.GetUtcNow();
        var found = new List<Resource>();

        foreach (var session in sessions.OfType<Session>().Where(session => session.IsAlive && session.Pid is not null))
        {
            var servers = children[session.Pid!.Value].Where(process =>
                !Lineage.IsShell(process) && !MachineSessions.IsAgent(process) && !own.Contains(process.Pid) && now - process.StartedAt >= Settled);

            found.AddRange(servers.Select(server => Described(server, session.Owner) with { Blocker = $"a server of {session.Owner}, which is running" }));
        }

        var recorded = (await ledger.HoldingsAsync(cancellationToken))
            .Where(holding => holding.ResourceId.StartsWith($"{Name}:", StringComparison.Ordinal))
            .ToDictionary(holding => holding.ResourceId, StringComparer.Ordinal);
        var parentless = running.Where(process => process.ParentPid == 1 && !own.Contains(process.Pid));
        var held = parentless
            .Select(process => (Process: process, Holding: recorded.GetValueOrDefault($"{Name}:{process.Key}")))
            .Where(pair => pair.Holding is not null);
        var orphans = held.Select(pair => Described(pair.Process, pair.Holding!.Owner)).ToList();

        found.AddRange(orphans);

        return new Observation(Name, Counted(found), [.. orphans.Select(orphan => $"process:{orphan.Key}")]);
    }

    /// <inheritdoc/>
    public Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken) =>
        processes.ReleaseAsync(resource, cancellationToken);

    /// <summary>The server a command runs: the program, or the script or assembly a runtime was given.</summary>
    internal static string ServerOf(RunningProcess process)
    {
        if (!Runtimes.Contains(process.Program))
            return process.Program;

        var argument = process.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault(part => !part.StartsWith('-'));

        return argument is null ? process.Program : Path.GetFileNameWithoutExtension(argument.TrimEnd('/'));
    }

    private static IReadOnlyList<Resource> Counted(IReadOnlyList<Resource> found)
    {
        var copies = found.CountBy(resource => resource.Facts["server"]).ToDictionary(StringComparer.Ordinal);

        return
        [
            .. found.Select(resource => copies[resource.Facts["server"]] is var count and > 1
                ? resource with
                {
                    Title = $"{resource.Title}, one of {count} copies",
                    Facts = new Dictionary<string, string>(resource.Facts) { ["copies"] = $"{count}" }
                }
                : resource)
        ];
    }

    private Resource Described(RunningProcess process, Owner owner) => new()
    {
        Kind = Name,
        Key = process.Key,
        Title = $"{ServerOf(process)} ({process.Pid}) for {owner}",
        Owner = owner,
        Evidence = Evidence.Ancestry,
        CreatedAt = process.StartedAt,
        Facts = new Dictionary<string, string>
        {
            ["pid"] = $"{process.Pid}",
            ["server"] = ServerOf(process),
            ["command"] = process.Command.Length <= 300 ? process.Command : process.Command[..300],
            ["ports"] = string.Empty
        }
    };
}
