using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Core.Adapters;
using Snail.MCP.CleanUp.Core.Domain;
using Snail.MCP.CleanUp.Domain;
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

    private ProcessKind Kind => new(_table, _programs, Ledger, TimeProvider.System);

    private static Session SessionOf(RunningProcess agent) => new(Me.Id, agent.Pid, DateTimeOffset.UnixEpoch, "/work", IsAlive: true);

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
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _scratch.Dispose();
    }
}
