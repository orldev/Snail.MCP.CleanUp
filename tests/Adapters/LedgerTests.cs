using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.Toolkit.Cleanup.Domain;
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

    /// <summary>The environment came after the label in the order of strength, and before it in no row: rows written before keep what they said.</summary>
    [Fact]
    public async Task Evidence_IsStoredUnderCodesThatNeverMove_AndComparedByStrength()
    {
        await using (var connection = await _file.OpenAsync(CancellationToken.None))
        {
            await using var command = connection.CreateCommand();

            command.CommandText = "INSERT INTO holdings VALUES ('file:/label', 'session:me', 3, '1970-01-01T00:00:00.0000000Z', 'l'), ('process:1@1', 'session:me', 4, '1970-01-01T00:00:00.0000000Z', 'p');";
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await Ledger.RecordAsync(new Holding("process:1@1", Neighbour, Evidence.Environment, DateTimeOffset.UnixEpoch, "seen"), CancellationToken.None);
        await Ledger.RecordAsync(new Holding("process:2@1", Me, Evidence.Ancestry, DateTimeOffset.UnixEpoch, "child"), CancellationToken.None);
        await Ledger.RecordAsync(new Holding("process:2@1", Neighbour, Evidence.Environment, DateTimeOffset.UnixEpoch, "carried"), CancellationToken.None);
        await Ledger.RecordAsync(new Holding("process:2@1", Me, Evidence.Ancestry, DateTimeOffset.UnixEpoch, "child again"), CancellationToken.None);

        var holdings = (await Ledger.HoldingsAsync(CancellationToken.None)).ToDictionary(holding => holding.ResourceId);

        Assert.Equal(Evidence.Label, holdings["file:/label"].Evidence);
        Assert.Equal((Me, Evidence.Ledger), (holdings["process:1@1"].Owner, holdings["process:1@1"].Evidence));
        Assert.Equal((Neighbour, Evidence.Environment), (holdings["process:2@1"].Owner, holdings["process:2@1"].Evidence));
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
        Assert.True(await Ledger.TryUnkeepAsync("process:7@1", CancellationToken.None));
        Assert.False(await Ledger.TryUnkeepAsync("process:7@1", CancellationToken.None));
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

    /// <summary>A file written before discoveries said «at least» gains the column, keeps its rows, and says it from then on.</summary>
    [Fact]
    public async Task Discovery_CountedShort_IsReadBackAsAtLeast_FromAFileWrittenBeforeThat()
    {
        await using (var before = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_file.Path};Pooling=False"))
        {
            await before.OpenAsync();

            await using var schema = before.CreateCommand();

            schema.CommandText =
                """
                CREATE TABLE discoveries (trace_id TEXT PRIMARY KEY, trail TEXT NOT NULL, key TEXT NOT NULL, title TEXT NOT NULL, bytes INTEGER NULL,
                    owner TEXT NOT NULL, first_seen TEXT NOT NULL, last_seen TEXT NOT NULL, sightings INTEGER NOT NULL);
                INSERT INTO discoveries VALUES ('file:/tmp/old', 'file', '/tmp/old', '/tmp/old', 5, 'session:me', '2026-10-13T00:00:00.0000000+00:00', '2026-10-13T00:00:00.0000000+00:00', 1);
                """;
            await schema.ExecuteNonQueryAsync();
        }

        var now = DateTimeOffset.Parse("2026-10-13T00:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture);
        var discoveries = new SqliteDiscoveries(_file);
        var big = new Trace("file", "/tmp/big", "/tmp/big", 3_760_000_000) { IsAtLeast = true };

        await discoveries.NoteAsync([new Discovery(big, Me, now, now, 1)], CancellationToken.None);

        var read = (await discoveries.ListAsync(new HashSet<string>(["file:/tmp/old", "file:/tmp/big"]), now, CancellationToken.None)).ToDictionary(discovery => discovery.Trace.Key);

        Assert.True(read["/tmp/big"].Trace.IsAtLeast);
        Assert.Equal(3_760_000_000, read["/tmp/big"].Trace.Bytes);
        Assert.False(read["/tmp/old"].Trace.IsAtLeast);
        Assert.Equal(5, read["/tmp/old"].Trace.Bytes);
    }

    /// <summary>
    /// A file let go closes the connections kept for it and only those: clearing every pool of the process disposed a
    /// connection another test was opening on a file of its own.
    /// </summary>
    [Fact]
    public async Task File_LetGo_ClosesItsOwnConnections_AndNoOtherFiles()
    {
        var let = new LedgerFile(_scratch.File("let.db"));

        await Ledger.RecordAsync(new Holding("process:7@1", Me, Evidence.Ancestry, DateTimeOffset.UnixEpoch, "kept"), CancellationToken.None);
        await new SqliteLedger(let).RecordAsync(new Holding("process:8@1", Me, Evidence.Ancestry, DateTimeOffset.UnixEpoch, "let go"), CancellationToken.None);

        let.Dispose();

        Assert.False(File.Exists($"{let.Path}-wal"));
        Assert.True(File.Exists($"{_file.Path}-wal"));
        Assert.Single(await Ledger.HoldingsAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        _file.Dispose();
        _scratch.Dispose();
    }
}
