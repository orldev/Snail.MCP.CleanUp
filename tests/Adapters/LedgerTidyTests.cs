using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Domain;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Adapters;

/// <summary>
/// The ledger of 2026-11-12: 89 MB, of which three plans of 17 MB each made on 2026-11-06 were 51 MB — crossed off only when
/// a next plan was saved, and nobody had planned since.
/// </summary>
public sealed class LedgerTidyTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UnixEpoch);

    private readonly LedgerFile _file;

    public LedgerTidyTests() => _file = new LedgerFile(_scratch.File("ledger.db"));

    public void Dispose()
    {
        _file.Dispose();
        _scratch.Dispose();
    }

    private Plan PlanOver(int resources, string id)
    {
        List<Resource> seen = [.. Enumerable.Range(0, resources).Select(index => Of(Kinds.File, $"/scratch/{index:D6}/{new string('x', 200)}", Me))];

        return Planner.Decide(Inventory(seen), Scope.Mine, id, MachineKinds.Order) with { MadeAt = _clock.GetUtcNow() };
    }

    [Fact]
    public async Task PlanPastKeeping_IsForgotten_WithoutANewPlanSaved()
    {
        var plans = new SqlitePlans(_file, _clock);

        await plans.SaveAsync(PlanOver(3, "old"), CancellationToken.None);
        _clock.Advance(SqlitePlans.Kept + TimeSpan.FromHours(1));

        var tidied = await new LedgerTidy(_file, _clock).TidyAsync(CancellationToken.None);

        Assert.Equal(1, tidied.Plans);
        Assert.Null(await plans.FindAsync("old", CancellationToken.None));
    }

    [Fact]
    public async Task PlanStillKept_StaysFound()
    {
        var plans = new SqlitePlans(_file, _clock);

        await plans.SaveAsync(PlanOver(3, "fresh"), CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(30));

        await new LedgerTidy(_file, _clock).TidyAsync(CancellationToken.None);

        Assert.NotNull(await plans.FindAsync("fresh", CancellationToken.None));
    }

    /// <summary>A file whose free pages are many, and most of it, is compacted and gives them back to the disk.</summary>
    [Fact]
    public async Task FileMostlyFree_IsCompacted()
    {
        var plans = new SqlitePlans(_file, _clock);

        await plans.SaveAsync(PlanOver(4_000, "large"), CancellationToken.None);
        _clock.Advance(SqlitePlans.Kept + TimeSpan.FromHours(1));

        var before = await PagesAsync();
        var tidied = await new LedgerTidy(_file, _clock, mostFree: 64 * 1024).TidyAsync(CancellationToken.None);
        var after = await PagesAsync();

        Assert.True(tidied.Bytes > 0, $"nothing was given back of a file of {before} pages");
        Assert.True(after < before / 2, $"the file went from {before} to {after} pages");
    }

    /// <summary>How many pages the file has; its length on disk waits for a checkpoint of the write-ahead log.</summary>
    private async Task<long> PagesAsync()
    {
        await using var connection = await _file.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();

        command.CommandText = "PRAGMA page_count;";

        var pages = await command.ExecuteScalarAsync(CancellationToken.None);

        return pages is long count ? count : 0;
    }

    /// <summary>A file with a few free pages is left as it is: compacting rewrites the whole file, for nothing.</summary>
    [Fact]
    public async Task FileWithFewFreePages_IsLeftAsItIs()
    {
        var plans = new SqlitePlans(_file, _clock);

        await plans.SaveAsync(PlanOver(3, "small"), CancellationToken.None);

        var tidied = await new LedgerTidy(_file, _clock).TidyAsync(CancellationToken.None);

        Assert.Equal(new Tidied(0, 0), tidied);
    }
}
