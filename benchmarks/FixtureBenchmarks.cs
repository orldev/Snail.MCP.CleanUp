using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Adapters.Trails;
using Snail.MCP.CleanUp.Application.Ports;
using Snail.MCP.CleanUp.Domain;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Benchmarks;

/// <summary>The hot paths of a census over inputs the benchmark makes itself, so that what they allocate is the code's and not the machine's.</summary>
/// <remarks>
/// The other benchmarks read this Mac, and what they allocate follows what is on it: on 2026-10-16 the file kind allocated
/// 21.7 MB against 2.4 MB in the baseline and the editor kind 728 KB against 1.4 KB, with no change in their code. An
/// allocation that grows here is a change in the code, which is what the baseline of <c>perf.yml</c> is there to catch.
/// </remarks>
[MemoryDiagnoser]
[ShortRunJob]
public class FixtureBenchmarks
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_792_000_000);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"s3l-bench-{Guid.NewGuid():n}");

    private FileTrail _trail = null!;

    private RemainsKind _remains = null!;

    private string _ps = string.Empty;

    private LedgerFile _ledger = null!;

    private SqliteBaselines _baselines = null!;

    private List<string> _traceIds = [];

    private int _owners;

    [GlobalSetup]
    public void Setup()
    {
        var tree = Path.Combine(_root, "tree");
        var temporary = Path.Combine(_root, "tmp");
        var sizes = new Sizes(TimeProvider.System);

        Tree(tree, folders: 200, files: 50);
        Endpoints(temporary, processes: 1_000);

        _trail = new FileTrail([(tree, 2)], sizes);
        _remains = new RemainsKind(new Listed(Running(300)), sizes, new FixedClock(Now), temporary, areEndpointsKeyedBySeconds: true);
        _ps = Ps(lines: 700);
        _ledger = new LedgerFile(Path.Combine(_root, "ledger.db"));
        _baselines = new SqliteBaselines(_ledger);
        _traceIds = [.. Enumerable.Range(0, 39_000).Select(index => $"file:/Users/dev/Library/Caches/tool/{index}")];
    }

    /// <summary>The file trail over ten thousand entries in two hundred folders.</summary>
    [Benchmark]
    public async Task<int> FollowFileTrailAsync() => (await _trail.FollowAsync([], CancellationToken.None)).Count;

    /// <summary>The endpoints of a thousand .NET processes, three hundred of them running.</summary>
    [Benchmark]
    public async Task<int> ObserveRemainsAsync() => (await _remains.ObserveAsync([], CancellationToken.None)).Resources.Count;

    /// <summary>Seven hundred lines of <c>ps</c>, as Linux reads its table.</summary>
    [Benchmark]
    public int ParsePs() => ProcessTable.Parse(_ps).Count;

    /// <summary>A baseline of 39 000 trace ids, what a session began with on 2026-10-16, written as one set.</summary>
    [Benchmark]
    public Task SaveBaselineAsync() =>
        _baselines.SaveAsync(Owner.Session($"bench-{Interlocked.Increment(ref _owners)}"), _traceIds, Now, CancellationToken.None);

    [GlobalCleanup]
    public void Cleanup()
    {
        _ledger.Dispose();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static void Tree(string root, int folders, int files)
    {
        for (var folder = 0; folder < folders; folder++)
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, $"folder-{folder}")).FullName;

            for (var file = 0; file < files; file++)
                File.WriteAllBytes(Path.Combine(directory, $"file-{file}"), []);
        }
    }

    private static void Endpoints(string directory, int processes)
    {
        Directory.CreateDirectory(directory);

        for (var pid = 1_000; pid < 1_000 + processes; pid++)
        {
            var started = StartOf(pid).ToUnixTimeSeconds();

            File.WriteAllBytes(Path.Combine(directory, $"clr-debug-pipe-{pid}-{started}-in"), []);
            File.WriteAllBytes(Path.Combine(directory, $"clr-debug-pipe-{pid}-{started}-out"), []);
            File.WriteAllBytes(Path.Combine(directory, $"dotnet-diagnostic-{pid}-{started}-socket"), []);
        }
    }

    private static IReadOnlyList<RunningProcess> Running(int count) =>
        [.. Enumerable.Range(1_000, count).Select(pid => new RunningProcess(pid, 1, StartOf(pid), "dotnet App.dll"))];

    private static DateTimeOffset StartOf(int pid) => Now.AddHours(-3).AddSeconds(-pid);

    private static string Ps(int lines)
    {
        var text = new StringBuilder();

        for (var pid = 1; pid <= lines; pid++)
        {
            var started = Now.AddSeconds(-pid).LocalDateTime.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture);

            text.Append(CultureInfo.InvariantCulture, $"{pid,6} {pid / 2,6} {started} /usr/local/bin/program --flag value-{pid}\n");
        }

        return text.ToString();
    }

    /// <summary>A process table the benchmark writes.</summary>
    private sealed class Listed(IReadOnlyList<RunningProcess> running) : IProcessTable
    {
        public Task<IReadOnlyList<RunningProcess>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(running);

        public void Refresh()
        {
        }
    }

    /// <summary>A clock that stays at one moment, so that what counts as young does not move between runs.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
