using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Core.Adapters;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class PlansTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly LedgerFile _file;

    public PlansTests() => _file = new LedgerFile(_scratch.File("ledger.db"));

    [Fact]
    public async Task Plan_Saved_IsFoundWithItsDecisionsInTheirOrder()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var plans = new SqlitePlans(_file, clock);
        var resource = Of(Kinds.Process, "7@1", Me) with { After = ["file:/x"], Facts = new Dictionary<string, string> { ["pid"] = "7" } };
        var plan = Planner.Decide(Inventory([resource, Of(Kinds.File, "/x", Me)]), Scope.Mine, "p1");

        await plans.SaveAsync(plan, CancellationToken.None);

        var found = await plans.FindAsync("p1", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(Me, found.Asker);
        Assert.Equal(plan.Decisions.Select(decision => decision.Resource.Id), found.Decisions.Select(decision => decision.Resource.Id));
        Assert.Equal("7", found.Decisions[1].Resource.Facts["pid"]);
        Assert.Equal(Evidence.Ledger, found.Decisions[1].Resource.Evidence);
    }

    [Fact]
    public async Task Plan_NeverSaved_IsNotFound() =>
        Assert.Null(await new SqlitePlans(_file, TimeProvider.System).FindAsync("nothing", CancellationToken.None));

    public void Dispose()
    {
        _file.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _scratch.Dispose();
    }
}
