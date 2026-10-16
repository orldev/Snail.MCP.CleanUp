using System.Collections.Frozen;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>Which running programs are an agent's work: what lives under the shells it starts for its commands.</summary>
/// <remarks>
/// An agent's direct children are its shells and its MCP servers. Only the shells run its work; the servers
/// are its tools, and what they start is theirs. Counting ancestry through the shells alone is what keeps a
/// cleanup from ending the language server or the browser the asking session is using.
/// </remarks>
public static class Lineage
{
    private static readonly FrozenSet<string> Shells = FrozenSet.Create(StringComparer.Ordinal, "zsh", "bash", "sh", "dash", "fish", "-zsh", "-bash");

    /// <summary>Whether a program is a shell.</summary>
    public static bool IsShell(RunningProcess process) => Shells.Contains(process.Program);

    /// <summary>The owner of every program that runs under the shells of a running agent.</summary>
    /// <remarks>Only a <see cref="Session"/> has an agent whose shells can be followed; another holder has no process here.</remarks>
    public static IReadOnlyDictionary<int, Owner> Of(IReadOnlyList<RunningProcess> running, IReadOnlyList<Holder> holders)
    {
        var children = running.ToLookup(process => process.ParentPid);
        var owners = new Dictionary<int, Owner>();

        foreach (var session in holders.OfType<Session>().Where(session => session.IsAlive && session.Pid is not null))
        {
            foreach (var process in children[session.Pid!.Value].Where(IsShell).SelectMany(shell => Subtree(shell, children)))
                owners.TryAdd(process.Pid, session.Owner);
        }

        return owners;
    }

    /// <summary>A program and everything under it, the program first.</summary>
    public static IEnumerable<RunningProcess> Subtree(RunningProcess root, ILookup<int, RunningProcess> children)
    {
        var waiting = new Queue<RunningProcess>([root]);
        var seen = new HashSet<int>();

        while (waiting.TryDequeue(out var process))
        {
            if (!seen.Add(process.Pid))
                continue;

            yield return process;

            foreach (var child in children[process.Pid])
                waiting.Enqueue(child);
        }
    }
}
