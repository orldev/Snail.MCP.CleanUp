using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Application.Ports;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Domain;
using Snail.Toolkit.Cleanup.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Application;

/// <summary>The day of 2026-10-13: 50 GB of free space gone, most of it into one running session's scratchpad.</summary>
public sealed class DiskTrendTests : IDisposable
{
    private const long Gigabyte = 1_000_000_000;

    private readonly Scratch _scratch = new();

    private readonly LedgerFile _file;

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero));

    private readonly FakeVolume _volume = new();

    public DiskTrendTests() => _file = new LedgerFile(_scratch.File("ledger.db"));

    private SqliteDiskHistory History => new(_file);

    private DiskTrend Trend(double gigabytes = 20) => new(_volume, History, new ServerConfig { DiskFallWarningGigabytes = gigabytes }, _clock);

    private static Resource Scratchpad(long bytes) => Of(Kinds.File, "/private/tmp/claude-501/p/s1", Me, Evidence.Label) with
    {
        Title = "scratchpad of session s1",
        Bytes = bytes,
        Facts = new Dictionary<string, string> { [Snail.MCP.CleanUp.Adapters.Resources.FileKind.SessionFact] = "s1" }
    };

    private static Resource Cache(string key, long bytes) => Of(Kinds.File, key) with { Title = key, Bytes = bytes, IsShared = true, IsRegenerable = true };

    private static Resource Results(long bytes) => Of(MachineKinds.TestResults, "/repo/TestResults") with { Title = "test results of repo", Bytes = bytes, IsShared = true };

    private async Task CensusAsync(params Resource[] resources) => Assert.True(await Trend().TryRecordAsync(Inventory(resources), CancellationToken.None));

    [Fact]
    public async Task FreeSpace_FallenPastTheSettingWithinADay_IsAWarning_NamingWhatGrewMost()
    {
        await CensusAsync(Scratchpad(2 * Gigabyte), Cache("/caches/a", 5 * Gigabyte), Results(Gigabyte));
        _clock.Advance(TimeSpan.FromHours(12));
        _volume.Free -= 52 * Gigabyte;
        await CensusAsync(Scratchpad(50 * Gigabyte), Cache("/caches/a", 5 * Gigabyte), Results(3 * Gigabyte));

        var report = await Trend().ReportAsync(CancellationToken.None);
        var growers = report["growers"]!.AsArray();

        Assert.Equal("52 GB", report["fellWithinADay"]!.GetValue<string>());
        Assert.Contains("scratchpad of session s1 +48 GB; test results of repo +2 GB", report["trend"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(2, growers.Count);
        Assert.Equal("50 GB", growers[0]!["size"]!.GetValue<string>());
    }

    [Fact]
    public async Task FreeSpace_FallenLessThanTheSetting_IsSaid_ButNoWarning()
    {
        await CensusAsync(Scratchpad(2 * Gigabyte));
        _clock.Advance(TimeSpan.FromHours(3));
        _volume.Free -= 5 * Gigabyte;

        var report = await Trend().ReportAsync(CancellationToken.None);

        Assert.Equal("5 GB", report["fellWithinADay"]!.GetValue<string>());
        Assert.Null(report["trend"]);
        Assert.NotNull((await Trend(4).ReportAsync(CancellationToken.None))["trend"]);
    }

    [Fact]
    public async Task Fall_FromMoreThanADayAgo_IsNotThisDays()
    {
        await CensusAsync();
        _clock.Advance(TimeSpan.FromHours(30));
        _volume.Free -= 60 * Gigabyte;

        var report = await Trend().ReportAsync(CancellationToken.None);

        Assert.Null(report["fellWithinADay"]);
        Assert.Null(report["trend"]);
    }

    [Fact]
    public async Task Fall_WithNothingMeasuredGrowing_SaysToLookOutside()
    {
        await CensusAsync();
        _clock.Advance(TimeSpan.FromHours(1));
        _volume.Free -= 30 * Gigabyte;

        Assert.Contains("lies outside", (await Trend().ReportAsync(CancellationToken.None))["trend"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    /// <summary>One row of free space per ten minutes for two weeks, sizes per hour for two days: bounded whatever the census rate.</summary>
    [Fact]
    public async Task History_KeepsARowPerStep_AndForgetsWhatIsOlderThanItIsKept()
    {
        await CensusAsync(Scratchpad(Gigabyte));

        for (var census = 0; census < 30; census++)
        {
            _clock.Advance(TimeSpan.FromSeconds(10));
            await CensusAsync(Scratchpad(Gigabyte + census));
        }

        Assert.Single(await History.FreeSinceAsync(DateTimeOffset.MinValue, CancellationToken.None));
        Assert.Equal(Gigabyte + 29, Assert.Single(await History.SizesSinceAsync(DateTimeOffset.MinValue, CancellationToken.None)).Bytes);

        _clock.Advance(TimeSpan.FromDays(3));
        await CensusAsync();

        Assert.Equal(2, (await History.FreeSinceAsync(DateTimeOffset.MinValue, CancellationToken.None)).Count);
        Assert.Empty(await History.SizesSinceAsync(DateTimeOffset.MinValue, CancellationToken.None));

        _clock.Advance(TimeSpan.FromDays(14));
        await CensusAsync();

        Assert.Single(await History.FreeSinceAsync(DateTimeOffset.MinValue, CancellationToken.None));
    }

    [Fact]
    public async Task Growers_AreTheLargestScratchpadsTestResultsAndCaches_PastAHundredMegabytes()
    {
        var process = Of(Kinds.Process, "1@1", Me) with { Bytes = 9 * Gigabyte };
        var caches = Enumerable.Range(0, 40).Select(index => Cache($"/caches/{index}", (index + 1) * 200_000_000L));

        await CensusAsync([Scratchpad(50_000_000), process, Results(Gigabyte), .. caches]);

        var sizes = await History.SizesSinceAsync(DateTimeOffset.MinValue, CancellationToken.None);

        Assert.Equal(DiskTrend.MostGrowers, sizes.Count);
        Assert.DoesNotContain(sizes, size => size.ResourceId.StartsWith("process:", StringComparison.Ordinal));
        Assert.DoesNotContain(sizes, size => size.ResourceId == "file:/private/tmp/claude-501/p/s1");
        Assert.Equal("file:/caches/39", sizes.MaxBy(size => size.Bytes)!.ResourceId);
    }

    [Fact]
    public async Task History_ThatCannotBeWritten_LeavesTheCensusBe()
    {
        var trend = new DiskTrend(_volume, new Unwritable(), new ServerConfig(), _clock);

        Assert.False(await trend.TryRecordAsync(Inventory([]), CancellationToken.None));
    }

    [Fact]
    public async Task Volume_ThatCannotBeRead_IsANote()
    {
        _volume.IsReadable = false;

        Assert.NotNull((await Trend().ReportAsync(CancellationToken.None))["note"]);
        Assert.False(await Trend().TryRecordAsync(Inventory([]), CancellationToken.None));
    }

    public void Dispose()
    {
        _file.Dispose();
        _scratch.Dispose();
    }

    private sealed class Unwritable : IDiskHistory
    {
        public Task RecordAsync(DiskSample disk, IReadOnlyList<SizeSample> sizes, CancellationToken cancellationToken) =>
            throw new IOException("disk full");

        public Task<IReadOnlyList<DiskSample>> FreeSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DiskSample>>([]);

        public Task<IReadOnlyList<SizeSample>> SizesSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SizeSample>>([]);
    }
}
