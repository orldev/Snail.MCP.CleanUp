using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.Toolkit.Cleanup.Application.Ports;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Benchmarks;

/// <summary>Each kind and each trail looking at this Mac on its own: what it costs in time and in allocations.</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class LookBenchmarks
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), "snail-cleanup-bench", Guid.NewGuid().ToString("n"));

    private IHost _host = null!;

    private IReadOnlyList<Holder> _sessions = [];

    private IReadOnlyList<Rule> _rules = [];

    [ParamsSource(nameof(Names))]
    public string Name { get; set; } = string.Empty;

    public static IEnumerable<string> Names()
    {
        using var host = Machine.Built(Path.Combine(Path.GetTempPath(), "snail-cleanup-bench-names"));

        return
        [
            .. host.Services.GetServices<IResourceKind>().Select(kind => $"kind:{kind.Name}"),
            .. host.Services.GetServices<ITrail>().Select(trail => $"trail:{trail.Name}")
        ];
    }

    [GlobalSetup]
    public async Task Setup()
    {
        _host = Machine.Built(_data);
        _sessions = await _host.Services.GetRequiredService<IHolders>().ListAsync(CancellationToken.None);
        _rules = await _host.Services.GetRequiredService<IRules>().ListAsync(CancellationToken.None);
    }

    [Benchmark]
    public async Task<int> LookAsync()
    {
        if (Name.StartsWith("kind:", StringComparison.Ordinal))
        {
            var kind = _host.Services.GetServices<IResourceKind>().First(candidate => $"kind:{candidate.Name}" == Name);

            return (await kind.ObserveAsync(_sessions, CancellationToken.None)).Resources.Count;
        }

        var trail = _host.Services.GetServices<ITrail>().First(candidate => $"trail:{candidate.Name}" == Name);

        return (await trail.FollowAsync(_rules, CancellationToken.None)).Count;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _host.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (Directory.Exists(_data))
            Directory.Delete(_data, recursive: true);
    }
}
