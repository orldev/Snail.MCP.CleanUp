using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>The sessions of Claude Code on this machine: SleepOf's account first, the process table for the rest.</summary>
/// <remarks>
/// A session is alive while its agent's process is still the program SleepOf heard it from. The process id
/// is not enough: ids are reused, and a session whose id now belongs to a compiler is not alive. So the
/// process must be <c>claude</c>, and must have been running when SleepOf last heard from the session.
/// The session's start is no guide: a resumed session keeps its id and its start from hours ago, while its
/// agent is a process started a minute ago — measured on 2026-10-11, when this very session was counted as
/// ended by a check against its start.
/// <para>
/// When SleepOf is not installed, or a session never reached its hooks, the agents running right now
/// still count: each becomes a session named after its process. Those cannot outlive their process in
/// anyone's memory, but the ledger already wrote down whatever they owned under that name.
/// </para>
/// </remarks>
public sealed class MachineSessions(ServerConfig config, IProcessTable processes) : IHolders
{
    /// <summary>How far SleepOf's clock and the process table's may disagree, the process table reading to the second.</summary>
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(5);

    /// <summary>The prefix of a session known only by its process.</summary>
    public const string ByProcess = "pid-";

    /// <inheritdoc/>
    /// <remarks>Every holder is a <see cref="Session"/>, so that the kinds that follow an agent's children find its process.</remarks>
    public async Task<IReadOnlyList<Holder>> ListAsync(CancellationToken cancellationToken) => await SessionsAsync(cancellationToken);

    /// <summary>Every session known, alive or ended, with the process of its agent.</summary>
    /// <remarks>
    /// An empty process table is a reading that failed, not a machine without agents — this very server runs on it. Read
    /// as one, it made every session ended, and an orphans plan of another session listed the simulator of a running one
    /// (2026-10-16). So it fails, as <see cref="IHolders"/> asks of a host that cannot read its owners: no census, no plan.
    /// </remarks>
    public async Task<IReadOnlyList<Session>> SessionsAsync(CancellationToken cancellationToken)
    {
        var running = await processes.ListAsync(cancellationToken);

        if (running.Count == 0)
            throw new SessionsUnknownException("the process table could not be read, so no session can be told to have ended");

        var agents = running.Where(IsAgent).ToDictionary(process => process.Pid);
        var written = await WrittenAsync(cancellationToken);

        var known = written.Select(entry => Known(entry, agents)).ToList();
        var accounted = known.Where(session => session.IsAlive && session.Pid is not null).Select(session => session.Pid!.Value).ToHashSet();

        known.AddRange(agents.Values
            .Where(agent => !accounted.Contains(agent.Pid))
            .Select(agent => new Session(Owner.Session(NameOf(agent)), agent.Pid, IsAlive: true) { StartedAt = agent.StartedAt }));

        return known;
    }

    /// <summary>A session SleepOf wrote down; one that is over ended when SleepOf last heard from it.</summary>
    private static Session Known(SleepOfAgents.Entry entry, IReadOnlyDictionary<int, RunningProcess> agents)
    {
        var isAlive = IsAlive(entry, agents);

        return new Session(Owner.Session(entry.Id), entry.Pid, isAlive)
        {
            StartedAt = entry.StartedAt,
            EndedAt = isAlive ? null : entry.UpdatedAt ?? entry.StartedAt,
            Directory = entry.Directory
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The server is a child of its agent, so the agent is the nearest ancestor that is one; the session is
    /// the one SleepOf maps that process to, or the process itself when SleepOf does not know it.
    /// </remarks>
    public async Task<Owner> CurrentAsync(CancellationToken cancellationToken)
    {
        var running = (await processes.ListAsync(cancellationToken)).ToDictionary(process => process.Pid);
        var agent = AgentAbove(Environment.ProcessId, running);

        if (agent is null)
            return Owner.Nobody;

        var sessions = await SessionsAsync(cancellationToken);
        var session = sessions.FirstOrDefault(known => known.IsAlive && known.Pid == agent.Pid);

        return Owner.Session(session?.Id ?? NameOf(agent));
    }

    /// <summary>The session name of an agent nobody else has named: its process id and start, which no later process shares.</summary>
    public static string NameOf(RunningProcess agent) => $"{ByProcess}{agent.Key}";

    /// <summary>Whether a process is the agent of a session rather than one of the helpers that share its name.</summary>
    /// <remarks>Claude Code starts a second <c>claude</c> as an MCP server for Chrome; that one is a tool, not a session.</remarks>
    public static bool IsAgent(RunningProcess process) =>
        string.Equals(process.Program, "claude", StringComparison.Ordinal)
        && !process.Command.Contains("--claude-in-chrome-mcp", StringComparison.Ordinal)
        && !process.Command.Contains(" mcp ", StringComparison.Ordinal);

    internal static RunningProcess? AgentAbove(int pid, IReadOnlyDictionary<int, RunningProcess> running)
    {
        var seen = new HashSet<int>();

        while (running.TryGetValue(pid, out var process) && seen.Add(pid))
        {
            if (IsAgent(process))
                return process;

            pid = process.ParentPid;
        }

        return null;
    }

    private static bool IsAlive(SleepOfAgents.Entry entry, IReadOnlyDictionary<int, RunningProcess> agents) =>
        entry.Pid is { } pid
        && !string.Equals(entry.State, "ended", StringComparison.Ordinal)
        && agents.TryGetValue(pid, out var agent)
        && (entry.UpdatedAt ?? entry.StartedAt) is { } heard
        && agent.StartedAt <= heard + Slack;

    private async Task<IReadOnlyList<SleepOfAgents.Entry>> WrittenAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(config.SleepOfDirectory, SleepOfAgents.FileName);

        if (!File.Exists(path))
            return [];

        try
        {
            return SleepOfAgents.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        }
        catch (IOException)
        {
            return [];
        }
    }
}
