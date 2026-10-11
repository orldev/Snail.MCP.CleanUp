using System.Globalization;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>The process table as <c>ps</c> prints it, read at most once every couple of seconds.</summary>
/// <remarks>
/// One census asks several kinds that all want the table — sessions, processes, the programs driving a
/// simulator — and the answers have to agree with each other, so they share one reading of it.
/// </remarks>
public sealed class ProcessTable(IPrograms programs, TimeProvider clock) : IProcessTable, IDisposable
{
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _reading = new(1, 1);

    private (DateTimeOffset At, IReadOnlyList<RunningProcess> Processes)? _last;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RunningProcess>> ListAsync(CancellationToken cancellationToken)
    {
        await _reading.WaitAsync(cancellationToken);

        try
        {
            if (_last is { } last && clock.GetUtcNow() - last.At < Freshness)
                return last.Processes;

            var run = await programs.RunAsync("ps", ["-axww", "-o", "pid=,ppid=,lstart=,command="], TimeSpan.FromSeconds(10), cancellationToken);
            var processes = run.IsSuccess ? Parse(run.Output) : [];

            _last = (clock.GetUtcNow(), processes);

            return processes;
        }
        finally
        {
            _reading.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _reading.Dispose();

    /// <summary>
    /// The lines of <c>ps -o pid=,ppid=,lstart=,command=</c> under the C locale: two numbers, a start written
    /// as five words such as <c>Sun Oct 11 14:04:06 2026</c>, then the command line to the end.
    /// </summary>
    internal static IReadOnlyList<RunningProcess> Parse(string output)
    {
        var processes = new List<RunningProcess>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var words = line.Split(' ', 8, StringSplitOptions.RemoveEmptyEntries);

            if (words.Length < 8 || !int.TryParse(words[0], CultureInfo.InvariantCulture, out var pid) || !int.TryParse(words[1], CultureInfo.InvariantCulture, out var parent))
                continue;

            if (!DateTime.TryParseExact(
                    string.Join(' ', words[2..7]),
                    ["ddd MMM d HH:mm:ss yyyy", "ddd MMM dd HH:mm:ss yyyy"],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var started))
                continue;

            processes.Add(new RunningProcess(pid, parent, new DateTimeOffset(started), words[7].Trim()));
        }

        return processes;
    }
}
