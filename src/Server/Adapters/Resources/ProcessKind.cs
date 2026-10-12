using System.Globalization;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>Programs an agent started: servers, watchers, workers — and how to end one without ending anybody else's.</summary>
/// <remarks>
/// An agent runs its commands in shells it starts as its own children, and runs its MCP servers as children
/// too. Only what lives under the shells is the agent's work; what lives under a server is the server's,
/// and ending it would break the session that is asking. So ancestry is counted through the shells alone,
/// and through the programs this server started on the agent's behalf and wrote down at birth.
/// <para>
/// An Android emulator is left to the emulator kind, which ends it the way the emulator expects.
/// </para>
/// <para>
/// A program that detached is a child of launchd, and nothing about it says whose it is any more. It is
/// still listed — without an owner — so that the ledger, which saw it while it was a child, can say. An MCP
/// server the ledger saw under an agent is the MCP kind's, see <see cref="McpKind"/>.
/// </para>
/// </remarks>
public sealed class ProcessKind(IProcessTable table, IPrograms programs, ILedger ledger, TimeProvider clock) : IResourceKind
{
    /// <summary>How long a program is given to end on SIGTERM before it is killed.</summary>
    public static TimeSpan Grace { get; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How young a program under a running agent's shell may be and still be the command the agent is running
    /// right now. A cleanup asked for while a command runs must not end that command: measured on 2026-10-11,
    /// when the first live plan of this server listed the test run that had asked for it.
    /// </summary>
    public static TimeSpan Fresh { get; } = TimeSpan.FromMinutes(2);

    /// <inheritdoc/>
    public string Name => Kinds.Process;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken)
    {
        var running = await table.ListAsync(cancellationToken);

        if (running.Count == 0)
            return Observation.Unseen(Name, "the process table could not be read");

        var children = running.ToLookup(process => process.ParentPid);
        var listening = await Listeners.ReadAsync(programs, cancellationToken);
        var found = new Dictionary<string, Resource>(StringComparer.Ordinal);
        var own = Own(running, children, Environment.ProcessId);
        var now = clock.GetUtcNow();

        foreach (var (pid, owner) in Lineage.Of(running, sessions))
        {
            foreach (var process in running.Where(process => process.Pid == pid && !own.Contains(process.Pid)))
            {
                var age = now - process.StartedAt;

                found.TryAdd(process.Key, Described(process, owner, Evidence.Ancestry, listening.GetValueOrDefault(process.Pid) ?? []) with
                {
                    Blocker = age < Fresh ? $"started {age.TotalSeconds:0} s ago under a running agent: it may be the command the agent is running right now" : null
                });
            }
        }

        var holdings = await ledger.HoldingsAsync(cancellationToken);

        foreach (var holding in Recorded(holdings))
        {
            var root = running.FirstOrDefault(process => $"{Name}:{process.Key}" == holding.ResourceId);

            if (root is not null)
                Add(found, holding.Owner, Evidence.Ancestry, Lineage.Subtree(root, children), listening);
        }

        var servers = holdings
            .Where(holding => holding.ResourceId.StartsWith($"{Kinds.McpServer}:", StringComparison.Ordinal))
            .Select(holding => holding.ResourceId[(Kinds.McpServer.Length + 1)..])
            .ToHashSet(StringComparer.Ordinal);

        Add(found, Owner.Nobody, Evidence.None, running.Where(process => IsDetached(process) && !own.Contains(process.Pid) && !servers.Contains(process.Key)), listening);

        var claimed = found.Values
            .Where(resource => resource.Owner.IsKnown)
            .SelectMany(resource => Ports(resource).Select(port => $"port:{port}"));

        return new Observation(Name, [.. found.Values], [.. claimed]);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var pid = int.Parse(resource.Facts["pid"], CultureInfo.InvariantCulture);
        var running = (await table.ListAsync(cancellationToken)).FirstOrDefault(process => process.Pid == pid);

        if (running is null)
            return Release.AlreadyGone(resource);

        if (!string.Equals(running.Key, resource.Key, StringComparison.Ordinal))
            return Release.NotTheSame(resource, $"process {pid} is now another program, started {running.StartedAt:u}");

        if (await EndedAsync(pid, "TERM", Grace, cancellationToken))
            return Release.Done(resource, Freed(resource, "ended on SIGTERM"));

        if (await EndedAsync(pid, "KILL", TimeSpan.FromSeconds(2), cancellationToken))
            return Release.Done(resource, Freed(resource, $"killed: it outlived SIGTERM by {Grace.TotalSeconds:0} s"));

        return Release.Refused(resource, $"process {pid} survived SIGKILL; it may belong to another user");
    }

