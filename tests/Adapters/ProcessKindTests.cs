using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Domain;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class ProcessKindTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly LedgerFile _file;

    private readonly FakeProcessTable _table = new();

    private readonly HashSet<int> _alive = [];

    private readonly FakePrograms _programs = new();

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UnixEpoch);

    public ProcessKindTests()
    {
        _file = new LedgerFile(_scratch.File("ledger.db"));
        _programs.Answer = Answered;
    }

    private SqliteLedger Ledger => new(_file);

    private ProcessKind Kind => new(_table, _programs, Ledger, _later);

    private readonly FakeTimeProvider _later = new(DateTimeOffset.UnixEpoch.AddDays(2));

    private static Session SessionOf(RunningProcess agent) => new(Me, agent.Pid, IsAlive: true) { StartedAt = DateTimeOffset.UnixEpoch, Directory = "/work" };

    private ProgramRun Answered(string program, IReadOnlyList<string> arguments)
    {
        if (program == "lsof")
            return FakePrograms.Said("p300\ncnode\nn127.0.0.1:3000\n");

        if (program == "kill" && arguments[0] == "-0")
            return _alive.Contains(int.Parse(arguments[1], System.Globalization.CultureInfo.InvariantCulture)) ? FakePrograms.Said(string.Empty) : FakePrograms.Failed();

        if (program == "kill" && arguments[0] is "-TERM" or "-KILL")
            _alive.Remove(int.Parse(arguments[1], System.Globalization.CultureInfo.InvariantCulture));

        return FakePrograms.Said(string.Empty);
    }

    [Fact]
    public async Task WhatRunsUnderTheAgentsShells_IsTheAgents_WithItsPorts()
    {
        var agent = _table.Add(100, 1, "/bin/claude");
        _table.Add(200, 100, "/bin/zsh -c npm run dev");
        _table.Add(300, 200, "node server.js");

        var observed = await Kind.ObserveAsync([SessionOf(agent)], CancellationToken.None);
        var server = observed.Resources.Single(resource => resource.Facts["pid"] == "300");

        Assert.Equal(Me, server.Owner);
        Assert.Equal(Evidence.Ancestry, server.Evidence);
        Assert.Contains("port 3000", server.Title, StringComparison.Ordinal);
        Assert.Contains("port:3000", observed.Claimed);
    }

    /// <summary>The command the agent is running right now must not be ended by a cleanup it asked for in parallel.</summary>
    [Fact]
    public async Task ProgramStartedAMomentAgoUnderARunningAgent_IsBlocked()
    {
        var agent = _table.Add(100, 1, "/bin/claude");
        _table.Add(200, 100, "/bin/zsh -c dotnet test", DateTimeOffset.UnixEpoch.AddDays(2).AddSeconds(-20));

        var shell = Assert.Single((await Kind.ObserveAsync([SessionOf(agent)], CancellationToken.None)).Resources);

        Assert.Contains("right now", shell.Blocker, StringComparison.Ordinal);
    }

    /// <summary>Measured on 2026-10-11: the first live plan listed the test run that asked for it, and this server's own ps.</summary>
    [Fact]
    public void ThisServer_ItsAncestors_AndItsChildren_AreNeverLeftovers()
    {
        var table = new FakeProcessTable();

        table.Add(100, 1, "/bin/claude");
        table.Add(200, 100, "/bin/zsh -c dotnet test");
        table.Add(300, 200, "dotnet testhost");
        table.Add(400, 300, "dotnet Snail.MCP.CleanUp.dll");
        table.Add(500, 400, "ps -axww");
        table.Add(600, 200, "node server.js");

        var own = ProcessKind.Own(table.Running, table.Running.ToLookup(process => process.ParentPid), self: 400);

        Assert.Equal([100, 200, 300, 400, 500], own.Order());
    }

    /// <summary>The servers of a session are its tools; ending one would break the very session that asked.</summary>
    [Fact]
    public async Task WhatRunsUnderTheAgentsMcpServers_IsNotTheAgents()
    {
        var agent = _table.Add(100, 1, "/bin/claude");
        _table.Add(200, 100, "dotnet Snail.MCP.Roslyn.dll");
        _table.Add(300, 200, "dotnet MSBuild.dll");

        var observed = await Kind.ObserveAsync([SessionOf(agent)], CancellationToken.None);

        Assert.DoesNotContain(observed.Resources, resource => resource.Owner.IsKnown);
    }

    [Fact]
    public async Task ChildrenOfAProgramStartedOnTheRecord_AreItsOwners()
    {
        var root = _table.Add(400, 1, "/bin/zsh -c npm run dev");
        _table.Add(401, 400, "node vite");

        await Ledger.RecordAsync(new Holding($"process:{root.Key}", Gone, Evidence.Ledger, DateTimeOffset.UnixEpoch, "npm run dev"), CancellationToken.None);

        var observed = await Kind.ObserveAsync([], CancellationToken.None);

        Assert.Equal(Gone, observed.Resources.Single(resource => resource.Facts["pid"] == "401").Owner);
    }

    /// <summary>A detached program says nothing about whose it is; it is listed so the ledger can say.</summary>
    [Fact]
    public async Task DetachedProgram_IsListedWithoutAnOwner_ApplicationsAreNot()
    {
        _table.Add(500, 1, "/usr/local/bin/node orphan.js");
        _table.Add(501, 1, "/Applications/Safari.app/Contents/MacOS/Safari");

        var observed = await Kind.ObserveAsync([], CancellationToken.None);

        Assert.Equal("500", Assert.Single(observed.Resources).Facts["pid"]);
        Assert.False(observed.Resources[0].Owner.IsKnown);
    }

    /// <summary>The http.server of 2026-10-13: detached by its agent, seven hours a child of launchd, its session in its environment.</summary>
    [Fact]
    public async Task DetachedProgram_WhoseEnvironmentNamesAnEndedSession_IsThatSessionsOrphan_WithItsPort()
    {
        _table.Add(300, 1, "/Applications/Xcode.app/Contents/Developer/usr/bin/python3 -m http.server 3000", session: Gone.Id, agent: 100);

        var observed = await Kind.ObserveAsync([Dead(Gone), Alive(Me)], CancellationToken.None);
        var server = Assert.Single(observed.Resources);
        var plan = Planner.Decide(Inventory([server]), Scope.Orphans, "plan", MachineKinds.Order);

        Assert.Equal((Gone, Evidence.Environment), (server.Owner, server.Evidence));
        Assert.Null(server.Blocker);
        Assert.Contains("port:3000", observed.Claimed);
        Assert.Equal(Verdict.Remove, Assert.Single(plan.Decisions).Verdict);
        Assert.Contains("environment", plan.Decisions[0].Reason, StringComparison.Ordinal);
    }

    /// <summary>A running session's detached program is its own to remove, and nobody else's.</summary>
    [Fact]
    public async Task DetachedProgram_OfARunningSession_IsItsOwn_AndKeptFromEveryoneElse()
    {
        var agent = _table.Add(100, 1, "/bin/claude", DateTimeOffset.UnixEpoch);
        _table.Add(300, 1, "node server.js", session: Me.Id, agent: agent.Pid);

        var server = Assert.Single((await Kind.ObserveAsync([SessionOf(agent)], CancellationToken.None)).Resources);
        var inventory = Inventory([server]);

        Assert.Equal((Me, Evidence.Environment), (server.Owner, server.Evidence));
        Assert.Equal(Verdict.Remove, Assert.Single(Planner.Decide(inventory, Scope.Mine, "plan", MachineKinds.Order).Decisions).Verdict);
        Assert.Equal(Verdict.Keep, Assert.Single(Planner.Decide(inventory with { Asker = Neighbour }, Scope.Everything, "plan", MachineKinds.Order).Decisions).Verdict);
        Assert.Empty(Planner.Decide(inventory with { Asker = Neighbour }, Scope.Orphans, "plan", MachineKinds.Order).Decisions);
    }

    /// <summary>After /clear the agent goes on under a new id; what it started before still names the old one, and is still in use.</summary>
    [Fact]
    public async Task Program_NamingASessionItsAgentWentOnFrom_BelongsToTheSessionTheAgentRunsNow()
    {
        var agent = _table.Add(100, 1, "/bin/claude", DateTimeOffset.UnixEpoch);
        _table.Add(300, 1, "node server.js", session: "before-clear", agent: agent.Pid);
        _table.Add(301, 1, "node watcher.js", session: "before-clear-too");
        _table.Add(200, 100, "dotnet Snail.MCP.Roslyn.dll", session: "before-clear-too");

        var observed = await Kind.ObserveAsync([SessionOf(agent), Dead(Owner.Session("before-clear")), Dead(Owner.Session("before-clear-too"))], CancellationToken.None);

        Assert.All(observed.Resources, resource => Assert.Equal(Me, resource.Owner));
        Assert.Equal(["300", "301"], observed.Resources.Select(resource => resource.Facts["pid"]).Order(StringComparer.Ordinal));
    }

    /// <summary>An id given again: the agent the environment names ended, and the claude now under its id started after the program.</summary>
    [Fact]
    public async Task Program_WhoseAgentsIdIsNowAnotherAgents_IsItsEndedSessions()
    {
        _table.Add(100, 1, "/bin/claude", DateTimeOffset.UnixEpoch.AddDays(3));
        _table.Add(300, 1, "node server.js", session: Gone.Id, agent: 100);

        var server = Assert.Single((await Kind.ObserveAsync([Dead(Gone)], CancellationToken.None)).Resources);

        Assert.Equal(Gone, server.Owner);
    }

    /// <summary>The servers of a running session carry its id too, and what they start: they are its tools all the same.</summary>
    [Fact]
    public async Task ProgramsUnderARunningAgentsServers_AreNotTakenOnTheirEnvironment()
    {
        var agent = _table.Add(100, 1, "/bin/claude", DateTimeOffset.UnixEpoch);
        _table.Add(200, 100, "dotnet Snail.MCP.Roslyn.dll", session: Me.Id, agent: 100);
        _table.Add(300, 200, "dotnet BuildHost.dll", session: Me.Id, agent: 100);
        _table.Add(400, 100, "/bin/zsh -c npm run dev", session: Me.Id, agent: 100);

        var observed = await Kind.ObserveAsync([SessionOf(agent)], CancellationToken.None);
        var shell = Assert.Single(observed.Resources);

        Assert.Equal(("400", Evidence.Ancestry), (shell.Facts["pid"], shell.Evidence));
    }

    [Fact]
    public async Task Program_OfARunningSession_StartedAMomentAgo_IsBlocked()
    {
        var agent = _table.Add(100, 1, "/bin/claude", DateTimeOffset.UnixEpoch);
        _table.Add(300, 1, "node server.js", DateTimeOffset.UnixEpoch.AddDays(2).AddSeconds(-20), session: Me.Id, agent: 100);

        var server = Assert.Single((await Kind.ObserveAsync([SessionOf(agent)], CancellationToken.None)).Resources);

        Assert.Contains("right now", server.Blocker, StringComparison.Ordinal);
    }

    /// <summary>VS Code opened with <c>code .</c> from an agent's shell keeps the session in its environment; a person may be using it.</summary>
    [Fact]
    public async Task Application_StartedByAnAgent_IsNamed_AndLeftForAPerson()
    {
        _table.Add(300, 1, "/Applications/Visual Studio Code.app/Contents/MacOS/Electron", executable: "/Applications/Visual Studio Code.app/Contents/MacOS/Electron", session: Gone.Id);
        _table.Add(301, 300, "/Applications/Visual Studio Code.app/Contents/Frameworks/Code Helper.app/Contents/MacOS/Code Helper", session: Gone.Id);

        var observed = await Kind.ObserveAsync([Dead(Gone)], CancellationToken.None);

        Assert.Equal(2, observed.Resources.Count);
        Assert.All(observed.Resources, resource => Assert.Contains("application", resource.Blocker, StringComparison.Ordinal));
        Assert.All(Planner.Decide(Inventory([.. observed.Resources]), Scope.Orphans, "plan", MachineKinds.Order).Decisions, decision => Assert.Equal(Verdict.Report, decision.Verdict));
    }

    /// <summary>The build servers of 2026-10-13: detached, carrying this session's id an hour on, reused by every window's builds.</summary>
    [Fact]
    public async Task SharedBuildServers_CarryingAnEndedSession_AreNotItsOrphans_OnlyNamed()
    {
        _table.Add(57178, 1, "/Users/me/.dotnet/sdk/10.0.302/Roslyn/bincore/VBCSCompiler -pipename:WdTTuu", executable: "/Users/me/.dotnet/sdk/10.0.302/Roslyn/bincore/VBCSCompiler", session: Gone.Id, agent: 100);
        _table.Add(57626, 1, "/Users/me/.dotnet/dotnet /Users/me/.dotnet/sdk/10.0.302/MSBuild.dll /noautoresponse /nologo /nodemode:1 /nodeReuse:true /low:false", session: Gone.Id, agent: 100);
        _table.Add(57700, 1, "/Users/me/.dotnet/dotnet /Users/me/.dotnet/sdk/10.0.302/Sdks/Microsoft.NET.Sdk.Razor/tools/rzc.dll server -p abc", session: Gone.Id, agent: 100);

        var observed = await Kind.ObserveAsync([Dead(Gone)], CancellationToken.None);
        var inventory = Inventory([.. observed.Resources]);

        Assert.Equal(3, observed.Resources.Count);
        Assert.All(observed.Resources, resource =>
        {
            Assert.False(resource.Owner.IsKnown);
            Assert.Equal(SharedServers.Why, resource.Blocker);
        });
        Assert.Empty(Planner.Decide(inventory, Scope.Orphans, "plan", MachineKinds.Order).Decisions);
        Assert.Empty(Planner.Decide(inventory, Scope.Everything, "plan", MachineKinds.Order).Removals);
    }

    /// <summary>Seen under the agent's shell the moment a build started it, a compiler server is still everybody's.</summary>
    [Fact]
    public async Task SharedBuildServer_UnderARunningAgentsShell_IsNotTheAgents()
    {
        var agent = _table.Add(100, 1, "/bin/claude", DateTimeOffset.UnixEpoch);
        _table.Add(200, 100, "/bin/zsh -c dotnet build");
        _table.Add(300, 200, "/Users/me/.dotnet/dotnet /Users/me/.dotnet/sdk/10.0.302/MSBuild.dll -nodemode:1 -nodeReuse:true", session: Me.Id, agent: 100);

        var observed = await Kind.ObserveAsync([SessionOf(agent)], CancellationToken.None);
        var node = observed.Resources.Single(resource => resource.Facts["pid"] == "300");

        Assert.Equal((Owner.Nobody, Evidence.None), (node.Owner, node.Evidence));
        Assert.DoesNotContain(Planner.Decide(Inventory([.. observed.Resources]), Scope.Mine, "plan", MachineKinds.Order).Removals, decision => decision.Resource.Facts["pid"] == "300");
    }

    /// <summary>The ledger may hold a node a census saw under a shell before builds were known to be shared: the plan still only names it.</summary>
    [Fact]
    public async Task SharedBuildServer_WhoseOwnerTheLedgerWroteDown_IsStillOnlyNamed()
    {
        _table.Add(57178, 1, "/Users/me/.dotnet/sdk/10.0.302/Roslyn/bincore/VBCSCompiler -pipename:x", executable: "/Users/me/.dotnet/sdk/10.0.302/Roslyn/bincore/VBCSCompiler");

        var listed = Assert.Single((await Kind.ObserveAsync([], CancellationToken.None)).Resources);
        var settled = listed with { Owner = Gone, Evidence = Evidence.Ancestry };

        Assert.Equal(Verdict.Report, Assert.Single(Planner.Decide(Inventory([settled]), Scope.Orphans, "plan", MachineKinds.Order).Decisions).Verdict);
    }

    [Fact]
    public async Task Release_EndsTheProgramOnSigterm_AndSaysThePortIsFree()
    {
        var process = _table.Add(300, 1, "node server.js");
        _alive.Add(300);

        var observed = await Kind.ObserveAsync([], CancellationToken.None);
        var release = await Kind.ReleaseAsync(observed.Resources[0], CancellationToken.None);

        Assert.Equal(Outcome.Released, release.Outcome);
        Assert.Contains("SIGTERM", release.Detail, StringComparison.Ordinal);
        Assert.Contains("port 3000 is free", release.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(_programs.Calls, call => call.StartsWith("kill -KILL", StringComparison.Ordinal));
    }

    /// <summary>The id was given to somebody else's program since the plan was made; that program is left alone.</summary>
    [Fact]
    public async Task Release_OfAnIdNowHeldByAnotherProgram_TouchesNothing()
    {
        var then = _table.Add(300, 1, "node server.js");
        var observed = await Kind.ObserveAsync([], CancellationToken.None);

        _table.Running.Clear();
        _table.Add(300, 1, "/usr/local/bin/postgres", then.StartedAt.AddHours(1));

        var release = await Kind.ReleaseAsync(observed.Resources[0], CancellationToken.None);

        Assert.Equal(Outcome.Changed, release.Outcome);
        Assert.DoesNotContain(_programs.Calls, call => call.StartsWith("kill -TERM", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Release_OfAProgramAlreadyEnded_IsGone()
    {
        _table.Add(300, 1, "node server.js");
        var observed = await Kind.ObserveAsync([], CancellationToken.None);

        _table.Running.Clear();

        Assert.Equal(Outcome.Gone, (await Kind.ReleaseAsync(observed.Resources[0], CancellationToken.None)).Outcome);
    }

    public void Dispose()
    {
        _file.Dispose();
        _scratch.Dispose();
    }
}
