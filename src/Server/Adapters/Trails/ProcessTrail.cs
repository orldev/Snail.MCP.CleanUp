namespace Snail.MCP.CleanUp.Adapters.Trails;

/// <summary>Programs that were handed to launchd: the ones that outlive whatever started them.</summary>
/// <remarks>
/// A child of an agent is the process kind's to account for. What this trail is for is the program that
/// detached — <c>nohup</c>, a double fork, a daemon that backgrounds itself — and so has launchd as its
/// parent. Applications and the system's own services have launchd as their parent too, so programs living
/// where those live are not followed: a new one there is somebody opening an app, not an agent's leftover.
/// </remarks>
public sealed class ProcessTrail(IProcessTable processes) : ITrail
{
    private static readonly string[] SystemPlaces =
        ["/Applications/", "/System/", "/usr/libexec/", "/usr/sbin/", "/sbin/", "/Library/Apple/", "/Library/Application Support/", "/usr/bin/"];

    /// <inheritdoc/>
    public string Name => "process";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Trace>> FollowAsync(IReadOnlyList<Rule> rules, CancellationToken cancellationToken) =>
    [
        .. (await processes.ListAsync(cancellationToken))
            .Where(process => process.ParentPid == 1 && !SystemPlaces.Any(place => process.Command.StartsWith(place, StringComparison.Ordinal)))
            .Select(process => new Trace(Name, process.Key, Shortened(process.Command)))
    ];

    /// <inheritdoc/>
    public Task<Trace> MeasureAsync(Trace trace, CancellationToken cancellationToken) => Task.FromResult(trace);

    private static string Shortened(string command) => command.Length <= 200 ? command : $"{command[..200]}…";
}