    private IEnumerable<Holding> Recorded(IEnumerable<Holding> holdings) =>
        holdings.Where(holding => holding.Evidence == Evidence.Ledger && holding.ResourceId.StartsWith($"{Name}:", StringComparison.Ordinal));

    private async Task<bool> EndedAsync(int pid, string signal, TimeSpan patience, CancellationToken cancellationToken)
    {
        await programs.RunAsync("kill", [$"-{signal}", $"{pid}"], TimeSpan.FromSeconds(5), cancellationToken);
        table.Refresh();

        var deadline = clock.GetUtcNow() + patience;

        while (clock.GetUtcNow() < deadline)
        {
            if (!(await programs.RunAsync("kill", ["-0", $"{pid}"], TimeSpan.FromSeconds(5), cancellationToken)).IsSuccess)
                return true;

            await Task.Delay(TimeSpan.FromMilliseconds(250), clock, cancellationToken);
        }

        return !(await programs.RunAsync("kill", ["-0", $"{pid}"], TimeSpan.FromSeconds(5), cancellationToken)).IsSuccess;
    }

    private static string Freed(Resource resource, string how) =>
        Ports(resource) is { Count: > 0 } ports ? $"{how}; port {string.Join(", ", ports)} is free again" : how;

    private void Add(
        Dictionary<string, Resource> found,
        Owner owner,
        Evidence evidence,
        IEnumerable<RunningProcess> processes,
        IReadOnlyDictionary<int, IReadOnlyList<int>> listening)
    {
        foreach (var process in processes.Where(process => process.Pid != Environment.ProcessId && Android.DeviceOf(process.Command) is null && !MachineSessions.IsAgent(process)))
            found.TryAdd(process.Key, Described(process, owner, evidence, listening.GetValueOrDefault(process.Pid) ?? []));
    }

    /// <summary>This server, the programs above it and the programs it started: none of them is ever anybody's leftover.</summary>
    internal static HashSet<int> Own(IReadOnlyList<RunningProcess> running, ILookup<int, RunningProcess> children, int self)
    {
        var byPid = running.ToDictionary(process => process.Pid);
        var own = new HashSet<int> { self };

        if (byPid.TryGetValue(self, out var server))
            own.UnionWith(Lineage.Subtree(server, children).Select(process => process.Pid));

        var pid = self;

        while (byPid.TryGetValue(pid, out var process) && process.ParentPid > 1)
        {
            own.Add(process.ParentPid);
            pid = process.ParentPid;
        }

        return own;
    }

    private Resource Described(RunningProcess process, Owner owner, Evidence evidence, IReadOnlyList<int> ports) => new()
    {
        Kind = Name,
        Key = process.Key,
        Title = ports.Count > 0 ? $"{process.Program} ({process.Pid}) on port {string.Join(", ", ports)}" : $"{process.Program} ({process.Pid})",
        Owner = owner,
        Evidence = evidence,
        CreatedAt = process.StartedAt,
        Facts = new Dictionary<string, string>
        {
            ["pid"] = $"{process.Pid}",
            ["command"] = process.Command.Length <= 300 ? process.Command : process.Command[..300],
            ["ports"] = string.Join(',', ports)
        }
    };

    private static IReadOnlyList<int> Ports(Resource resource) =>
        resource.Facts.TryGetValue("ports", out var ports) && ports.Length > 0
            ? [.. ports.Split(',').Select(port => int.Parse(port, CultureInfo.InvariantCulture))]
            : [];

    private static bool IsDetached(RunningProcess process) =>
        process.ParentPid == 1 && !MacPlaces.IsSystem(process.Command);
}
