using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Application.Ports;
using Snail.MCP.CleanUp.Core.Domain;
using Snail.MCP.CleanUp.Extensions;

namespace Snail.MCP.CleanUp.Tests.Machine;

/// <summary>The whole way on a real Mac: start a program on the record, find it, plan it away, end it.</summary>
[Collection(OnThisMac.Name)]
public sealed class RunOnThisMacTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private int? _started;

    [MachineFact]
    public async Task ProgramStartedOnTheRecord_IsFoundAsMine_AndEndedByMyCleanup()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services
            .AddSingleton(new ServerConfig { DataDirectory = _scratch.Path, TrashDirectory = _scratch.File("Trash"), ScratchRoot = _scratch.File("tmp") })
            .AddCleanUp();

        using var host = builder.Build();
        var services = host.Services;
        var started = await services.GetRequiredService<Runs>().StartAsync("sleep 300", _scratch.Path, CancellationToken.None);
        var id = started.Data!["id"]!.GetValue<string>();

        _started = started.Data!["pid"]!.GetValue<int>();

        var me = await services.GetRequiredService<ISessions>().CurrentAsync(CancellationToken.None);

        var inventory = await services.GetRequiredService<Census>().TakeAsync(me, CancellationToken.None);
        var plan = Planner.Decide(inventory, Scope.Mine, "machine");
        var removal = Assert.Single(plan.Removals, decision => decision.Resource.Id == id);

        Assert.Equal(Evidence.Ledger, removal.Resource.Evidence);

        var releases = await services.GetRequiredService<Cleaner>().ApplyAsync(plan with { Decisions = [removal] }, CancellationToken.None);

        Assert.Equal(Outcome.Released, Assert.Single(releases).Outcome);
        Assert.DoesNotContain(
            (await services.GetRequiredService<Census>().TakeAsync(me, CancellationToken.None)).Resources,
            resource => resource.Id == id);
    }

    /// <summary>A failed assertion must not leave the program it started behind: that is the very thing this server exists to prevent.</summary>
    public void Dispose()
    {
        if (_started is { } pid)
        {
            try
            {
                System.Diagnostics.Process.GetProcessById(pid).Kill();
            }
            catch (ArgumentException)
            {
            }
        }

        _scratch.Dispose();
    }
}
