using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Adapters.Trails;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Application.Ports;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Domain;
using Snail.Toolkit.Cleanup.Adapters;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Application.Ports;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Benchmarks;

/// <summary>A whole census over a ledger the size of a busy machine's: the file trail, the baselines and the discoveries, and the disk's history written after it.</summary>
/// <remarks>
/// Everything lives in a directory of the benchmark's own, never in the developer's home: 32 000 entries in a temporary
/// folder, the way a Mac's fills with debugger pipes, and a ledger of 100 000 discoveries — most of them of things gone
/// for over a week, some gone a day, some still there. Beside them, the debugger pipes and diagnostic sockets of 4 000
/// .NET processes that have ended, named as the runtime names them, the way a Mac's temporary folder held 3 939 of each on
/// 2026-10-13, and the temporary folders of 2 000 test runs that have ended, named by the Snail.Sdk convention
/// <c>s3l-test-&lt;pid&gt;-&lt;start&gt;-&lt;random&gt;</c>, and the two places servers write outside their data folders, ONNX
/// Runtime's and NuGet's scratch folder. One session began before the census and has a baseline; the other takes its first one. The first census of a server is what a new session pays; the next is what a running one
/// pays on every change.
/// </remarks>
[MemoryDiagnoser]
[ShortRunJob]
[InvocationCount(1)]
public class CensusBenchmarks
{
    private const int Entries = 32_000;

    private const int NewEntries = 1_000;

    private const int Ended = 4_000;

    private const int TestRuns = 2_000;

    private const int Gone = 90_000;

    private const int GoneLately = 8_000;

    private static readonly Session Began = new(Owner.Session("bench-began"), 1, IsAlive: true);

    /// <summary>The session taking its first baseline; named as Claude Code names a session, so that its scratchpad is its own.</summary>
    private static readonly Session Beginning = new(Owner.Session("5d1b8f0e-0000-4000-8000-00000000be9c"), 2, IsAlive: true);

    private const int Projects = 20;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "snail-cleanup-bench", Guid.NewGuid().ToString("n"));

    private string Folder => Path.Combine(_root, "T");

    private string Seed => Path.Combine(_root, "seed.db");

    private string Ledger => Path.Combine(_root, "data", "ledger.db");

    private LedgerFile _file = null!;

    private MeasuredCensus _census = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        Directory.CreateDirectory(Folder);

        for (var index = 0; index < Entries; index++)
            await File.WriteAllBytesAsync(Path.Combine(Folder, $"clr-debug-pipe-{100_000 + index}-in"), []);

        for (var index = 0; index < Ended; index++)
        {
            foreach (var name in (string[])[$"clr-debug-pipe-{200_000 + index}-1791828291-in", $"clr-debug-pipe-{200_000 + index}-1791828291-out", $"dotnet-diagnostic-{200_000 + index}-1791828291-socket"])
                await File.WriteAllBytesAsync(Path.Combine(Folder, name), []);
        }

        for (var index = 0; index < TestRuns; index++)
        {
            var root = Directory.CreateDirectory(Path.Combine(Folder, $"s3l-test-{300_000 + index}-1791828291-{index:x8}"));

            await File.WriteAllBytesAsync(Path.Combine(root.FullName, "state.json"), new byte[1_024]);
        }

        Scratchpad();

        foreach (var place in OutsideKind.Known(Path.Combine(_root, "home"), Folder))
        {
            Directory.CreateDirectory(place.Path);

            for (var index = 0; index < 20; index++)
                await File.WriteAllBytesAsync(Path.Combine(place.Path, $"blob-{index}"), new byte[4_096]);
        }

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

    private MeasuredCensus Built(string ledger)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ledger)!);

        _file = new LedgerFile(ledger);

        var clock = TimeProvider.System;
        var config = new ServerConfig { DataDirectory = Path.GetDirectoryName(ledger)!, TrashDirectory = Path.Combine(_root, "Trash"), ScratchRoot = Path.Combine(_root, "scratch") };
        var rules = new JsonRules(Path.Combine(_root, "rules.json"), []);
        var trail = new FileTrail([(Folder, 1)], new Sizes(clock));
        var table = new OnlyLaunchd();
        var kind = new FileKind(config, trail, rules, table, new Disposal(config, clock), new Sizes(clock), clock);
        var remains = new RemainsKind(table, new Sizes(clock), clock, Folder);
        var outside = new OutsideKind(new Sizes(clock), OutsideKind.Known(Path.Combine(_root, "home"), Folder));

        var census = new Census([kind, remains, outside], [trail], new Alive(), new SqliteLedger(_file), rules, new SqliteBaselines(_file), new SqliteDiscoveries(_file), clock);
        var trend = new DiskTrend(new HomeVolume(_root), new SqliteDiskHistory(_file), config, clock);

        return new MeasuredCensus(census, new Performance(clock), trend);
    }

    /// <summary>
    /// The scratchpad of the running session as 2026-10-13 had one, in small: copies of a repository of <see cref="Projects"/>
    /// projects with their bin and obj, test results, benchmark artifacts and a site's node_modules, beside a Python
    /// environment that is no output — all last written three hours ago.
    /// </summary>
    private void Scratchpad()
    {
        var scratchpad = Path.Combine(_root, "scratch", "claude-501", "-bench-project", Beginning.Id);
        var then = DateTime.UtcNow.AddHours(-3);

        for (var project = 0; project < Projects; project++)
        {
            var folder = Path.Combine(scratchpad, "copy", "src", $"Project{project}");

            Filled(folder, 1, "csproj");
            Filled(Path.Combine(folder, "bin", "Debug", "net10.0"), 30, "dll");
            Filled(Path.Combine(folder, "obj", "Debug", "net10.0"), 30, "cache");
        }

        Filled(Path.Combine(scratchpad, "copy", "tests", "TestResults", "run"), 10, "trx");
        Filled(Path.Combine(scratchpad, "copy", "benchmarks", "BenchmarkDotNet.Artifacts", "results"), 10, "csv");
        Filled(Path.Combine(scratchpad, "site"), 1, "json", "package");
        Filled(Path.Combine(scratchpad, "site", "node_modules", "left-pad"), 100, "js");
        Filled(Path.Combine(scratchpad, "site-venv"), 1, "cfg", "pyvenv");
        Filled(Path.Combine(scratchpad, "site-venv", "lib", "python3.13", "site-packages"), 200, "py");

        foreach (var entry in Directory.EnumerateFileSystemEntries(scratchpad, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(entry, then);

        static void Filled(string folder, int files, string extension, string? name = null)
        {
            Directory.CreateDirectory(folder);

            for (var index = 0; index < files; index++)
                File.WriteAllBytes(Path.Combine(folder, $"{name ?? Path.GetFileName(folder)}{(name is null ? index : string.Empty)}.{extension}"), new byte[512]);
        }
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

    private sealed class Alive : IHolders
    {
        public Task<IReadOnlyList<Holder>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Holder>>([Began, Beginning]);

        public Task<Owner> CurrentAsync(CancellationToken cancellationToken) => Task.FromResult(Began.Owner);
    }

    /// <summary>A machine where nothing runs but launchd: every process the endpoints name has ended.</summary>
    private sealed class OnlyLaunchd : IProcessTable
    {
        public Task<IReadOnlyList<RunningProcess>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RunningProcess>>([new RunningProcess(1, 0, DateTimeOffset.UnixEpoch, "/sbin/launchd")]);

        public void Refresh()
        {
        }
    }
}
