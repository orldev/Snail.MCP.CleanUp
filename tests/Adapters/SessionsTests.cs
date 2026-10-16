using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Domain;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class SessionsTests : IDisposable
{
    /// <summary>813430529 seconds after 2001-01-01 is 2026-10-11 09:08:49 UTC, as SleepOf wrote it that day.</summary>
    private const double SleepOfMoment = 813430529;

    private static readonly DateTimeOffset Began = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(SleepOfMoment);

    private readonly Scratch _scratch = new();

    private readonly FakeProcessTable _processes = new();

    private MachineSessions Sessions => new(new ServerConfig { SleepOfDirectory = _scratch.Path }, _processes);

    private void Written(string json) => File.WriteAllText(_scratch.File(SleepOfAgents.FileName), json);

    [Fact]
    public void AgentsFile_IsReadWithPidsStartsAndDirectories()
    {
        var entry = Assert.Single(SleepOfAgents.Parse(
            $$$"""{"sessions":[{"id":"s1","cwd":"/work","startedAt":{{{SleepOfMoment}}},"state":"working"}],"pids":{"s1":4242}}"""));

        Assert.Equal(new SleepOfAgents.Entry("s1", 4242, Began, "/work", "working"), entry);
    }

    [Fact]
    public void AgentsFile_ThatIsNotOne_GivesNoSessions() =>
        Assert.Empty(SleepOfAgents.Parse("{ not json"));

    /// <summary>
    /// SleepOf says the session runs under agent 4242, and the process table comes back empty: unknown is not ended, so the
    /// sessions are not answered at all, as IHolders asks of a host that cannot read its owners.
    /// </summary>
    [Fact]
    public async Task Sessions_WhileTheProcessTableCannotBeRead_AreNotAnsweredAsEnded()
    {
        Written($$$"""{"sessions":[{"id":"s1","startedAt":{{{SleepOfMoment}}}}],"pids":{"s1":4242}}""");

        await Assert.ThrowsAsync<SessionsUnknownException>(() => Sessions.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Session_WhoseAgentStillRuns_IsAlive()
    {
        _processes.Add(4242, 1, "/bin/claude --output-format stream-json", Began.AddSeconds(-30));
        Written($$$"""{"sessions":[{"id":"s1","startedAt":{{{SleepOfMoment}}}}],"pids":{"s1":4242}}""");

        Assert.True(Assert.Single(await Sessions.ListAsync(CancellationToken.None)).IsAlive);
    }

    /// <summary>A reused id belongs to a program that started after SleepOf last heard from the session; that session is over.</summary>
    [Fact]
    public async Task Session_WhoseIdNowBelongsToALaterAgent_IsEnded()
    {
        _processes.Add(4242, 1, "/bin/claude", Began.AddHours(3));
        Written($$$"""{"sessions":[{"id":"s1","startedAt":{{{SleepOfMoment}}},"updatedAt":{{{SleepOfMoment}}}}],"pids":{"s1":4242}}""");

        var sessions = await Sessions.ListAsync(CancellationToken.None);

        Assert.False(sessions.Single(session => session.Owner.Id == "s1").IsAlive);
    }

    /// <summary>A resumed session keeps its id and its start from hours ago; its agent is a new process SleepOf has heard from since.</summary>
    [Fact]
    public async Task Session_ResumedByANewAgent_IsAlive()
    {
        _processes.Add(4242, 1, "/bin/claude", Began.AddHours(4));
        Written($$$"""{"sessions":[{"id":"s1","startedAt":{{{SleepOfMoment}}},"updatedAt":{{{SleepOfMoment + 5 * 3600}}}}],"pids":{"s1":4242}}""");

        Assert.True(Assert.Single(await Sessions.ListAsync(CancellationToken.None)).IsAlive);
    }

    [Fact]
    public async Task Session_SleepOfSawEnd_IsEnded()
    {
        _processes.Add(4242, 1, "/bin/claude", Began.AddSeconds(-30));
        Written($$$"""{"sessions":[{"id":"s1","startedAt":{{{SleepOfMoment}}},"state":"ended"}],"pids":{"s1":4242}}""");

        Assert.False((await Sessions.ListAsync(CancellationToken.None)).Single(session => session.Owner.Id == "s1").IsAlive);
    }

    [Fact]
    public async Task Session_WhoseIdNowBelongsToAnotherProgram_IsEnded()
    {
        _processes.Add(4242, 1, "/usr/bin/clang", Began.AddSeconds(-30));
        Written($$$"""{"sessions":[{"id":"s1","startedAt":{{{SleepOfMoment}}}}],"pids":{"s1":4242}}""");

        Assert.False(Assert.Single(await Sessions.ListAsync(CancellationToken.None)).IsAlive);
    }

    [Fact]
    public async Task Agent_SleepOfDoesNotKnow_IsStillASession_NamedByItsProcess()
    {
        var agent = _processes.Add(5151, 1, "/bin/claude -p task");

        var session = Assert.Single(await Sessions.ListAsync(CancellationToken.None));

        Assert.Equal($"{MachineSessions.ByProcess}{agent.Key}", session.Owner.Id);
        Assert.True(session.IsAlive);
    }

    /// <summary>Claude Code starts a second claude as Chrome's MCP server; it is a tool of a session, not one.</summary>
    [Fact]
    public async Task ChromeHelper_NamedClaude_IsNotASession()
    {
        _processes.Add(6161, 1, "/bin/claude --claude-in-chrome-mcp");

        Assert.Empty(await Sessions.ListAsync(CancellationToken.None));
    }

    [Fact]
    public void Agent_AboveAServer_IsItsNearestClaudeAncestor()
    {
        var processes = new FakeProcessTable();
        var agent = processes.Add(100, 1, "/bin/claude");

        processes.Add(200, 100, "dotnet Snail.MCP.CleanUp.dll");
        processes.Add(300, 200, "/bin/zsh");

        Assert.Equal(agent, MachineSessions.AgentAbove(300, processes.Running.ToDictionary(process => process.Pid)));
    }

    [Fact]
    public void Server_StartedByNoAgent_HasNoSession()
    {
        var processes = new FakeProcessTable();

        processes.Add(200, 1, "dotnet Snail.MCP.CleanUp.dll");

        Assert.Null(MachineSessions.AgentAbove(200, processes.Running.ToDictionary(process => process.Pid)));
    }

    public void Dispose() => _scratch.Dispose();
}
