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
            var executables = await programs.RunAsync("ps", ["-axww", "-o", "pid=,comm="], TimeSpan.FromSeconds(10), cancellationToken);
            var processes = run.IsSuccess ? Named(Parse(run.Output), Executables(executables.Output)) : [];

            _last = (clock.GetUtcNow(), processes);

            return processes;
        }
        finally
        {
            _reading.Release();
        }
    }

    /// <inheritdoc/>
    public void Refresh() => _last = null;

    /// <inheritdoc/>
    public void Dispose() => _reading.Dispose();

    /// <summary>The executables of <c>ps -o pid=,comm=</c>: an id, then the path to the end of the line, spaces and all.</summary>
    internal static IReadOnlyDictionary<int, string> Executables(string output)
    {
        var executables = new Dictionary<int, string>();

        foreach (var written in output.AsSpan().EnumerateLines())
        {
            var line = written.Trim();

            if (Word(ref line) is var id && int.TryParse(id, CultureInfo.InvariantCulture, out var pid) && line.Trim() is { IsEmpty: false } executable)
                executables[pid] = executable.ToString();
        }

        return executables;
    }

    private static IReadOnlyList<RunningProcess> Named(IReadOnlyList<RunningProcess> processes, IReadOnlyDictionary<int, string> executables) =>
        [.. processes.Select(process => executables.TryGetValue(process.Pid, out var executable) ? process with { Executable = executable } : process)];

    /// <summary>
    /// The lines of <c>ps -o pid=,ppid=,lstart=,command=</c> under the C locale: two numbers, a start written
    /// as five words such as <c>Sun Oct 11 14:04:06 2026</c>, then the command line to the end.
    /// </summary>
    /// <remarks>
    /// Read without splitting: the words are slices of the output, and only the command line becomes a string of
    /// its own. Splitting cost a megabyte for a table of seven hundred processes, measured on 2026-10-12, and on
    /// Linux, where there is no libproc, this is how every census reads the table.
    /// </remarks>
    internal static IReadOnlyList<RunningProcess> Parse(string output)
    {
        var processes = new List<RunningProcess>();
        Span<char> start = stackalloc char[40];

        foreach (var written in output.AsSpan().EnumerateLines())
        {
            var line = written.Trim();

            if (!int.TryParse(Word(ref line), CultureInfo.InvariantCulture, out var pid) || !int.TryParse(Word(ref line), CultureInfo.InvariantCulture, out var parent))
                continue;

            var length = 0;

            for (var index = 0; index < 5; index++)
            {
                var word = Word(ref line);

                if (word.IsEmpty || length + word.Length + 1 > start.Length)
                    break;

                if (length > 0)
                    start[length++] = ' ';

                word.CopyTo(start[length..]);
                length += word.Length;
            }

            var command = line.Trim();

            if (command.IsEmpty || !DateTime.TryParseExact(start[..length], Starts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var started))
                continue;

            processes.Add(new RunningProcess(pid, parent, new DateTimeOffset(started), command.ToString()));
        }

        return processes;
    }

    private static readonly string[] Starts = ["ddd MMM d HH:mm:ss yyyy", "ddd MMM dd HH:mm:ss yyyy"];

    /// <summary>The next word of a line, the line moved past it.</summary>
    private static ReadOnlySpan<char> Word(ref ReadOnlySpan<char> line)
    {
        line = line.TrimStart(' ');

        var end = line.IndexOf(' ');
        var word = end < 0 ? line : line[..end];

        line = end < 0 ? [] : line[end..];

        return word;
    }
}
