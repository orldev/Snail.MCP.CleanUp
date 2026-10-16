namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>Ports adb forwards between this Mac and its devices, in either direction.</summary>
/// <remarks>
/// A forward outlives the command that made it for as long as its device runs, and holds a port of this Mac
/// all that time. Nothing in a forward says who made it, so these are only ever named, never removed on
/// ownership — and they are not looked for at all unless adb's server is already running.
/// </remarks>
public sealed class ForwardKind(Android android, IPrograms programs) : IResourceKind
{
    /// <inheritdoc/>
    public string Name => MachineKinds.Forward;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Holder> sessions, CancellationToken cancellationToken)
    {
        if (!File.Exists(android.Adb) || !await android.IsAdbRunningAsync(cancellationToken))
            return new Observation(Name, [], []);

        var forwards = await programs.RunAsync(android.Adb, ["forward", "--list"], TimeSpan.FromSeconds(10), cancellationToken);
        var found = Parse(forwards.Output, "forward").ToList();

        foreach (var serial in Serials((await programs.RunAsync(android.Adb, ["devices"], TimeSpan.FromSeconds(10), cancellationToken)).Output))
        {
            var reverses = await programs.RunAsync(android.Adb, ["-s", serial, "reverse", "--list"], TimeSpan.FromSeconds(10), cancellationToken);

            found.AddRange(Parse(reverses.Output, "reverse").Select(resource => resource with { Key = $"reverse/{serial}/{resource.Facts["local"]}", Facts = new Dictionary<string, string>(resource.Facts) { ["serial"] = serial } }));
        }

        return new Observation(Name, found, []);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var direction = resource.Facts["direction"];
        var spec = resource.Facts["local"];
        var run = await programs.RunAsync(android.Adb, ["-s", resource.Facts["serial"], direction, "--remove", spec], TimeSpan.FromSeconds(10), cancellationToken);

        return run.IsSuccess ? Release.Done(resource, $"removed the {direction} of {spec}") : Release.Refused(resource, run.Failure);
    }

    /// <summary>The lines of <c>adb forward --list</c> or <c>adb reverse --list</c>: a serial, then the end <c>--remove</c> takes, then the other.</summary>
    internal static IEnumerable<Resource> Parse(string output, string direction) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 3)
            .Select(parts => new Resource
            {
                Kind = MachineKinds.Forward,
                Key = $"{direction}/{parts[0]}/{parts[1]}",
                Title = $"adb {direction} {parts[1]} → {parts[2]} on {parts[0]}",
                Facts = new Dictionary<string, string> { ["direction"] = direction, ["serial"] = parts[0], ["local"] = parts[1], ["remote"] = parts[2] }
            });

    private static IEnumerable<string> Serials(string devices) =>
        devices.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1)
            .Select(line => line.Split('\t', ' ')[0])
            .Where(serial => serial.Length > 0);
}
