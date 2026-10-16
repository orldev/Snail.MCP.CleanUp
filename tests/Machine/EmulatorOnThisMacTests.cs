using System.Diagnostics;
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

/// <summary>A real emulator, made and started for this session, found as its own and taken apart by its own cleanup.</summary>
[Collection(OnThisMac.Name)]
public sealed class EmulatorOnThisMacTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private string? _device;

    [MachineFact]
    public async Task EmulatorLeased_IsMine_AndMyCleanupEndsItAndErasesItsDevice()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services
            .AddSingleton(new ServerConfig { DataDirectory = _scratch.Path, TrashDirectory = _scratch.File("Trash"), ScratchRoot = _scratch.File("tmp") })
            .AddCleanUp();

        using var host = builder.Build();
        var services = host.Services;
        var android = services.GetRequiredService<Android>();
        var hadAdb = await android.IsAdbRunningAsync(CancellationToken.None);
        var leased = await services.GetRequiredService<Leases>().EmulatorAsync(null, window: false, CancellationToken.None);

        Assert.True(leased.IsSuccess, leased.Error);

        _device = leased.Data!["avd"]!.GetValue<string>();

        var me = await services.GetRequiredService<IHolders>().CurrentAsync(CancellationToken.None);
        var plan = await PlannedAsync(services, me, _device);

        var removals = plan.Removals.Where(decision => decision.Resource.Key == _device).ToList();
        var releases = await services.GetRequiredService<Cleaner>().ApplyAsync(plan with { Decisions = removals }, CancellationToken.None);

        Assert.Equal([MachineKinds.Emulator, MachineKinds.VirtualDevice], removals.Select(decision => decision.Resource.Kind));
        Assert.All(releases, release => Assert.Equal(Outcome.Released, release.Outcome));
        Assert.DoesNotContain(_device, android.DeviceNames());
        Assert.Equal(hadAdb, await android.IsAdbRunningAsync(CancellationToken.None));

        _device = null;
    }

    /// <summary>The emulator takes a few seconds to appear in the process table after it is started.</summary>
    private static async Task<Plan> PlannedAsync(IServiceProvider services, Owner me, string device)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var plan = Planner.Decide(await services.GetRequiredService<Census>().TakeAsync(me, CancellationToken.None), Scope.Mine, "machine", MachineKinds.Order);

            if (plan.Removals.Any(decision => decision.Resource.Kind == MachineKinds.Emulator && decision.Resource.Key == device))
                return plan;

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException($"the emulator of {device} never appeared");
    }

    public void Dispose()
    {
        if (_device is not null)
        {
            using var killed = Process.Start("pkill", ["-f", $"-avd {_device}"]);
            killed.WaitForExit();

            var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "avd");

            if (Directory.Exists(Path.Combine(home, $"{_device}.avd")))
                Directory.Delete(Path.Combine(home, $"{_device}.avd"), recursive: true);

            File.Delete(Path.Combine(home, $"{_device}.ini"));
        }

        _scratch.Dispose();
    }
}
