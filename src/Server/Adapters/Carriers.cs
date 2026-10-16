namespace Snail.MCP.CleanUp.Adapters;

/// <summary>Which running programs name the session that started them in their environment, and whose that makes them.</summary>
/// <remarks>
/// Claude Code gives every program it starts <c>CLAUDE_CODE_SESSION_ID</c>, and a program keeps it when it detaches: a
/// <c>python -m http.server</c> started with <c>( … &amp; )</c> lived seven hours as a child of launchd on 2026-10-13, with
/// nothing but its environment left to say whose it was. The environment is proof of the same kind as a label — written
/// when the program was made — and it outlives the agent, so a program whose session has ended is that session's orphan.
/// <para>
/// Four things are never taken on it. The servers builds and editors share (<see cref="SharedServers"/>), which carry
/// whichever session started them while every window reuses them. The agents themselves. What runs under the MCP servers of a running agent — its
/// direct children other than shells, and everything below them: they carry the session too, and they are its tools (see
/// <see cref="Lineage"/>). And the session's name as it was: after <c>/clear</c> the agent goes on under a new session id,
/// while the programs it started before still name the old one, so a program whose agent — <c>CLAUDE_PID</c>, or the agent
/// whose children name the same session — still runs belongs to that agent's session now, which is still running.
/// </para>
/// </remarks>
public static class Carriers
{
    /// <summary>How far a program may seem to have started before its agent, the two starts read apart.</summary>
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(1);

    /// <summary>The owner the environment of every program that names a session proves, by process id.</summary>
    public static IReadOnlyDictionary<int, Owner> Of(IReadOnlyList<RunningProcess> running, IReadOnlyList<Holder> holders)
    {
        var owners = new Dictionary<int, Owner>();
        var carriers = running.Where(process => process.SessionId is not null).ToList();

        if (carriers.Count == 0)
            return owners;

        var byPid = new Dictionary<int, RunningProcess>(running.Count);

        foreach (var process in running)
            byPid[process.Pid] = process;

        var agents = running.Where(MachineSessions.IsAgent).ToDictionary(agent => agent.Pid);
        var alive = holders.OfType<Session>().Where(session => session.IsAlive).ToList();
        var aliveIds = alive.Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
        var aliveByPid = alive.Where(session => session.Pid is not null).GroupBy(session => session.Pid!.Value).ToDictionary(group => group.Key, group => group.First().Owner);
        var spokenFor = SpokenFor(carriers, agents);

        foreach (var process in carriers)
        {
            if (agents.ContainsKey(process.Pid) || SharedServers.Is(process) || IsUnderAServer(process, byPid, agents))
                continue;

            owners[process.Pid] = OwnerOf(process, agents, aliveIds, aliveByPid, spokenFor);
        }

        return owners;
    }

    private static Owner OwnerOf(
        RunningProcess process,
        Dictionary<int, RunningProcess> agents,
        HashSet<string> aliveIds,
        Dictionary<int, Owner> aliveByPid,
        Dictionary<string, RunningProcess> spokenFor)
    {
        var id = process.SessionId!;

        if (aliveIds.Contains(id))
            return Owner.Session(id);

        var agent = process.AgentPid is { } pid && agents.TryGetValue(pid, out var named) && named.StartedAt <= process.StartedAt + Slack
            ? named
            : spokenFor.GetValueOrDefault(id);

        if (agent is null)
            return Owner.Session(id);

        return aliveByPid.TryGetValue(agent.Pid, out var owner) ? owner : Owner.Session(MachineSessions.NameOf(agent));
    }

    /// <summary>The running agent each session is still spoken for by: one whose own children name it.</summary>
    private static Dictionary<string, RunningProcess> SpokenFor(List<RunningProcess> carriers, Dictionary<int, RunningProcess> agents)
    {
        var spokenFor = new Dictionary<string, RunningProcess>(StringComparer.Ordinal);

        foreach (var child in carriers)
        {
            if (agents.TryGetValue(child.ParentPid, out var agent))
                spokenFor.TryAdd(child.SessionId!, agent);
        }

        return spokenFor;
    }

    /// <summary>Whether a program is a running agent's tool, or runs under one: on the way up, the child of the agent is not a shell.</summary>
    private static bool IsUnderAServer(RunningProcess process, Dictionary<int, RunningProcess> byPid, Dictionary<int, RunningProcess> agents)
    {
        var child = process;
        var seen = new HashSet<int> { process.Pid };

        while (byPid.TryGetValue(child.ParentPid, out var parent) && seen.Add(parent.Pid))
        {
            if (agents.ContainsKey(parent.Pid))
                return !Lineage.IsShell(child);

            child = parent;
        }

        return false;
    }
}
