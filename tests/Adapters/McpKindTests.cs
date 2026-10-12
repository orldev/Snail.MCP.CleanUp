using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Core.Domain;
using Snail.MCP.CleanUp.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class McpKindTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly LedgerFile _file;

    private readonly FakeProcessTable _table = new();

    private readonly FakePrograms _programs = new();

    private readonly FakeTimeProvider _later = new(DateTimeOffset.UnixEpoch.AddDays(2));

    public McpKindTests() => _file = new LedgerFile(_scratch.File("ledger.db"));

    public void Dispose() => _scratch.Dispose();

    private SqliteLedger Ledger => new(_file);

    private McpKind Kind => new(_table, new ProcessKind(_table, _programs, Ledger, _later), Ledger, _later);

    private static Session Alive(Owner owner, RunningProcess agent) => new(owner.Id, agent.Pid, DateTimeOffset.UnixEpoch, "/work", IsAlive: true);

    /// <summary>A server is the session's tool while the session runs: listed as its, and never released.</summary>
    [Fact]
    public async Task ARunningSessionsServers_AreItsOwn_AndBlocked()
    {
        var agent = _table.Add(100, 1, "/bin/claude");
        _table.Add(200, 100, "/bin/zsh -c dotnet test");
        _table.Add(300, 100, "dotnet /Users/me/.snail-mcp/Snail.MCP.Memory/current/Snail.MCP.Memory.dll");

        var server = Assert.Single((await Kind.ObserveAsync([Alive(Me, agent)], CancellationToken.None)).Resources);

        Assert.Equal("Snail.MCP.Memory", server.Facts["server"]);
        Assert.Equal(Me, server.Owner);
        Assert.Equal(Evidence.Ancestry, server.Evidence);
        Assert.Contains("running", server.Blocker, StringComparison.Ordinal);
    }

    /// <summary>Every session starts its own copy of every server; how many there are is what shows one worth sharing.</summary>
    [Fact]
    public async Task TheSameServerUnderTwoSessions_SaysItRunsTwice()
    {
        var mine = _table.Add(100, 1, "/bin/claude");
        var theirs = _table.Add(101, 1, "/bin/claude");
        _table.Add(300, 100, "dotnet /x/Snail.MCP.Memory.dll");
        _table.Add(301, 101, "dotnet /x/Snail.MCP.Memory.dll");
        _table.Add(302, 101, "/x/Snail.MCP.Vault");

        var observed = await Kind.ObserveAsync([Alive(Me, mine), Alive(Neighbour, theirs)], CancellationToken.None);

        Assert.All(observed.Resources.Where(resource => resource.Facts["server"] == "Snail.MCP.Memory"), copy => Assert.Equal("2", copy.Facts["copies"]));
        Assert.DoesNotContain("copies", observed.Resources.Single(resource => resource.Facts["server"] == "Snail.MCP.Vault").Facts.Keys);
    }

    /// <summary>A hook runs as a child of the agent too, for a moment; a server is what is still there a few seconds later.</summary>
    [Fact]
    public async Task AChildStartedAMomentAgo_IsNotTakenForAServer()
    {
        var agent = _table.Add(100, 1, "/bin/claude");
        _table.Add(300, 100, "/x/snail-mcp-cleanup-hook", DateTimeOffset.UnixEpoch.AddDays(2).AddSeconds(-1));

        Assert.Empty((await Kind.ObserveAsync([Alive(Me, agent)], CancellationToken.None)).Resources);
    }

    /// <summary>A stdio server whose agent ended has no client; the ledger, written while it was a child, says whose it was.</summary>
    [Fact]
    public async Task AServerTheLedgerSawUnderAnEndedSession_IsThatSessionsLeftover()
    {
        var orphan = _table.Add(300, 1, "dotnet /x/Snail.MCP.Memory.dll");
        await Ledger.RecordAsync(new Holding($"mcp:{orphan.Key}", Gone, Evidence.Ancestry, DateTimeOffset.UnixEpoch, "Snail.MCP.Memory"), CancellationToken.None);

        var observed = await Kind.ObserveAsync([], CancellationToken.None);
        var server = Assert.Single(observed.Resources);

        Assert.Equal(Gone, server.Owner);
        Assert.Null(server.Blocker);
        Assert.Contains($"process:{orphan.Key}", observed.Claimed);
        Assert.DoesNotContain((await new ProcessKind(_table, _programs, Ledger, _later).ObserveAsync([], CancellationToken.None)).Resources, resource => resource.Key == orphan.Key);
    }

    /// <summary>A detached program nobody saw under an agent stays the process kind's, without an owner.</summary>
    [Fact]
    public async Task ADetachedProgramTheLedgerNeverSaw_IsNotAServer()
    {
        _table.Add(300, 1, "dotnet /x/Snail.MCP.Memory.dll");

        Assert.Empty((await Kind.ObserveAsync([], CancellationToken.None)).Resources);
    }

    [Theory]
    [InlineData("dotnet /x/Snail.MCP.Memory.dll", "Snail.MCP.Memory")]
    [InlineData("/x/.snail-mcp/Snail.MCP.CleanUp/current/Snail.MCP.CleanUp", "Snail.MCP.CleanUp")]
    [InlineData("node --enable-source-maps /x/dist/index.js", "index")]
    [InlineData("npx -y @playwright/mcp", "mcp")]
    public void TheServerIsTheProgram_OrWhatARuntimeWasGiven(string command, string server) =>
        Assert.Equal(server, McpKind.ServerOf(new RunningProcess(1, 1, DateTimeOffset.UnixEpoch, command)));
}
