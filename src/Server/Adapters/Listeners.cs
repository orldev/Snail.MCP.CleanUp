using System.Globalization;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>Which process listens on which TCP ports, as <c>lsof</c> tells it.</summary>
public static class Listeners
{
    /// <summary>The listening ports of every process that has one.</summary>
    public static async Task<IReadOnlyDictionary<int, IReadOnlyList<int>>> ReadAsync(IPrograms programs, CancellationToken cancellationToken)
    {
        var run = await programs.RunAsync("lsof", ["-nP", "-iTCP", "-sTCP:LISTEN", "-Fpn"], TimeSpan.FromSeconds(15), cancellationToken);

        return Parse(run.Output);
    }

    /// <summary>The ports of <c>lsof -F pn</c> output, by process.</summary>
    internal static IReadOnlyDictionary<int, IReadOnlyList<int>> Parse(string output)
    {
        var ports = new Dictionary<int, SortedSet<int>>();
        var pid = 0;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line[0] == 'p' && int.TryParse(line[1..], CultureInfo.InvariantCulture, out var next))
                pid = next;

            var colon = line.LastIndexOf(':');

            if (line[0] == 'n' && colon > 0 && int.TryParse(line[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port))
            {
                if (!ports.TryGetValue(pid, out var held))
                    ports[pid] = held = [];

                held.Add(port);
            }
        }

        return ports.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<int>)[.. pair.Value]);
    }
}
