using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Configuration;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class EditorTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow.AddDays(1));

    private ServerConfig Config => new() { EditorUserDirectory = _scratch.File("User"), DataDirectory = _scratch.File("data"), TrashDirectory = _scratch.File("Trash") };

    private EditorKind Kind => KindAt(_clock);

    private EditorKind KindAt(FakeTimeProvider clock) => new(Config, new Disposal(Config, clock), new Sizes(clock), clock);

    private string State(string hash, string json)
    {
        var directory = _scratch.File($"User/workspaceStorage/{hash}");

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "workspace.json"), json);

        return directory;
    }

    [Fact]
    public async Task StateOfAFolderThatNoLongerExists_IsAnOrphanedCache()
    {
        var gone = State("a1", """{"folder":"file:///Users/dev/Projects/app/.claude/worktrees/agent-1"}""");

        var orphan = Assert.Single((await Kind.ObserveAsync([], CancellationToken.None)).Resources);

        Assert.Equal(gone, orphan.Key);
        Assert.True(orphan.IsShared);
        Assert.Null(orphan.Blocker);
        Assert.Contains("/Users/dev/Projects/app/.claude/worktrees/agent-1", orphan.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StateOfAFolderThatExists_OrOfARemoteOne_IsNotAResource()
    {
        Directory.CreateDirectory(_scratch.File("project with spaces"));
        State("b1", $$"""{"folder":"{{new Uri(_scratch.File("project with spaces")).AbsoluteUri}}"}""");
        State("b2", """{"folder":"vscode-remote://ssh-remote+box/home/dev/app"}""");

        Assert.Empty((await Kind.ObserveAsync([], CancellationToken.None)).Resources);
    }

    /// <summary>A window still open on a deleted folder keeps writing its state; that is what in use looks like.</summary>
    [Fact]
    public async Task StateWrittenInTheLastHour_IsBlocked()
    {
        State("c1", """{"folder":"file:///nowhere/at/all"}""");

        var soon = KindAt(new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(10)));

        Assert.NotNull(Assert.Single((await soon.ObserveAsync([], CancellationToken.None)).Resources).Blocker);
    }

    [Fact]
    public async Task Release_OfStateWhoseFolderCameBack_TouchesNothing()
    {
        var folder = _scratch.File("came-back");
        var state = State("d1", $$"""{"folder":"{{new Uri(folder).AbsoluteUri}}"}""");
        var orphan = Assert.Single((await Kind.ObserveAsync([], CancellationToken.None)).Resources);

        Directory.CreateDirectory(folder);

        Assert.Equal(Outcome.Changed, (await Kind.ReleaseAsync(orphan, CancellationToken.None)).Outcome);
        Assert.True(Directory.Exists(state));
    }

    /// <summary>WebStorage is removed whole: its quota database indexes every bucket, and a bucket removed alone leaves it lying.</summary>
    [Fact]
    public void ShippedRules_CoverWebStorageWhole_AndOnlyWhileCodeIsClosed()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var rule = MacPlaces.Shipped.Last(candidate => candidate.Matches(new Trace("file", $"{home}/Library/Application Support/Code/WebStorage", "x")));

        Assert.Equal(RuleAction.Cache, rule.Action);
        Assert.Equal("Code", rule.BlockedWhileRunning);
        Assert.DoesNotContain(MacPlaces.Shipped, candidate => candidate.Matches(new Trace("file", $"{home}/Library/Application Support/Code/WebStorage/QuotaManager", "x")));
    }

    public void Dispose() => _scratch.Dispose();
}
