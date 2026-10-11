using System.Globalization;

namespace Snail.MCP.CleanUp.Adapters.Trails;

/// <summary>The TCP ports something listens on, as <c>lsof</c> lists them.</summary>
/// <remarks>
/// A port is the first thing anyone notices of a server left running — «address already in use» — and
/// the program behind it is the process kind's to answer for. A new port whose program no kind claims is
/// written down by its number, because the number is what a person will search for.
/// </remarks>
public sealed class PortTrail(IPrograms programs) : ITrail
{
    /// <inheritdoc/>
    public string Name => "port";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Trace>> FollowAsync(IReadOnlyList<Rule> rules, CancellationToken cancellationToken)
    {
        var run = await programs.ReadAsync("lsof", Listeners.Arguments, IPrograms.Census, TimeSpan.FromSeconds(15), cancellationToken);

        return run.ExitCode is 0 or 1 ? Parse(run.Output) : throw new InvalidOperationException(run.Failure);
    }

    /// <inheritdoc/>
    public Task<Trace> MeasureAsync(Trace trace, CancellationToken cancellationToken) => Task.FromResult(trace);

    /// <summary>The ports of <c>lsof -F pcn</c> output: <c>p</c> starts a process, <c>c</c> names it, <c>n</c> is an address ending in a port.</summary>
    internal static IReadOnlyList<Trace> Parse(string output)
    {
        var traces = new Dictionary<string, Trace>(StringComparer.Ordinal);
        var pid = string.Empty;
        var program = string.Empty;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (line[0])
            {
                case 'p':
                    pid = line[1..];
                    break;
                case 'c':
                    program = line[1..];
                    break;
                case 'n' when Port(line[1..]) is { } port:
                    traces.TryAdd(port, new Trace("port", port, $"TCP {port}, listened on by {program} ({pid})"));
                    break;
            }
        }

        return [.. traces.Values];
    }

    private static string? Port(string address)
    {
        var colon = address.LastIndexOf(':');

        return colon >= 0 && int.TryParse(address[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? $"{port}" : null;
    }
}
