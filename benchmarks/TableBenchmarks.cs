using System.Buffers;
using System.Collections.Frozen;
using BenchmarkDotNet.Attributes;
using Snail.MCP.CleanUp.Adapters.Native;
using Snail.MCP.CleanUp.Core.Domain;

namespace Snail.MCP.CleanUp.Benchmarks;

/// <summary>Whether frozen collections and SearchValues pay for themselves on the small fixed tables of a census.</summary>
[MemoryDiagnoser]
public class TableBenchmarks
{
    private static readonly string[] Places =
        ["/Applications/", "/System/", "/usr/libexec/", "/usr/sbin/", "/sbin/", "/Library/Apple/", "/Library/Application Support/", "/usr/bin/"];

    private static readonly string[] ShellNames = ["zsh", "bash", "sh", "dash", "fish", "-zsh", "-bash"];

    private readonly SearchValues<string> _placeValues = SearchValues.Create(Places, StringComparison.Ordinal);

    private readonly HashSet<string> _shells = new(ShellNames, StringComparer.Ordinal);

    private readonly FrozenSet<string> _frozenShells = ShellNames.ToFrozenSet(StringComparer.Ordinal);

    private readonly FrozenDictionary<string, int> _ranks = Kinds.Order.Select((kind, position) => (kind, position)).ToFrozenDictionary(pair => pair.kind, pair => pair.position, StringComparer.Ordinal);

    private string[] _commands = [];

    private string[] _programs = [];

    private string[] _kinds = [];

    [GlobalSetup]
    public async Task Setup()
    {
        var running = await new NativeProcessTable(TimeProvider.System).ListAsync(CancellationToken.None);

        _commands = [.. running.Select(process => process.Command)];
        _programs = [.. running.Select(process => process.Program)];
        _kinds = [.. Enumerable.Range(0, 5_000).Select(index => Kinds.Order[index % Kinds.Order.Count])];
    }

    [Benchmark(Baseline = true)]
    public int PlacesByStartsWith() => _commands.Count(command => Places.Any(place => command.StartsWith(place, StringComparison.Ordinal)));

    [Benchmark]
    public int PlacesBySearchValues() => _commands.Count(command => command.AsSpan().IndexOfAny(_placeValues) == 0);

    [Benchmark]
    public int ShellsInHashSet() => _programs.Count(_shells.Contains);

    [Benchmark]
    public int ShellsInFrozenSet() => _programs.Count(_frozenShells.Contains);

    [Benchmark]
    public int RankByIndexOf() => _kinds.Sum(Kinds.Rank);

    [Benchmark]
    public int RankByFrozenDictionary() => _kinds.Sum(kind => _ranks.TryGetValue(kind, out var rank) ? rank : Kinds.Order.Count);

    [Benchmark]
    public int BuildHashSet() => new HashSet<string>(ShellNames, StringComparer.Ordinal).Count;

    [Benchmark]
    public int BuildFrozenSet() => ShellNames.ToFrozenSet(StringComparer.Ordinal).Count;

    [Benchmark]
    public int BuildSearchValues() => SearchValues.Create(Places, StringComparison.Ordinal).GetHashCode();
}
