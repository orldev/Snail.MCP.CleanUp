using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Adapters;

/// <summary>The repository of 2026-10-13: a hang dump of a test run a month old in <c>tests/Scraping.Tests/TestResults</c>.</summary>
public sealed class TestResultsKindTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private FakeTimeProvider _clock = new(DateTimeOffset.UtcNow.AddDays(2));

    public void Dispose() => _scratch.Dispose();

    private ServerConfig Config => new()
    {
        ScratchRoot = _scratch.File("tmp"),
        DataDirectory = _scratch.File("data"),
        TrashDirectory = _scratch.File("Trash"),
        EditorUserDirectory = _scratch.File("Code/User")
    };

    private TestResultsKind Kind => new(Config, new Disposal(Config, _clock), new Sizes(_clock), _clock);

    private string Repository(string name)
    {
        var path = _scratch.File($"Projects/Программирование/Toolkit/{name}");

        Directory.CreateDirectory(Path.Combine(path, ".git"));

        return path;
    }

    private static string Dump(string folder, int kilobytes = 64)
    {
        var path = Path.Combine(folder, "53797c18-2cb5-4f49-b2fe-55a51016f1d2", "dotnet_74121_20260915T002103_hangdump.dmp");
        var bytes = new byte[kilobytes * 1024];

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Random.Shared.NextBytes(bytes);
        File.WriteAllBytes(path, bytes);

        return path;
    }

    private static Session WorkingIn(string directory) => new("s1", 1, DateTimeOffset.UnixEpoch, directory, IsAlive: false);

    [Fact]
    public async Task TestResults_OfARepositoryASessionWorkedIn_AreACacheOfIt_WithTheirSize()
    {
        var repository = Repository("Snail.Toolkit.Web");
        var folder = Path.Combine(repository, "tests", "Scraping.Tests", "TestResults");
        Dump(folder);

        var results = Assert.Single((await Kind.ObserveAsync([WorkingIn(repository)], CancellationToken.None)).Resources);

        Assert.Equal(Kinds.TestResults, results.Kind);
        Assert.Equal(folder, results.Key);
        Assert.True(results.IsShared);
        Assert.True(results.IsRegenerable);
        Assert.True(results.Bytes >= 64 * 1024);
        Assert.Null(results.Blocker);
        Assert.Contains("Snail.Toolkit.Web", results.Title, StringComparison.Ordinal);
        Assert.Contains("the next test run makes them again", results.Title, StringComparison.Ordinal);
    }

    /// <summary>A repository no session of SleepOf's still names is found through the folders VS Code keeps state for.</summary>
    [Fact]
    public async Task Repository_TheEditorOpened_IsLookedInto()
    {
        var repository = Repository("Snail.Toolkit.Web");
        var state = Directory.CreateDirectory(_scratch.File("Code/User/workspaceStorage/0a1b")).FullName;
        await File.WriteAllTextAsync(Path.Combine(state, "workspace.json"), $$"""{ "folder": "{{new Uri(repository).AbsoluteUri}}" }""");
        Dump(Path.Combine(repository, "tests", "Scraping.Tests", "TestResults"));

        Assert.Single((await Kind.ObserveAsync([], CancellationToken.None)).Resources);
    }

    [Fact]
    public async Task OnlyRepositories_AreLookedInto_AndNotBelowHiddenFoldersBuildTreesOrTooDeep()
    {
        var notARepository = _scratch.File("Projects/loose");
        var repository = Repository("Deep");
        Dump(Path.Combine(notARepository, "tests", "TestResults"));
        Dump(Path.Combine(repository, ".git", "TestResults"));
        Dump(Path.Combine(repository, "bin", "Debug", "TestResults"));
        Dump(Path.Combine(repository, "a", "b", "c", "d", "TestResults"));
        Dump(Path.Combine(repository, "src", "Tests", "Some.Tests", "TestResults"));
        Directory.CreateDirectory(Path.Combine(repository, "tests", "Empty.Tests", "TestResults"));

        var found = (await Kind.ObserveAsync([WorkingIn(notARepository), WorkingIn(repository)], CancellationToken.None)).Resources;

        Assert.Equal(Path.Combine(repository, "src", "Tests", "Some.Tests", "TestResults"), Assert.Single(found).Key);
    }

    /// <summary>A test run writes its dump at the end: what was written within the hour is left alone.</summary>
    [Fact]
    public async Task TestResults_WrittenWithinTheHour_AreNotToBeReleased()
    {
        var repository = Repository("Snail.Toolkit.Web");
        Dump(Path.Combine(repository, "tests", "Scraping.Tests", "TestResults"));
        _clock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(10));

        var results = Assert.Single((await Kind.ObserveAsync([WorkingIn(repository)], CancellationToken.None)).Resources);

        Assert.NotNull(results.Blocker);
        Assert.Equal(Verdict.Report, Assert.Single(Planner.Decide(Inventory([results]), Scope.Caches, "plan").Decisions).Verdict);
    }

    [Fact]
    public async Task TestResults_Released_AreErased_AndTheRepositoryKept()
    {
        var repository = Repository("Snail.Toolkit.Web");
        var folder = Path.Combine(repository, "tests", "Scraping.Tests", "TestResults");
        Dump(folder);
        var kind = Kind;
        var results = Assert.Single((await kind.ObserveAsync([WorkingIn(repository)], CancellationToken.None)).Resources);

        Assert.Equal(Verdict.Remove, Assert.Single(Planner.Decide(Inventory([results]), Scope.Caches, "plan").Decisions).Verdict);
        Assert.Equal(Outcome.Released, (await kind.ReleaseAsync(results, CancellationToken.None)).Outcome);
        Assert.False(Directory.Exists(folder));
        Assert.True(Directory.Exists(Path.Combine(repository, "tests", "Scraping.Tests")));
        Assert.False(Directory.Exists(_scratch.File("Trash")));
    }

    /// <summary>A test run that began after the plan was shown is writing into the folder now: it is judged again and kept.</summary>
    [Fact]
    public async Task TestResults_WrittenAfterThePlan_AreKept()
    {
        var repository = Repository("Snail.Toolkit.Web");
        var folder = Path.Combine(repository, "tests", "Scraping.Tests", "TestResults");
        var dump = Dump(folder);
        File.SetLastWriteTimeUtc(dump, DateTime.UtcNow.AddDays(-3));
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(dump)!, DateTime.UtcNow.AddDays(-3));
        Directory.SetLastWriteTimeUtc(folder, DateTime.UtcNow.AddDays(-3));
        _clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var kind = Kind;
        var results = Assert.Single((await kind.ObserveAsync([WorkingIn(repository)], CancellationToken.None)).Resources);

        Assert.Null(results.Blocker);

        await File.WriteAllTextAsync(Path.Combine(folder, "run.trx"), "<TestRun/>");

        Assert.Equal(Outcome.Changed, (await kind.ReleaseAsync(results, CancellationToken.None)).Outcome);
        Assert.True(File.Exists(dump));
    }
}
