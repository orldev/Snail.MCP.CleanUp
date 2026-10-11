using Snail.MCP.CleanUp.Core.Adapters;
using Snail.MCP.CleanUp.Core.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class LedgerTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly LedgerFile _file;

    public LedgerTests() => _file = new LedgerFile(_scratch.File("ledger.db"));

    private SqliteLedger Ledger => new(_file);

    [Fact]
    public async Task Holding_Recorded_IsReadBackWithItsOwnerAndEvidence()
    {
        var holding = new Holding("process:7@1", Me, Evidence.Ancestry, DateTimeOffset.UnixEpoch, "node server");

        await Ledger.RecordAsync(holding, CancellationToken.None);

        Assert.Equal(holding, Assert.Single(await Ledger.HoldingsAsync(CancellationToken.None)));
    }

    /// <summary>A process this server started stays recorded as such when a later census only sees it as a child.</summary>
    [Fact]
    public async Task Holding_WithWeakerEvidence_DoesNotOverwriteAStrongerOne()
    {
        await Ledger.RecordAsync(new Holding("process:7@1", Me, Evidence.Ledger, DateTimeOffset.UnixEpoch, "started"), CancellationToken.None);
        await Ledger.RecordAsync(new Holding("process:7@1", Neighbour, Evidence.Ancestry, DateTimeOffset.UnixEpoch, "seen"), CancellationToken.None);

        var holding = Assert.Single(await Ledger.HoldingsAsync(CancellationToken.None));

        Assert.Equal(Me, holding.Owner);
        Assert.Equal(Evidence.Ledger, holding.Evidence);
    }

    [Fact]
    public async Task Holding_Forgotten_IsGone()
    {
        await Ledger.RecordAsync(new Holding("file:/x", Me, Evidence.Ledger, DateTimeOffset.UnixEpoch, "x"), CancellationToken.None);
        await Ledger.ForgetAsync("file:/x", CancellationToken.None);

        Assert.Empty(await Ledger.HoldingsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Keeping_IsRememberedUntilWithdrawn()
    {
        await Ledger.KeepAsync("process:7@1", "my dev server", CancellationToken.None);

        Assert.Equal("my dev server", (await Ledger.KeptAsync(CancellationToken.None))["process:7@1"]);
        Assert.True(await Ledger.UnkeepAsync("process:7@1", CancellationToken.None));
        Assert.False(await Ledger.UnkeepAsync("process:7@1", CancellationToken.None));
        Assert.Empty(await Ledger.KeptAsync(CancellationToken.None));
    }

    /// <summary>Every session runs its own server over the same file; what one writes, the next one reads.</summary>
    [Fact]
    public async Task Ledger_WrittenByOneServer_IsReadByAnother()
    {
        using var other = new LedgerFile(_file.Path);

        await Ledger.RecordAsync(new Holding("simulator:udid", Me, Evidence.Label, DateTimeOffset.UnixEpoch, "clone"), CancellationToken.None);

        Assert.Single(await new SqliteLedger(other).HoldingsAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        _file.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _scratch.Dispose();
    }
}
