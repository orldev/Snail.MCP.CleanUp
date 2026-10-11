using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Trails;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Tests.Machine;

public sealed class TrailsOnThisMacTests
{
    /// <summary>A census runs every 30 seconds in every session; following all the trails has to stay a small part of that.</summary>
    [MachineFact]
    public async Task EveryTrail_OnThisMac_IsFollowedInAFewSeconds()
    {
        var programs = new Programs();
        var table = new ProcessTable(programs, new FakeTimeProvider(DateTimeOffset.UtcNow));
        var timer = Stopwatch.StartNew();

        var files = await new FileTrail().FollowAsync(MacPlaces.Shipped, CancellationToken.None);
        var processes = await new ProcessTrail(table).FollowAsync([], CancellationToken.None);
        var ports = await new PortTrail(programs).FollowAsync([], CancellationToken.None);
        var crontab = await new CrontabTrail(programs).FollowAsync([], CancellationToken.None);

        Assert.NotEmpty(files);
        Assert.NotEmpty(ports);
        Assert.InRange(timer.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        Assert.True(files.Count < FileTrail.MostEntries, $"{files.Count} files, {processes.Count} processes, {crontab.Count} cron lines");
    }
}
