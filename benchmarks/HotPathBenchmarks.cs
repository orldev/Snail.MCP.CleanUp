using BenchmarkDotNet.Attributes;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Trails;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Domain;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace Snail.MCP.CleanUp.Benchmarks;

/// <summary>The functions a census runs thousands of times: reading ps and lsof, matching rules, deciding a plan.</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class HotPathBenchmarks
{
    private string _ps = string.Empty;

    private string _lsof = string.Empty;

    private IReadOnlyList<Trace> _traces = [];

    private IReadOnlyList<Rule> _rules = [];

    private Inventory _inventory = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _ps = Run("ps", "-axww", "-o", "pid=,ppid=,lstart=,command=");
        _lsof = Run("lsof", "-nP", "-iTCP", "-sTCP:LISTEN", "-Fpcn");
        _rules = MacPlaces.Shipped;
        _traces = await new FileTrail(new Sizes(TimeProvider.System)).FollowAsync(_rules, CancellationToken.None);

        var owner = Owner.Session("bench");
        List<Resource> resources = [.. Enumerable.Range(0, 5_000).Select(index => new Resource
        {
            Kind = index % 3 == 0 ? Kinds.Process : Kinds.File,
            Key = $"{index}",
            Title = $"resource {index}",
            Owner = index % 2 == 0 ? owner : Owner.Session($"gone-{index % 7}"),
            Evidence = (Evidence)(index % 5),
            After = index % 11 == 0 ? [$"{Kinds.Process}:{index - 1}"] : []
        })];

        _inventory = new Inventory(DateTimeOffset.UtcNow, owner, resources, [new Session("bench", 1, null, null, true)], new Dictionary<string, string>(), [], new Dictionary<string, string>());
    }

    [Benchmark]
    public int ParsePs() => ProcessTable.Parse(_ps).Count;

    /// <summary>The whole process table read from the kernel, as a census reads it on macOS.</summary>
    [Benchmark]
    public int ReadProcessTable() => Adapters.Native.LibProc.List().Count;

    [Benchmark]
    public int ParseLsof() => PortTrail.Parse(_lsof).Count;

    [Benchmark]
    public int MatchRules() => _traces.Count(trace => _rules.Any(rule => rule.Matches(trace)));

    [Benchmark]
    public int DecidePlan() => Planner.Decide(_inventory, Scope.Everything, "bench").Decisions.Count;

    private static string Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program, arguments) { RedirectStandardOutput = true };

        start.Environment["LC_ALL"] = "C";

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();

        process.WaitForExit();

        return output;
    }
}
