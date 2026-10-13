using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Application.Ports;
using Snail.MCP.CleanUp.Core.Domain;
using Snail.MCP.CleanUp.Extensions;

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

        var me = await services.GetRequiredService<ISessions>().CurrentAsync(CancellationToken.None);
        var inventory = await services.GetRequiredService<Census>().TakeAsync(me, CancellationToken.None);
        var plan = Planner.Decide(inventory, Scope.Mine, "machine");
        var seen = inventory.Resources.Where(resource => resource.Kind == Kinds.Simulator).Select(resource => $"{resource.Title} {resource.Owner} {resource.Evidence} {resource.Blocker}");

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
