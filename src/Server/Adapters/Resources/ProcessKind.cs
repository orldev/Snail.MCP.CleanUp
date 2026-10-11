using System.Globalization;

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
/// still listed — without an owner — so that the ledger, which saw it while it was a child, can say.
/// </para>
/// </remarks>
public sealed class ProcessKind(IProcessTable table, IPrograms programs, ILedger ledger, TimeProvider clock) : IResourceKind
{
    /// <summary>How long a program is given to end on SIGTERM before it is killed.</summary>
    public static TimeSpan Grace { get; } = TimeSpan.FromSeconds(5);

    private static readonly string[] SystemPlaces =
        ["/Applications/", "/System/", "/usr/libexec/", "/usr/sbin/", "/sbin/", "/Library/Apple/", "/Library/Application Support/", "/usr/bin/"];

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

        foreach (var (pid, owner) in Lineage.Of(running, sessions))
            Add(found, owner, Evidence.Ancestry, running.Where(process => process.Pid == pid), listening);

        foreach (var holding in await RecordedAsync(cancellationToken))
        {
            var root = running.FirstOrDefault(process => $"{Name}:{process.Key}" == holding.ResourceId);

            if (root is not null)
                Add(found, holding.Owner, Evidence.Ancestry, Lineage.Subtree(root, children), listening);
        }

        Add(found, Owner.Nobody, Evidence.None, running.Where(IsDetached), listening);

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

    private async Task<IEnumerable<Holding>> RecordedAsync(CancellationToken cancellationToken) =>
        (await ledger.HoldingsAsync(cancellationToken)).Where(holding => holding.Evidence == Evidence.Ledger && holding.ResourceId.StartsWith($"{Name}:", StringComparison.Ordinal));

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
        foreach (var process in processes.Where(process => process.Pid != Environment.ProcessId && Android.DeviceOf(process.Command) is null))
            found.TryAdd(process.Key, Described(process, owner, evidence, listening.GetValueOrDefault(process.Pid) ?? []));
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
        process.ParentPid == 1 && !SystemPlaces.Any(place => process.Command.StartsWith(place, StringComparison.Ordinal));
}
