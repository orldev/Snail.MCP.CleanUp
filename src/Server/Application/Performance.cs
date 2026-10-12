using System.Collections.Concurrent;
using System.Diagnostics;
using Snail.MCP.CleanUp.Core.Application;

namespace Snail.MCP.CleanUp.Application;

/// <summary>What the server spends: how long a census takes and on what, which programs it starts, what it allocates.</summary>
/// <remarks>
/// Every optimisation of this server is judged by these numbers, before and after, on the same machine.
/// The counts are of the whole process: a census and a tool call running at the same moment share them,
/// which is why each census also carries the timings of its own kinds and trails, measured inside it.
/// </remarks>
public sealed class Performance(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, Spent> _programs = new(StringComparer.Ordinal);

    private readonly long _started = clock.GetTimestamp();

    private CensusRecord? _last;

    private int _censuses;

    /// <summary>What running programs has cost so far, by program and its first command word.</summary>
    public readonly record struct Spent(int Runs, TimeSpan Time);

    /// <summary>One census as it was measured.</summary>
    public sealed record CensusRecord(
        DateTimeOffset At,
        TimeSpan Duration,
        IReadOnlyDictionary<string, TimeSpan> Timings,
        IReadOnlyDictionary<string, Spent> Programs,
        long AllocatedBytes,
        int Gen0,
        int Gen1,
        int Gen2);

    /// <summary>Writes down one run of an outside program.</summary>
    public void Ran(string program, IReadOnlyList<string> arguments, TimeSpan time)
    {
        var words = arguments.TakeWhile(argument => !argument.StartsWith('-')).Take(2);
        var name = string.Join(' ', new[] { Path.GetFileName(program) }.Concat(words));

        _programs.AddOrUpdate(name, new Spent(1, time), (_, spent) => new Spent(spent.Runs + 1, spent.Time + time));
    }

    /// <summary>How many times a program and its first command words were run since the process started.</summary>
    public int Runs(string name) => _programs.TryGetValue(name, out var spent) ? spent.Runs : 0;

    /// <summary>Takes a census and keeps what it cost.</summary>
    public async Task<Inventory> MeasureAsync(Func<Task<Inventory>> census)
    {
        var programs = _programs.ToDictionary(StringComparer.Ordinal);
        var allocated = GC.GetTotalAllocatedBytes();
        var (gen0, gen1, gen2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var started = clock.GetTimestamp();

        var inventory = await census();

        _last = new CensusRecord(
            clock.GetUtcNow(),
            clock.GetElapsedTime(started),
            inventory.Timings,
            Delta(programs),
            GC.GetTotalAllocatedBytes() - allocated,
            GC.CollectionCount(0) - gen0,
            GC.CollectionCount(1) - gen1,
            GC.CollectionCount(2) - gen2);
        Interlocked.Increment(ref _censuses);

        return inventory;
    }

    /// <summary>The numbers as an answer: the last census and the process as a whole.</summary>
    public JsonObject Report()
    {
        var memory = GC.GetGCMemoryInfo();
        using var process = Process.GetCurrentProcess();
        var report = new JsonObject
        {
            ["censuses"] = _censuses,
            ["lastCensus"] = _last is { } last ? Shown(last) : null,
            ["process"] = new JsonObject
            {
                ["uptimeSeconds"] = (int)clock.GetElapsedTime(_started).TotalSeconds,
                ["workingSetMB"] = Megabytes(process.WorkingSet64),
                ["gcHeapMB"] = Megabytes(memory.HeapSizeBytes),
                ["gcCommittedMB"] = Megabytes(memory.TotalCommittedBytes),
                ["totalAllocatedMB"] = Megabytes(GC.GetTotalAllocatedBytes()),
                ["gen0"] = GC.CollectionCount(0),
                ["gen1"] = GC.CollectionCount(1),
                ["gen2"] = GC.CollectionCount(2),
                ["threads"] = process.Threads.Count,
                ["assemblies"] = AppDomain.CurrentDomain.GetAssemblies().Length
            }
        };

        return report;
    }

    private Dictionary<string, Spent> Delta(Dictionary<string, Spent> before) =>
        _programs
            .Select(now => (now.Key, Spent: before.TryGetValue(now.Key, out var then) ? new Spent(now.Value.Runs - then.Runs, now.Value.Time - then.Time) : now.Value))
            .Where(entry => entry.Spent.Runs > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Spent, StringComparer.Ordinal);

    private static JsonObject Shown(CensusRecord census) => new()
    {
        ["at"] = census.At.ToString("u", System.Globalization.CultureInfo.InvariantCulture),
        ["durationMs"] = (int)census.Duration.TotalMilliseconds,
        ["timingsMs"] = new JsonObject([.. census.Timings.OrderByDescending(timing => timing.Value).Select(timing => KeyValuePair.Create(timing.Key, (JsonNode?)(int)timing.Value.TotalMilliseconds))]),
        ["programRuns"] = census.Programs.Values.Sum(spent => spent.Runs),
        ["programs"] = new JsonObject([.. census.Programs.OrderByDescending(program => program.Value.Runs).Select(program => KeyValuePair.Create(program.Key, (JsonNode?)new JsonObject { ["runs"] = program.Value.Runs, ["ms"] = (int)program.Value.Time.TotalMilliseconds }))]),
        ["allocatedMB"] = Megabytes(census.AllocatedBytes),
        ["collections"] = new JsonObject { ["gen0"] = census.Gen0, ["gen1"] = census.Gen1, ["gen2"] = census.Gen2 }
    };

    private static double Megabytes(long bytes) => Math.Round(bytes / 1_048_576.0, 1);
}

/// <summary>The census as the server takes it: the core's census, with what it cost written down.</summary>
public sealed class MeasuredCensus(Census census, Performance performance)
{
    /// <summary>The inventory of this moment, measured.</summary>
    public Task<Inventory> TakeAsync(Owner asker, CancellationToken cancellationToken) =>
        performance.MeasureAsync(() => census.TakeAsync(asker, cancellationToken));
}
