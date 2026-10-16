using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Adapters.Trails;
using Snail.MCP.CleanUp.Domain;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Adapters;

/// <summary>
/// The temporary folder of 2026-10-13: 3 939 debugger pipes and diagnostic sockets of each kind, nearly all of processes long
/// ended, one of them under an id the system had since given to Music.
/// </summary>
public sealed class RemainsKindTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_791_920_000);

    private readonly Scratch _scratch = new();

    private readonly FakeProcessTable _table = new();

    private readonly FakeTimeProvider _clock = new(Now);

    public RemainsKindTests() => _table.Add(1, 0, "/sbin/launchd", DateTimeOffset.UnixEpoch);

    public void Dispose() => _scratch.Dispose();

    private RemainsKind Kind => new(_table, new Sizes(_clock), _clock, _scratch.Path);

    /// <summary>The three endpoints a .NET process opens, named by its id and the second it started.</summary>
    private string[] Endpoints(int pid, DateTimeOffset started)
    {
        var start = started.ToUnixTimeSeconds();
        string[] paths = [_scratch.File($"clr-debug-pipe-{pid}-{start}-in"), _scratch.File($"clr-debug-pipe-{pid}-{start}-out"), _scratch.File($"dotnet-diagnostic-{pid}-{start}-socket")];

        foreach (var path in paths)
            File.WriteAllBytes(path, []);

        return paths;
    }

    [Fact]
    public async Task Endpoints_OfProcessesThatEnded_AreOneCache_AndThoseOfARunningOneAreLeftOut()
    {
        Endpoints(10056, Now.AddHours(-20));
        Endpoints(10093, Now.AddHours(-20));
        _table.Add(20716, 1, "dotnet", Now.AddHours(-3));
        Endpoints(20716, Now.AddHours(-3));

        var remains = Assert.Single((await Kind.ObserveAsync([], CancellationToken.None)).Resources);

        Assert.Equal(MachineKinds.Remains, remains.Kind);
        Assert.True(remains.IsShared);
        Assert.True(remains.IsRegenerable);
        Assert.Null(remains.Blocker);
        Assert.Contains("2 .NET process(es) that have ended, 6 file(s)", remains.Title, StringComparison.Ordinal);
        Assert.Contains("1 running process(es) are left alone", remains.Title, StringComparison.Ordinal);
    }

    /// <summary>An id given again is not the process that named its endpoints: the start in the name tells them apart.</summary>
    [Fact]
    public async Task Endpoints_UnderAnIdGivenToAnotherProcessSince_AreOfOneThatEnded()
    {
        var pipes = Endpoints(3455, Now.AddHours(-30));
        _table.Add(3455, 1, "/System/Applications/Music.app/Contents/MacOS/Music", Now.AddHours(-16));

        var remains = Assert.Single((await Kind.ObserveAsync([], CancellationToken.None)).Resources);
        var release = await Kind.ReleaseAsync(remains, CancellationToken.None);

        Assert.Equal(Outcome.Released, release.Outcome);
        Assert.All(pipes, path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public async Task Release_ErasesOnlyTheEndpointsOfProcessesThatEnded_AndNothingElse()
    {
        var ended = Endpoints(10056, Now.AddHours(-20));
        _table.Add(20716, 1, "dotnet", Now.AddHours(-3));
        var running = Endpoints(20716, Now.AddHours(-3));
        var unknown = Endpoints(31904, Now.AddHours(-2));
        _table.Add(31904, 1, "VBCSCompiler", DateTimeOffset.UnixEpoch);
        File.WriteAllBytes(_scratch.File("clr-debug-pipe-notes.txt"), []);
        Directory.CreateDirectory(_scratch.File("MSBuildTemp4DEPIm"));

        var remains = Assert.Single((await Kind.ObserveAsync([], CancellationToken.None)).Resources);
        var release = await Kind.ReleaseAsync(remains, CancellationToken.None);

        Assert.Equal(Outcome.Released, release.Outcome);
        Assert.All(ended, path => Assert.False(File.Exists(path)));
        Assert.All([.. running, .. unknown, _scratch.File("clr-debug-pipe-notes.txt")], path => Assert.True(File.Exists(path)));
        Assert.True(Directory.Exists(_scratch.File("MSBuildTemp4DEPIm")));
    }

    /// <summary>A process started after the plan was shown is judged again: its endpoints are not the plan's.</summary>
    [Fact]
    public async Task Release_JudgesAgainst_TheTableAsItIsWhenItActs()
    {
        var pipes = Endpoints(10056, Now.AddHours(-20));
        var remains = Assert.Single((await Kind.ObserveAsync([], CancellationToken.None)).Resources);

        _table.Add(10056, 1, "dotnet", Now.AddHours(-20));

        Assert.Equal(Outcome.Gone, (await Kind.ReleaseAsync(remains, CancellationToken.None)).Outcome);
        Assert.All(pipes, path => Assert.True(File.Exists(path)));
    }

    /// <summary>The table may have been read a moment before a process started: its endpoints wait until they are older than that.</summary>
    [Fact]
    public async Task Endpoints_OfAProcessStartedAMomentAgo_AreNotJudged()
    {
        Endpoints(40000, Now.AddSeconds(-30));

        Assert.Empty((await Kind.ObserveAsync([], CancellationToken.None)).Resources);
    }

    [Fact]
    public async Task ProcessTable_ThatCannotBeRead_ProvesNothingEnded()
    {
        _table.Running.Clear();
        Endpoints(10056, Now.AddHours(-20));

        var observed = await Kind.ObserveAsync([], CancellationToken.None);

        Assert.Empty(observed.Resources);
        Assert.NotNull(observed.Note);
    }

    /// <summary>The kind accounts for them, so the file trail does not list them and no census writes them down as discoveries.</summary>
    [Fact]
    public async Task Endpoints_AreNotTracesOfTheFileTrail()
    {
        Endpoints(10056, Now.AddHours(-20));
        File.WriteAllBytes(_scratch.File("clr-debug-pipe-100000-in"), []);
        Directory.CreateDirectory(_scratch.File("MSBuildTemp4DEPIm"));

        var traces = await new FileTrail([(_scratch.Path, 1)], new Sizes(_clock)).FollowAsync([], CancellationToken.None);

        Assert.Equal(["MSBuildTemp4DEPIm", "clr-debug-pipe-100000-in"], traces.Select(trace => Path.GetFileName(trace.Key)).Order(StringComparer.Ordinal));
    }

    /// <summary>Nobody's, and rebuilt by every process that starts: removed when caches are asked for, never on ownership.</summary>
    [Fact]
    public async Task Remains_ArePlannedOnlyWithTheCaches()
    {
        Endpoints(10056, Now.AddHours(-20));
        var remains = Assert.Single((await Kind.ObserveAsync([], CancellationToken.None)).Resources);
        var inventory = Inventory([remains]);

        Assert.Empty(Planner.Decide(inventory, Scope.Orphans, "plan", MachineKinds.Order).Decisions);
        Assert.Equal(Verdict.Remove, Assert.Single(Planner.Decide(inventory, Scope.Caches, "plan", MachineKinds.Order).Decisions).Verdict);
    }

    /// <summary>A test run's folder as Snail.Sdk.Mcp.Testing names it, with a file of the given size inside.</summary>
    private string TestRoot(int pid, DateTimeOffset started, int bytes = 1_000)
    {
        var root = Directory.CreateDirectory(_scratch.File($"s3l-test-{pid}-{started.ToUnixTimeSeconds()}-{Guid.NewGuid():n}")).FullName;

        File.WriteAllBytes(Path.Combine(root, "server.log"), new byte[bytes]);

        return root;
    }

    [Fact]
    public async Task TestRunFolders_OfRunsThatEnded_AreOneCacheWithTheirSize_AndARunningOnesIsLeftOut()
    {
        TestRoot(10056, Now.AddHours(-20), 5_000);
        TestRoot(10093, Now.AddHours(-20), 5_000);
        _table.Add(20716, 1, "dotnet testhost", Now.AddHours(-1));
        TestRoot(20716, Now.AddHours(-1));
        Directory.CreateDirectory(_scratch.File("s3l-test-abc-1-x"));
        Directory.CreateDirectory(_scratch.File("s3l-test-10056-1791848000-"));

        var runs = Assert.Single((await Kind.ObserveAsync([], CancellationToken.None)).Resources);

        Assert.Equal(MachineKinds.Remains, runs.Kind);
        Assert.True(runs.IsShared && runs.IsRegenerable);
        Assert.True(runs.Bytes >= 10_000, $"{runs.Bytes} bytes");
        Assert.False(runs.IsAtLeast);
        Assert.Contains("2 test run(s) that have ended", runs.Title, StringComparison.Ordinal);
        Assert.Contains("1 running test run(s) are left alone", runs.Title, StringComparison.Ordinal);
        Assert.Equal(Verdict.Remove, Assert.Single(Planner.Decide(Inventory([runs]), Scope.Caches, "plan", MachineKinds.Order).Decisions).Verdict);
        Assert.Empty(Planner.Decide(Inventory([runs]), Scope.Mine, "plan", MachineKinds.Order).Decisions);
    }

    [Fact]
    public async Task Release_ErasesOnlyTheFoldersOfRunsThatEnded_AndNothingElse()
    {
        var ended = TestRoot(10056, Now.AddHours(-20));
        _table.Add(20716, 1, "dotnet testhost", Now.AddHours(-1));
        var running = TestRoot(20716, Now.AddHours(-1));
        var unknown = TestRoot(31904, Now.AddHours(-2));
        _table.Add(31904, 1, "launchd-child", DateTimeOffset.UnixEpoch);
        var pipes = Endpoints(10056, Now.AddHours(-20));
        var odd = Directory.CreateDirectory(_scratch.File("s3l-test-notes")).FullName;
        var target = Directory.CreateDirectory(_scratch.File("elsewhere")).FullName;
        File.WriteAllBytes(Path.Combine(target, "keep.txt"), [1]);
        Directory.CreateSymbolicLink(_scratch.File($"s3l-test-10093-{Now.AddHours(-20).ToUnixTimeSeconds()}-link"), target);

        var runs = (await Kind.ObserveAsync([], CancellationToken.None)).Resources.Single(resource => resource.Key.EndsWith("s3l-test-*", StringComparison.Ordinal));
        var release = await Kind.ReleaseAsync(runs, CancellationToken.None);

        Assert.Equal(Outcome.Released, release.Outcome);
        Assert.Contains("1 test run(s)", release.Detail, StringComparison.Ordinal);
        Assert.False(Directory.Exists(ended));
        Assert.All([running, unknown, odd, target], path => Assert.True(Directory.Exists(path)));
        Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
        Assert.All(pipes, path => Assert.True(File.Exists(path)));
    }

    /// <summary>A run that began after the plan was shown under an id of one that ended is judged again, and kept.</summary>
    [Fact]
    public async Task Release_OfTestRunFolders_JudgesAgainst_TheTableAsItIsWhenItActs()
    {
        var root = TestRoot(10056, Now.AddHours(-20));
        var runs = Assert.Single((await Kind.ObserveAsync([], CancellationToken.None)).Resources);

        _table.Add(10056, 1, "dotnet testhost", Now.AddHours(-20));

        Assert.Equal(Outcome.Gone, (await Kind.ReleaseAsync(runs, CancellationToken.None)).Outcome);
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public async Task TestRunFolder_OfARunStartedAMomentAgo_IsNotJudged()
    {
        TestRoot(40000, Now.AddSeconds(-30));

        Assert.Empty((await Kind.ObserveAsync([], CancellationToken.None)).Resources);
    }

    [Fact]
    public async Task TestRunFolders_AreNotTracesOfTheFileTrail()
    {
        TestRoot(10056, Now.AddHours(-20));
        Directory.CreateDirectory(_scratch.File("s3l-test-notes"));

        var traces = await new FileTrail([(_scratch.Path, 1)], new Sizes(_clock)).FollowAsync([], CancellationToken.None);

        Assert.Equal(["s3l-test-notes"], traces.Select(trace => Path.GetFileName(trace.Key)));
    }

    /// <summary>
    /// On Linux the runtime keys an endpoint by its process's start in clock ticks since boot, which read as seconds made the
    /// endpoints of a .NET process running for three hours look ended (2026-10-16). Where the keys are not seconds the
    /// endpoints are left alone, and the folders of test runs, named by the Snail convention in seconds, are judged still.
    /// </summary>
    [Fact]
    public async Task Endpoints_WhereTheRuntimeKeysThemByTicks_AreLeftAlone_AndTestRunsAreStillJudged()
    {
        const long ticksSinceBoot = 4_521_337;
        var kind = new RemainsKind(_table, new Sizes(_clock), _clock, _scratch.Path, areEndpointsKeyedBySeconds: false);
        string[] endpoints = [_scratch.File($"dotnet-diagnostic-4242-{ticksSinceBoot}-socket"), _scratch.File($"clr-debug-pipe-4242-{ticksSinceBoot}-in")];

        _table.Add(4242, 1, "dotnet Api.dll", Now.AddHours(-3));

        foreach (var endpoint in endpoints)
            File.WriteAllBytes(endpoint, []);

        var root = TestRoot(10056, Now.AddHours(-20));
        var runs = Assert.Single((await kind.ObserveAsync([], CancellationToken.None)).Resources);

        await kind.ReleaseAsync(runs, CancellationToken.None);

        Assert.All(endpoints, endpoint => Assert.True(File.Exists(endpoint)));
        Assert.False(Directory.Exists(root));
    }
}
