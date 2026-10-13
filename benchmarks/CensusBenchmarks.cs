using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Adapters.Trails;
using Snail.MCP.CleanUp.Application.Ports;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Core.Adapters;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Application.Ports;
using Snail.MCP.CleanUp.Core.Domain;
using Snail.MCP.CleanUp.Domain;

namespace Snail.MCP.CleanUp.Benchmarks;

/// <summary>A whole census over a ledger the size of a busy machine's: the file trail, the baselines and the discoveries.</summary>
/// <remarks>
/// Everything lives in a directory of the benchmark's own, never in the developer's home: 32 000 entries in a temporary
/// folder, the way a Mac's fills with debugger pipes, and a ledger of 100 000 discoveries — most of them of things gone
/// for over a week, some gone a day, some still there. One session began before the census and has a baseline; the
/// other takes its first one. The first census of a server is what a new session pays; the next is what a running one
/// pays on every change.
/// </remarks>
[MemoryDiagnoser]
[ShortRunJob]
[InvocationCount(1)]
public class CensusBenchmarks
{
    private const int Entries = 32_000;

    private const int NewEntries = 1_000;

    private const int Gone = 90_000;

    private const int GoneLately = 8_000;

    private static readonly Session Began = new("bench-began", 1, null, null, IsAlive: true);

    private static readonly Session Beginning = new("bench-beginning", 2, null, null, IsAlive: true);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "snail-cleanup-bench", Guid.NewGuid().ToString("n"));

    private string Folder => Path.Combine(_root, "T");

    private string Seed => Path.Combine(_root, "seed.db");

    private string Ledger => Path.Combine(_root, "data", "ledger.db");

    private LedgerFile _file = null!;

    private Census _census = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        Directory.CreateDirectory(Folder);

        for (var index = 0; index < Entries; index++)
            await File.WriteAllBytesAsync(Path.Combine(Folder, $"clr-debug-pipe-{100_000 + index}-in"), []);

        await SeededAsync();

        Reseed();
        await _census.TakeAsync(Began.Owner, CancellationToken.None);
    }

    [IterationSetup(Target = nameof(FirstCensusAsync))]
    public void Reseed()
    {
        _file?.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.CreateDirectory(Path.GetDirectoryName(Ledger)!);

        foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(Ledger)!, "ledger.db*"))
            File.Delete(file);

        File.Copy(Seed, Ledger);
        _census = Built(Ledger);
    }

    /// <summary>The census a server takes when it starts: one session's baseline to take, the record as a week left it.</summary>
    [Benchmark]
    public async Task<int> FirstCensusAsync() => (await _census.TakeAsync(Began.Owner, CancellationToken.None)).Discoveries.Count;

    /// <summary>The census a running server takes when something changed: baselines read, the record already settled.</summary>
    [Benchmark]
    public async Task<int> NextCensusAsync() => (await _census.TakeAsync(Began.Owner, CancellationToken.None)).Discoveries.Count;

    [GlobalCleanup]
    public void Cleanup()
    {
        _file.Dispose();
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private Census Built(string ledger)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ledger)!);

        _file = new LedgerFile(ledger);

        var clock = TimeProvider.System;
        var config = new ServerConfig { DataDirectory = Path.GetDirectoryName(ledger)!, TrashDirectory = Path.Combine(_root, "Trash"), ScratchRoot = Path.Combine(_root, "scratch") };
        var rules = new JsonRules(Path.Combine(_root, "rules.json"), []);
        var trail = new FileTrail([(Folder, 1)], new Sizes(clock));
        var kind = new FileKind(config, trail, rules, new NoProcesses(), new Disposal(config, clock), new Sizes(clock), clock);

        return new Census([kind], [trail], new Alive(), new SqliteLedger(_file), rules, new SqliteBaselines(_file), new SqliteDiscoveries(_file), clock);
    }

    /// <summary>The ledger a week of sessions leaves: a baseline without the newest entries, and discoveries of every age.</summary>
    private async Task SeededAsync()
    {
        using var file = new LedgerFile(Seed);
        var now = DateTimeOffset.UtcNow;
        var paths = Enumerable.Range(0, Entries).Select(index => $"file:{Path.Combine(Folder, $"clr-debug-pipe-{100_000 + index}-in")}").ToList();

        await new SqliteBaselines(file).SaveAsync(Began.Owner, [.. paths.Take(Entries - NewEntries)], now.AddHours(-1), CancellationToken.None);

        List<Discovery> written =
        [
            .. Enumerable.Range(0, Gone).Select(index => Written($"/var/folders/xx/T/clr-debug-pipe-{index}-in", now.AddDays(-10))),
            .. Enumerable.Range(0, GoneLately).Select(index => Written($"/var/folders/xx/T/dotnet-diagnostic-{index}-socket", now.AddDays(-1))),
            .. Enumerable.Range(Entries - NewEntries, NewEntries / 2).Select(index => Written(Path.Combine(Folder, $"clr-debug-pipe-{100_000 + index}-in"), now.AddDays(-10)))
        ];

        await new SqliteDiscoveries(file).NoteAsync(written, CancellationToken.None);

        await using var connection = await file.OpenAsync(CancellationToken.None);
        await using var checkpoint = connection.CreateCommand();

        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await checkpoint.ExecuteNonQueryAsync(CancellationToken.None);
        await connection.CloseAsync();
        SqliteConnection.ClearAllPools();
    }

    private static Discovery Written(string path, DateTimeOffset seen) => new(new Trace("file", path, path), Began.Owner, seen, seen, 3);

    private sealed class Alive : ISessions
    {
        public Task<IReadOnlyList<Session>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Session>>([Began, Beginning]);

        public Task<Owner> CurrentAsync(CancellationToken cancellationToken) => Task.FromResult(Began.Owner);
    }

    private sealed class NoProcesses : IProcessTable
    {
        public Task<IReadOnlyList<RunningProcess>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RunningProcess>>([]);

        public void Refresh()
        {
        }
    }
}
