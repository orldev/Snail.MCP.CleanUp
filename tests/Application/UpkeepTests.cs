using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Domain;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Application;

/// <summary>
/// The one sweep that came by itself, at a session's end, left the session's scratchpad written moments before, and nothing
/// came back for it: 152 scratchpads, 29 GB of Docker's build cache and 11 001 endpoints of ended processes waited for
/// somebody to ask in twelve days (2026-10-24). The server's own sweep clears what may go without asking.
/// </summary>
public sealed class UpkeepTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_792_800_000);

    private readonly Scratch _scratch = new();

    private readonly FakeTimeProvider _clock = new(Now);

    private readonly FakeKind _processes = new(Kinds.Process);

    private readonly FakeKind _files = new(Kinds.File);

    private readonly FakeKind _remains = new(MachineKinds.Remains);

    private readonly FakeKind _buildCache = new(MachineKinds.BuildCache);

    public void Dispose() => _scratch.Dispose();

    private ServerConfig Config => new() { DataDirectory = _scratch.Path };

    private Upkeep UpkeepOf(ServerConfig config) =>
        new(new Cleaner([_processes, _files, _remains, _buildCache], new NoLedger()), new SweepLog(config, _clock), config, _clock);

    private static Inventory InventoryOf(IReadOnlyList<Resource> resources, params Holder[] holders) =>
        new(Now, resources, holders) { Asker = Me };

    private static Resource Cache(string kind, long bytes) =>
        Of(kind, "machine") with { IsShared = true, IsRegenerable = true, Bytes = bytes };

    [Fact]
    public async Task Orphan_OfASessionOverForLongerThanTheGrace_IsReleased()
    {
        var server = Of(Kinds.Process, "7@1", Gone, Evidence.Environment);
        var inventory = InventoryOf([server], Alive(Me), Dead(Gone) with { EndedAt = Now - TimeSpan.FromHours(2) });

        var releases = await UpkeepOf(Config).SweepAsync(inventory, CancellationToken.None);

        Assert.Equal(Outcome.Released, Assert.Single(releases).Outcome);
        Assert.Equal([server.Id], _processes.Released);
    }

    /// <summary>A session resumed within the hour finds what it left where it left it.</summary>
    [Fact]
    public async Task Orphan_OfASessionOverForLessThanTheGrace_IsLeft()
    {
        var server = Of(Kinds.Process, "7@1", Gone, Evidence.Environment);
        var inventory = InventoryOf([server], Alive(Me), Dead(Gone) with { EndedAt = Now - TimeSpan.FromMinutes(10) });

        Assert.Empty(await UpkeepOf(Config).SweepAsync(inventory, CancellationToken.None));
        Assert.Empty(_processes.Released);
    }

    /// <summary>What the asker holds and what a running session holds are theirs to ask for, never the server's to clear.</summary>
    [Fact]
    public async Task WhatTheAskerOrARunningSessionHolds_IsNeverCleared()
    {
        Resource[] held = [Of(Kinds.Process, "7@1", Me, Evidence.Ancestry), Of(Kinds.Process, "8@1", Neighbour, Evidence.Ancestry)];
        var inventory = InventoryOf(held, Alive(Me), Alive(Neighbour));

        Assert.Empty(await UpkeepOf(Config).SweepAsync(inventory, CancellationToken.None));
        Assert.Empty(_processes.Released);
    }

    /// <summary>A scratchpad goes only once it is erased rather than trashed: the Trash would give no space back.</summary>
    [Fact]
    public async Task Scratchpad_OfAnEndedSession_IsClearedOnlyOnceItIsErased()
    {
        var ended = Dead(Gone) with { EndedAt = Now - TimeSpan.FromDays(3) };
        var young = Of(Kinds.File, "/private/tmp/claude-501/p/young", Gone, Evidence.Label);
        var erasable = Of(Kinds.File, "/private/tmp/claude-501/p/old", Gone, Evidence.Label) with { IsRegenerable = true };

        await UpkeepOf(Config).SweepAsync(InventoryOf([young, erasable], Alive(Me), ended), CancellationToken.None);

        Assert.Equal([erasable.Id], _files.Released);
    }

    [Fact]
    public async Task Remains_AreAlwaysCleared_DockersBuildCacheOnlyPastItsThreshold()
    {
        var config = Config;

        config.DockerCacheSweepGigabytes = 10;

        Resource[] small = [Cache(MachineKinds.Remains, 0), Cache(MachineKinds.BuildCache, 5_000_000_000)];

        await UpkeepOf(config).SweepAsync(InventoryOf(small, Alive(Me)), CancellationToken.None);

        Assert.Single(_remains.Released);
        Assert.Empty(_buildCache.Released);

        await UpkeepOf(config).SweepAsync(InventoryOf([Cache(MachineKinds.BuildCache, 12_000_000_000)], Alive(Me)), CancellationToken.None);

        Assert.Single(_buildCache.Released);
    }

    /// <summary>Other caches — an editor's state, test results, a cache a rule names — are left to a plan somebody asks for.</summary>
    [Fact]
    public async Task OtherCaches_AreLeftToAPlanSomebodyAsksFor()
    {
        var editor = new FakeKind(MachineKinds.Editor);
        var upkeep = new Upkeep(new Cleaner([editor], new NoLedger()), new SweepLog(Config, _clock), Config, _clock);

        Assert.Empty(await upkeep.SweepAsync(InventoryOf([Cache(MachineKinds.Editor, 1_000)], Alive(Me)), CancellationToken.None));
    }

    [Fact]
    public async Task Upkeep_IsDueOnceAnInterval_AndNeverWhenItsMinutesAreZero()
    {
        var config = Config;
        var upkeep = UpkeepOf(config);

        Assert.True(upkeep.IsDue);

        await upkeep.SweepAsync(InventoryOf([], Alive(Me)), CancellationToken.None);

        Assert.False(upkeep.IsDue);

        _clock.Advance(TimeSpan.FromMinutes(config.SweepMinutes));

        Assert.True(upkeep.IsDue);

        config.SweepMinutes = 0;

        Assert.False(upkeep.IsDue);
    }

    /// <summary>The sweep log says what the server cleared by itself, and stays quiet when it cleared nothing.</summary>
    [Fact]
    public async Task SweepLog_NamesWhatWasCleared_AndNothingWhenNothingWas()
    {
        var log = Path.Combine(_scratch.Path, SweepLog.FileName);

        await UpkeepOf(Config).SweepAsync(InventoryOf([], Alive(Me)), CancellationToken.None);

        Assert.False(File.Exists(log));

        await UpkeepOf(Config).SweepAsync(InventoryOf([Cache(MachineKinds.Remains, 0)], Alive(Me)), CancellationToken.None);

        Assert.Contains("upkeep", File.ReadAllText(log), StringComparison.Ordinal);
        Assert.Contains("released remains:machine", File.ReadAllText(log), StringComparison.Ordinal);
    }
}
