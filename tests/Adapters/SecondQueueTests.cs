using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Adapters.Trails;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Core.Domain;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class SecondQueueTests
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void Certificates_AreReadByHash_WithTheirLabel()
    {
        var traces = CertificateTrail.Parse(
            """
            keychain: "/Users/dev/Library/Keychains/login.keychain-db"
            SHA-1 hash: 0F2D7FB650BC12C9DDB73B1371541C620509FD7A
                "alis"<blob>="mkcert development CA"
            SHA-1 hash: C761DBFE0C18748942978D4E94060333925B1C27
                "alis"<blob>=0x00FF  "binary"
            """);

        Assert.Equal(["0F2D7FB650BC12C9DDB73B1371541C620509FD7A", "C761DBFE0C18748942978D4E94060333925B1C27"], traces.Select(trace => trace.Key));
        Assert.Equal("certificate «mkcert development CA» (0F2D7FB6)", traces[0].Title);
    }

    [Fact]
    public void Worktrees_AreReadWithTheirBranches_TheMainOneFirst_AndAPrunableOneSaysSo()
    {
        var worktrees = WorktreeKind.Parse(
            """
            worktree /work/app
            HEAD 1111111111111111111111111111111111111111
            branch refs/heads/main

            worktree /work/app/.claude/worktrees/agent-a1
            HEAD 2222222222222222222222222222222222222222
            branch refs/heads/worktree-agent-a1

            worktree /work/app/.claude/worktrees/agent-b2
            HEAD 3333333333333333333333333333333333333333
            detached
            prunable gitdir file points to non-existent location
            """);

        Assert.Equal(["/work/app", "/work/app/.claude/worktrees/agent-a1", "/work/app/.claude/worktrees/agent-b2"], worktrees.Select(worktree => worktree.Key));
        Assert.Equal("worktree-agent-a1", worktrees[1].Facts["branch"]);
        Assert.Contains("prunable", worktrees[2].Title, StringComparison.Ordinal);
    }

    /// <summary>A worktree may hold work nobody committed; this server names it and leaves the deciding to a person.</summary>
    [Fact]
    public async Task Worktree_IsNeverReleased()
    {
        var worktree = WorktreeKind.Parse("worktree /work/app\nbranch refs/heads/main")[0];

        Assert.Equal(Outcome.Failed, (await new WorktreeKind(new FakePrograms()).ReleaseAsync(worktree, CancellationToken.None)).Outcome);
    }

    [Fact]
    public void GlobalPackagePlaces_AreWatched()
    {
        Assert.Contains(MacPlaces.Watched, place => place.Directory == "/opt/homebrew/Cellar");
        Assert.Contains(MacPlaces.Watched, place => place.Directory == $"{Home}/.dotnet/tools/.store");
    }

    [Fact]
    public void DerivedData_IsACacheBlockedWhileXcodeRuns()
    {
        var rule = MacPlaces.Shipped.Last(candidate => candidate.Matches(new Trace("file", $"{Home}/Library/Developer/Xcode/DerivedData/App-abcdef", "x")));

        Assert.Equal(RuleAction.Cache, rule.Action);
        Assert.Equal("Xcode", rule.BlockedWhileRunning);
    }
}
