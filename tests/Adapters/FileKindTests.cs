using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Adapters.Trails;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Domain;
using Snail.Toolkit.Cleanup.Adapters;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class FileKindTests : IDisposable
{
    private const string SessionId = "2647fa9c-8ea1-4ee7-a1f0-7053be900dd8";

    private readonly Scratch _scratch = new();

    private readonly FakeProcessTable _table = new();

    private ServerConfig Config => new() { ScratchRoot = _scratch.Path, DataDirectory = _scratch.File("data"), TrashDirectory = _scratch.File("Trash") };

    private FileKind Kind(params Rule[] rules) => new(
        Config,
        new FileTrail([(_scratch.File("apps"), 1)], new Sizes(TimeProvider.System)),
        new JsonRules(_scratch.File("rules.json"), rules),
        _table,
        new Disposal(Config, new FakeTimeProvider(DateTimeOffset.UnixEpoch)),
        new Sizes(_clock),
        _clock);

    private FakeTimeProvider _clock = new(DateTimeOffset.UtcNow.AddDays(1));

    private string Scratchpad()
    {
        var path = _scratch.File($"claude-501/-Users-dev-project/{SessionId}");

        Directory.CreateDirectory(Path.Combine(path, "scratchpad"));

        return path;
    }

    [Fact]
    public async Task Scratchpad_IsItsSessions_ByItsName()
    {
        var path = Scratchpad();

        var scratchpad = Assert.Single((await Kind().ObserveAsync([], CancellationToken.None)).Resources);

        Assert.Equal(path, scratchpad.Key);
        Assert.Equal(Owner.Session(SessionId), scratchpad.Owner);
        Assert.Equal(Evidence.Label, scratchpad.Evidence);
        Assert.Null(scratchpad.Blocker);
    }

    /// <summary>A session SleepOf never heard of goes by another name; its scratchpad, written a minute ago, is not an orphan.</summary>
    [Fact]
    public async Task Scratchpad_WrittenInTheLastHour_IsNotToBeReleased_WhateverTheSessionsSay()
    {
        Scratchpad();
        _clock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.NotNull(Assert.Single((await Kind().ObserveAsync([], CancellationToken.None)).Resources).Blocker);
    }

    [Fact]
    public async Task Scratchpad_OfARunningSession_IsNotToBeReleased()
    {
        Scratchpad();

        var running = new Session(Owner.Session(SessionId), 1, IsAlive: true) { StartedAt = DateTimeOffset.UnixEpoch, Directory = "/work" };
        var scratchpad = Assert.Single((await Kind().ObserveAsync([running], CancellationToken.None)).Resources);

        Assert.NotNull(scratchpad.Blocker);
    }

    [Fact]
    public async Task Scratchpad_Released_GoesToTheTrash()
    {
        var path = Scratchpad();
        var kind = Kind();

        var release = await kind.ReleaseAsync(Assert.Single((await kind.ObserveAsync([], CancellationToken.None)).Resources), CancellationToken.None);

        Assert.Equal(Outcome.Released, release.Outcome);
        Assert.False(Directory.Exists(path));
        Assert.True(Directory.Exists(_scratch.File($"Trash/{SessionId}")));
    }

    /// <summary>VS Code holds its webview caches open; a cache rule names the program that blocks it.</summary>
    [Fact]
    public async Task CacheRule_MakesSharedRegenerableResources_BlockedWhileTheirProgramRuns()
    {
        Directory.CreateDirectory(_scratch.File("apps/WebStorage/11"));
        _table.Add(9, 1, "/Applications/Visual Studio Code.app/Contents/MacOS/Code", executable: "/Applications/Visual Studio Code.app/Contents/MacOS/Code");

        var rule = new Rule("file", $"{_scratch.Path}/apps/WebStorage/*", RuleAction.Cache) { BlockedWhileRunning = "Code" };
        var cache = Assert.Single((await Kind(rule).ObserveAsync([], CancellationToken.None)).Resources);

        Assert.True(cache.IsShared);
        Assert.True(cache.IsRegenerable);
        Assert.Equal("Code is running and keeps it open", cache.Blocker);
    }

    [Fact]
    public async Task IgnoreRule_MakesNoResource()
    {
        Directory.CreateDirectory(_scratch.File("apps/Noise"));

        Assert.Empty((await Kind(new Rule("file", $"{_scratch.Path}/apps/*", RuleAction.Ignore)).ObserveAsync([], CancellationToken.None)).Resources);
    }

    /// <summary>The kind looks by walking the file trail, and hands the walk to the census, which then does not walk it again.</summary>
    [Fact]
    public async Task Observation_CarriesTheWalkOfTheFileTrail()
    {
        Directory.CreateDirectory(_scratch.File("apps/NewTool"));

        var followed = (await Kind().ObserveAsync([], CancellationToken.None)).Followed;

        Assert.NotNull(followed);
        Assert.Equal("file", followed.Trail);
        Assert.Contains(followed.Traces, trace => trace.Key == _scratch.File("apps/NewTool"));
    }

    private static Session Running => new(Owner.Session(SessionId), 1, IsAlive: true) { StartedAt = DateTimeOffset.UnixEpoch, Directory = "/work" };

    /// <summary>A folder with a file in it, both made now — a day before the test's clock.</summary>
    private static string Made(string folder, string file = "blob")
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, file), new byte[2_048]);

        return folder;
    }

    /// <summary>The scratchpad of 2026-10-13, in small: copies of repositories with their build trees, a venv, a folder of tools.</summary>
    private (string Bin, string Obj, string Results, string Artifacts, string Modules) Repositories(string scratchpad)
    {
        var server = Path.Combine(scratchpad, "scratchpad/copy/src/Server");

        Made(server, "Server.csproj");

        var outputs = (
            Made(Path.Combine(server, "bin/Release/net10.0")).Replace("/Release/net10.0", string.Empty, StringComparison.Ordinal),
            Made(Path.Combine(server, "obj/Debug")).Replace("/Debug", string.Empty, StringComparison.Ordinal),
            Made(Path.Combine(scratchpad, "scratchpad/copy/tests/TestResults/run")).Replace("/run", string.Empty, StringComparison.Ordinal),
            Made(Path.Combine(scratchpad, "scratchpad/copy/benchmarks/BenchmarkDotNet.Artifacts/results")).Replace("/results", string.Empty, StringComparison.Ordinal),
            Made(Path.Combine(scratchpad, "scratchpad/site/node_modules/left-pad")).Replace("/left-pad", string.Empty, StringComparison.Ordinal));

        Made(Path.Combine(scratchpad, "scratchpad/site"), "package.json");
        Made(Path.Combine(scratchpad, "scratchpad/site-venv"), "pyvenv.cfg");
        Made(Path.Combine(scratchpad, "scratchpad/site-venv/lib/node_modules/x"));
        Made(Path.Combine(scratchpad, "scratchpad/site-venv/TestResults"));
        Made(Path.Combine(scratchpad, "scratchpad/tools/.store/some-tool"));
        Made(Path.Combine(scratchpad, "scratchpad/tools/TestResults"));
        Made(Path.Combine(scratchpad, "scratchpad/scripts/bin"));
        Made(Path.Combine(scratchpad, "scratchpad/.hidden/TestResults"));
        Made(Path.Combine(scratchpad, "scratchpad/global/lib/node_modules/some-cli"));

        return outputs;
    }

    /// <summary>A running session's scratchpad stays; what a build makes again inside it is offered to that session alone.</summary>
    [Fact]
    public async Task Outputs_InARunningSessionsScratchpad_AreItsOwn_RebuiltByTheNextBuild_VenvsAndToolsAreNot()
    {
        var scratchpad = Scratchpad();
        var outputs = Repositories(scratchpad);

        var found = (await Kind().ObserveAsync([Running], CancellationToken.None)).Resources;
        var offered = found.Where(resource => resource.Facts.ContainsKey(FileKind.OutputFact)).ToList();

        Assert.Equal(
            new[] { outputs.Artifacts, outputs.Bin, outputs.Modules, outputs.Obj, outputs.Results }.Order(StringComparer.Ordinal),
            offered.Select(resource => resource.Key).Order(StringComparer.Ordinal));
        Assert.All(offered, resource =>
        {
            Assert.Equal((Owner.Session(SessionId), Evidence.Label), (resource.Owner, resource.Evidence));
            Assert.True(resource.IsRegenerable && !resource.IsShared);
            Assert.Null(resource.Blocker);
            Assert.True(resource.Bytes > 0);
        });
        Assert.NotNull(found.Single(resource => resource.Key == scratchpad).Blocker);
    }

    [Fact]
    public async Task Outputs_ArePlannedForTheirSessionAlone_AndNeverForAnother()
    {
        var scratchpad = Scratchpad();
        Repositories(scratchpad);

        var found = (await Kind().ObserveAsync([Running], CancellationToken.None)).Resources;
        var mine = new Inventory(DateTimeOffset.UnixEpoch, found, [Running]) { Asker = Owner.Session(SessionId) };
        var other = mine with { Asker = Owner.Session("another") };

        Assert.Equal(5, Planner.Decide(mine, Scope.Mine, "plan", MachineKinds.Order).Removals.Count);
        Assert.DoesNotContain(Planner.Decide(mine, Scope.Mine, "plan", MachineKinds.Order).Removals, decision => decision.Resource.Key == scratchpad);
        Assert.Empty(Planner.Decide(other, Scope.Everything, "plan", MachineKinds.Order).Removals);
        Assert.Empty(Planner.Decide(other, Scope.Caches, "plan", MachineKinds.Order).Decisions);
    }

    [Fact]
    public async Task Outputs_OfAnEndedSession_AreNotListedApart_ItsScratchpadGoesWhole()
    {
        Repositories(Scratchpad());

        Assert.Single((await Kind().ObserveAsync([Running with { IsAlive = false }], CancellationToken.None)).Resources);
    }

    [Fact]
    public async Task Output_WrittenWithinTwoHours_OrRunFrom_IsLeft()
    {
        var outputs = Repositories(Scratchpad());
        File.SetLastWriteTimeUtc(Path.Combine(outputs.Obj, "Debug/blob"), _clock.GetUtcNow().UtcDateTime.AddMinutes(-30));
        _table.Add(700, 1, $"dotnet {outputs.Bin}/Release/net10.0/Server.dll");

        var offered = (await Kind().ObserveAsync([Running], CancellationToken.None)).Resources.ToDictionary(resource => resource.Key);

        Assert.Contains("30 minutes ago", offered[outputs.Obj].Blocker, StringComparison.Ordinal);
        Assert.Contains("(700) runs from it", offered[outputs.Bin].Blocker, StringComparison.Ordinal);
        Assert.Null(offered[outputs.Results].Blocker);
    }

    /// <summary>A build begun after the plan was shown: the output is judged again before it goes, and stays.</summary>
    [Fact]
    public async Task Output_Released_IsErased_UnlessABuildWroteIntoItSinceThePlan()
    {
        var outputs = Repositories(Scratchpad());
        var kind = Kind();
        var offered = (await kind.ObserveAsync([Running], CancellationToken.None)).Resources.ToDictionary(resource => resource.Key);

        File.SetLastWriteTimeUtc(Path.Combine(outputs.Obj, "Debug/blob"), _clock.GetUtcNow().UtcDateTime.AddMinutes(-1));

        Assert.Equal(Outcome.Changed, (await kind.ReleaseAsync(offered[outputs.Obj], CancellationToken.None)).Outcome);
        Assert.True(Directory.Exists(outputs.Obj));
        Assert.Equal(Outcome.Released, (await kind.ReleaseAsync(offered[outputs.Bin], CancellationToken.None)).Outcome);
        Assert.False(Directory.Exists(outputs.Bin));
        Assert.False(Directory.Exists(_scratch.File("Trash/bin")));
    }

    public void Dispose() => _scratch.Dispose();
}
