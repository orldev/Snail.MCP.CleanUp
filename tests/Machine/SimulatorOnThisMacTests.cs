using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Domain;
using Snail.MCP.CleanUp.Extensions;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Application.Ports;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Tests.Machine;

/// <summary>A real simulator, made for this session, booted, found as its own and deleted by its own cleanup.</summary>
[Collection(OnThisMac.Name)]
public sealed class SimulatorOnThisMacTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private string? _udid;

    [MachineFact]
    public async Task SimulatorLeasedAndBooted_IsMine_AndMyCleanupShutsItDownAndDeletesIt()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services
            .AddSingleton(new ServerConfig { DataDirectory = _scratch.Path, TrashDirectory = _scratch.File("Trash"), ScratchRoot = _scratch.File("tmp") })
            .AddCleanUp();

        using var host = builder.Build();
        var services = host.Services;
        var leased = await services.GetRequiredService<Leases>().SimulatorAsync(null, boot: true, CancellationToken.None);

        Assert.True(leased.IsSuccess, leased.Error);

        _udid = leased.Data!["udid"]!.GetValue<string>();

        var me = await services.GetRequiredService<IHolders>().CurrentAsync(CancellationToken.None);
        var inventory = await services.GetRequiredService<Census>().TakeAsync(me, CancellationToken.None);
        var plan = Planner.Decide(inventory, Scope.Mine, "machine", MachineKinds.Order);
        var seen = inventory.Resources.Where(resource => resource.Kind == MachineKinds.Simulator).Select(resource => $"{resource.Title} {resource.Owner} {resource.Evidence} {resource.Blocker}");

        Assert.True(
            plan.Removals.Any(decision => decision.Resource.Key == _udid),
            $"me {me}; notes {string.Join("; ", inventory.Notes.Select(note => $"{note.Key}: {note.Value}"))}; simulators {string.Join(" | ", seen)}; decisions {string.Join(" | ", plan.Decisions.Select(decision => $"{decision.Resource.Id} {decision.Verdict} {decision.Reason}"))}");

        var removal = plan.Removals.Single(decision => decision.Resource.Key == _udid);
        var releases = await services.GetRequiredService<Cleaner>().ApplyAsync(plan with { Decisions = [removal] }, CancellationToken.None);

        Assert.True(leased.Data!["isBooted"]!.GetValue<bool>());
        Assert.Equal(Outcome.Released, Assert.Single(releases).Outcome);
        Assert.Contains("after shutting it down", releases[0].Detail, StringComparison.Ordinal);

        _udid = null;
    }

    /// <summary>A real device.plist is binary and keeps a date, which plutil cannot write as JSON: read as XML, the device comes through.</summary>
    [MachineFact]
    public async Task DevicePlist_BinaryWithADate_IsReadWhileTheServiceIsDown()
    {
        var udid = Guid.NewGuid().ToString("D").ToUpperInvariant();
        var folder = Directory.CreateDirectory(_scratch.File($"Devices/{udid}")).FullName;
        var plist = Path.Combine(folder, "device.plist");
        var programs = new Programs(new Performance(TimeProvider.System), TimeProvider.System);

        await File.WriteAllTextAsync(plist, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0">
            <dict>
            	<key>UDID</key>
            	<string>{udid}</string>
            	<key>isDeleted</key>
            	<false/>
            	<key>lastBootedAt</key>
            	<date>2026-10-11T09:00:00Z</date>
            	<key>name</key>
            	<string>snail-machine-test-1</string>
            	<key>runtime</key>
            	<string>com.apple.CoreSimulator.SimRuntime.iOS-27-0</string>
            	<key>state</key>
            	<integer>1</integer>
            </dict>
            </plist>
            """);
        Assert.True((await programs.RunAsync("plutil", ["-convert", "binary1", plist], TimeSpan.FromSeconds(10), CancellationToken.None)).IsSuccess);

        var listing = await new SimulatorDevices(programs, new FakeProcessTable()) { DeviceSet = _scratch.File("Devices") }.ListAsync(CancellationToken.None);

        var device = Assert.Single(listing.Devices);
        Assert.Equal(("snail-machine-test-1", "com.apple.CoreSimulator.SimRuntime.iOS-27-0"), (device.Name, device.Runtime));
    }

    public void Dispose()
    {
        if (_udid is not null)
        {
            using var shutdown = System.Diagnostics.Process.Start("xcrun", ["simctl", "shutdown", _udid]);
            shutdown.WaitForExit();
            using var delete = System.Diagnostics.Process.Start("xcrun", ["simctl", "delete", _udid]);
            delete.WaitForExit();
        }

        _scratch.Dispose();
    }
}
