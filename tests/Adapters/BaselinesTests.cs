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

    public void Dispose()
    {
        _file.Dispose();
        _scratch.Dispose();
    }
}
