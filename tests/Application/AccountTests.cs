using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Core.Adapters;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Application;

public sealed class AccountTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly LedgerFile _file;

    private readonly FakeKind _processes = new(Kinds.Process);

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UnixEpoch.AddDays(1));

    private readonly FakeVolume _volume = new();

    public AccountTests() => _file = new LedgerFile(_scratch.File("ledger.db"));

    private Account Account
    {
        get
        {
            var sessions = new FakeSessions(Me, Alive(Me), Alive(Neighbour), Dead(Gone));
            var ledger = new SqliteLedger(_file);
            var rules = new JsonRules(_scratch.File("rules.json"), []);
            var discoveries = new SqliteDiscoveries(_file);
            var census = new Census([_processes], [], sessions, ledger, rules, new SqliteBaselines(_file), discoveries, _clock);

            var config = new ServerConfig { DataDirectory = _scratch.Path };
            var trend = new DiskTrend(_volume, new SqliteDiskHistory(_file), config, _clock);

            return new Account(new MeasuredCensus(census, new Performance(_clock), trend), sessions, new SqlitePlans(_file, _clock), new Cleaner([_processes], ledger), ledger, discoveries, rules, trend, config, _clock);
        }
    }

    [Fact]
    public async Task Plan_ShownThenApplied_RemovesWhatItShowed_AndNothingElse()
    {
        _processes.Present.Add(Of(Kinds.Process, "1@1", Me, Evidence.Ancestry));
        _processes.Present.Add(Of(Kinds.Process, "2@1", Neighbour, Evidence.Ancestry));

        var plan = await Account.PlanAsync(Scope.Mine, CancellationToken.None);
        var planId = plan.Data!["planId"]!.GetValue<string>();

        _processes.Present.Add(Of(Kinds.Process, "3@1", Me, Evidence.Ancestry));

        var applied = await Account.ApplyAsync(planId, null, CancellationToken.None);

        Assert.Equal(1, applied.Data!["released"]!.GetValue<int>());
        Assert.Equal(["process:1@1"], _processes.Released);
    }

    /// <summary>A running session's scratchpad past the size is the first thing both answers say.</summary>
    [Fact]
    public async Task Inventory_AndPlan_WarnOfARunningSessionsScratchpadPastTheSize()
    {
        _processes.Present.Add(Of(Kinds.File, "/private/tmp/claude-501/p/me", Me, Evidence.Label) with
        {
            Bytes = 12_000_000_000,
            Facts = new Dictionary<string, string> { [Snail.MCP.CleanUp.Adapters.Resources.FileKind.SessionFact] = Me.Id }
        });

        var inventory = await Account.InventoryAsync(CancellationToken.None);
        var plan = await Account.PlanAsync(Scope.Orphans, CancellationToken.None);

        Assert.Contains("running session me holds 12 GB", inventory.Data!["warnings"]![0]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("running session me holds 12 GB", plan.Data!["warnings"]![0]!.GetValue<string>(), StringComparison.Ordinal);
    }

    /// <summary>Every census writes the free space down; the inventory of a day later says how far it fell, and what grew.</summary>
    [Fact]
    public async Task Inventory_AfterTheDiskFellFarWithinADay_SaysSo_AndNamesWhatGrew()
    {
        var scratchpad = Of(Kinds.File, "/private/tmp/claude-501/p/me", Me, Evidence.Label) with
        {
            Bytes = 2_000_000_000,
            Facts = new Dictionary<string, string> { [Snail.MCP.CleanUp.Adapters.Resources.FileKind.SessionFact] = Me.Id }
        };

        _processes.Present.Add(scratchpad);
        await Account.InventoryAsync(CancellationToken.None);

        _clock.Advance(TimeSpan.FromHours(20));
        _volume.Free -= 50_000_000_000;
        _processes.Present[0] = scratchpad with { Bytes = 49_000_000_000 };

        var disk = (await Account.InventoryAsync(CancellationToken.None)).Data!["disk"]!;

        Assert.Equal("50 GB", disk["fellWithinADay"]!.GetValue<string>());
        Assert.Contains("fell by 50 GB within a day", disk["trend"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("47 GB", disk["growers"]![0]!["grew"]!.GetValue<string>());
    }

    [Fact]
    public async Task Inventory_WithNothingToWarnOf_HasNoWarnings() =>
        Assert.Null((await Account.InventoryAsync(CancellationToken.None)).Data!["warnings"]);

    [Fact]
    public async Task Plan_CarriedOutAfterItWentStale_DoesNothing()
    {
        _processes.Present.Add(Of(Kinds.Process, "1@1", Me, Evidence.Ancestry));

        var planId = (await Account.PlanAsync(Scope.Mine, CancellationToken.None)).Data!["planId"]!.GetValue<string>();

        _clock.Advance(Plan.Lifetime + TimeSpan.FromMinutes(1));

        var applied = await Account.ApplyAsync(planId, null, CancellationToken.None);

        Assert.False(applied.IsSuccess);
        Assert.Contains("stale", applied.Error, StringComparison.Ordinal);
        Assert.Empty(_processes.Released);
    }

    [Fact]
    public async Task Release_OfSomethingNotProvenAnAgents_IsRefusedWithTheReason()
    {
        _processes.Present.Add(Of(Kinds.Process, "9@1", Me, Evidence.Appeared));

        var released = await Account.ReleaseAsync("process:9@1", CancellationToken.None);

        Assert.False(released.IsSuccess);
        Assert.Contains("not proof", released.Error, StringComparison.Ordinal);
        Assert.Empty(_processes.Released);
    }

    [Fact]
    public async Task Release_OfMyOwn_HappensAtOnce()
    {
        _processes.Present.Add(Of(Kinds.Process, "1@1", Me, Evidence.Ledger));

        Assert.True((await Account.ReleaseAsync("process:1@1", CancellationToken.None)).IsSuccess);
        Assert.Equal(["process:1@1"], _processes.Released);
    }

    [Fact]
    public async Task Keep_WithoutAReason_IsRefused_AndAKeptThingSurvivesThePlan()
    {
        _processes.Present.Add(Of(Kinds.Process, "1@1", Me, Evidence.Ledger));

        Assert.False((await Account.KeepAsync("process:1@1", null, withdraw: false, CancellationToken.None)).IsSuccess);
        Assert.True((await Account.KeepAsync("process:1@1", "my dev server", withdraw: false, CancellationToken.None)).IsSuccess);

        var plan = await Account.PlanAsync(Scope.Mine, CancellationToken.None);

        Assert.Equal(0, plan.Data!["remove"]!["count"]!.GetValue<int>());
        Assert.Equal(1, plan.Data!["keep"]!["count"]!.GetValue<int>());
    }

    [Fact]
    public async Task Learn_OnlyIgnores_TrailsOtherThanFiles()
    {
        var refused = await Account.LearnAsync(new Rule("port", "3000", RuleAction.Cache), CancellationToken.None);
        var learned = await Account.LearnAsync(new Rule("port", "3000", RuleAction.Ignore), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.True(learned.IsSuccess);
    }

    public void Dispose()
    {
        _file.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _scratch.Dispose();
    }
}
