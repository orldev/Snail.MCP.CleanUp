using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.Toolkit.Cleanup.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class BaselinesTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly LedgerFile _file;

    public BaselinesTests() => _file = new LedgerFile(_scratch.File("ledger.db"));

    /// <summary>A baseline never changes once taken, so a second take does not move it, and it is read once.</summary>
    [Fact]
    public async Task Baseline_TakenOnce_IsWhatTheSessionBeganWith_AsLongAsItRuns()
    {
        var baselines = new SqliteBaselines(_file);

        await baselines.SaveAsync(Me, ["file:/a"], DateTimeOffset.UnixEpoch, CancellationToken.None);
        await baselines.SaveAsync(Me, ["file:/a", "file:/b"], DateTimeOffset.UnixEpoch, CancellationToken.None);

        var first = await baselines.FindAsync(Me, CancellationToken.None);

        Assert.NotNull(first);
        Assert.True(first.Contains("file:/a"));
        Assert.False(first.Contains("file:/b"));
        Assert.Same(first, await baselines.FindAsync(Me, CancellationToken.None));
    }

    /// <summary>
    /// A baseline is kept as hashes of its ids. Every id it was taken with is still in it, and none of a busy machine's
    /// worth of others is — the odds of a collision are in <see cref="Baseline"/>.
    /// </summary>
    [Fact]
    public async Task Baseline_KeptAsHashes_HoldsEveryIdItWasTakenWith_AndNoOther()
    {
        var baselines = new SqliteBaselines(_file);
        var taken = Enumerable.Range(0, 50_000).Select(index => $"file:/var/folders/v1/T/clr-debug-pipe-{index}-in").ToList();

        await baselines.SaveAsync(Me, taken, DateTimeOffset.UnixEpoch, CancellationToken.None);

        var found = await new SqliteBaselines(_file).FindAsync(Me, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(taken.Count + 1, found.Count);
        Assert.All(taken, id => Assert.True(found.Contains(id), id));
        Assert.DoesNotContain(Enumerable.Range(50_000, 50_000), index => found.Contains($"file:/var/folders/v1/T/clr-debug-pipe-{index}-in"));
        Assert.False(found.Contains("file:/var/folders/v1/T/clr-debug-pipe-0-out"));
    }

    [Fact]
    public async Task Baseline_OfASessionThatEnded_IsGone_FromTheFileAndFromWhatWasRead()
    {
        var baselines = new SqliteBaselines(_file);

        await baselines.SaveAsync(Me, ["file:/a"], DateTimeOffset.UnixEpoch, CancellationToken.None);
        await baselines.SaveAsync(Neighbour, ["file:/a"], DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.NotNull(await baselines.FindAsync(Me, CancellationToken.None));

        await baselines.KeepOnlyAsync(new HashSet<Owner> { Neighbour }, CancellationToken.None);

        Assert.Null(await baselines.FindAsync(Me, CancellationToken.None));
        Assert.Null(await new SqliteBaselines(_file).FindAsync(Me, CancellationToken.None));
        Assert.NotNull(await baselines.FindAsync(Neighbour, CancellationToken.None));
    }

    /// <summary>
    /// A server of before keeps a row per trace id: 413 453 rows and 115 MB on 2026-10-16. A server of this version opening
    /// the file moves them into one set per owner, and the baseline reads the same.
    /// </summary>
    [Fact]
    public async Task Baseline_WrittenAsRowsByAServerOfBefore_IsMovedIntoOneSet_AndReadTheSame()
    {
        await using (var connection = await _file.OpenAsync(CancellationToken.None))
        {
            foreach (var trace in new[] { "file:/a", "file:/b", "baseline:taken" })
                await connection.RunAsync("INSERT INTO baselines (owner, trace_id, taken_at) VALUES ($owner, $trace, $at);", CancellationToken.None, ("$owner", Me.ToString()), ("$trace", trace), ("$at", "2026-10-16T10:00:00.0000000+00:00"));
        }

        using var opened = new LedgerFile(_file.Path);

        var found = await new SqliteBaselines(opened).FindAsync(Me, CancellationToken.None);

        Assert.NotNull(found);
        Assert.True(found.Contains("file:/b"));
        Assert.Equal(0L, await CountAsync(opened, "baselines"));
        Assert.Equal(1L, await CountAsync(opened, "baseline_sets"));
    }

    /// <summary>A baseline a server of before took stays the one: this server neither writes a second nor reads another.</summary>
    [Fact]
    public async Task Baseline_TakenAsRowsByAServerRunningBeside_IsNotTakenAgain()
    {
        await using (var connection = await _file.OpenAsync(CancellationToken.None))
            await connection.RunAsync("INSERT INTO baselines (owner, trace_id, taken_at) VALUES ($owner, 'file:/a', '2026-10-16T10:00:00.0000000+00:00');", CancellationToken.None, ("$owner", Me.ToString()));

        var baselines = new SqliteBaselines(_file);

        await baselines.SaveAsync(Me, ["file:/a", "file:/b"], DateTimeOffset.UnixEpoch, CancellationToken.None);

        var found = await baselines.FindAsync(Me, CancellationToken.None);

        Assert.NotNull(found);
        Assert.False(found.Contains("file:/b"));
        Assert.Equal(0L, await CountAsync(_file, "baseline_sets"));
    }

    /// <summary>Fifty thousand ids, a busy machine's worth, are one row of eight bytes an id.</summary>
    [Fact]
    public async Task Baseline_OfFiftyThousandIds_IsOneRowOfEightBytesAnId()
    {
        var taken = Enumerable.Range(0, 50_000).Select(index => $"file:/var/folders/v1/T/clr-debug-pipe-{index}-in").ToList();

        await new SqliteBaselines(_file).SaveAsync(Me, taken, DateTimeOffset.UnixEpoch, CancellationToken.None);

        await using var connection = await _file.OpenAsync(CancellationToken.None);
        await using var size = connection.Prepared("SELECT length(hashes) FROM baseline_sets WHERE owner = $owner;", ("$owner", Me.ToString()));

        Assert.Equal((taken.Count + 1) * 8L, await size.ExecuteScalarAsync(CancellationToken.None));
        Assert.Equal(1L, await CountAsync(_file, "baseline_sets"));
    }

    private static async Task<long> CountAsync(LedgerFile file, string table)
    {
        await using var connection = await file.OpenAsync(CancellationToken.None);
        await using var count = connection.Prepared($"SELECT COUNT(*) FROM {table};");

        return (long)(await count.ExecuteScalarAsync(CancellationToken.None))!;
    }

    public void Dispose()
    {
        _file.Dispose();
        _scratch.Dispose();
    }
}
