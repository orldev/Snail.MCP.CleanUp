using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.CleanUp.Application.Ports;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Domain;
using Snail.MCP.CleanUp.Extensions;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Application.Ports;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Tests.Machine;

/// <summary>VS Code's caches on this Mac: found, sized, and never planned away while Code runs.</summary>
[Collection(OnThisMac.Name)]
public sealed class EditorOnThisMacTests : IDisposable
{
    private readonly Scratch _scratch = new();

    [MachineFact]
    public async Task WebStorage_WhileCodeRuns_IsOnlyReported()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services
            .AddSingleton(new ServerConfig { DataDirectory = _scratch.Path, TrashDirectory = _scratch.File("Trash"), ScratchRoot = _scratch.File("tmp") })
            .AddCleanUp();

        using var host = builder.Build();
        var services = host.Services;
        var isCodeRunning = (await services.GetRequiredService<IProcessTable>().ListAsync(CancellationToken.None)).Any(process => process.Program == "Code");
        var me = await services.GetRequiredService<IHolders>().CurrentAsync(CancellationToken.None);
        var plan = Planner.Decide(await services.GetRequiredService<Census>().TakeAsync(me, CancellationToken.None), Scope.Caches, "machine", MachineKinds.Order);
        var webStorage = Assert.Single(plan.Decisions, decision => decision.Resource.Key.EndsWith("/Code/WebStorage", StringComparison.Ordinal));

        Assert.Equal(isCodeRunning ? Verdict.Report : Verdict.Remove, webStorage.Verdict);
        Assert.NotNull(webStorage.Resource.Bytes);
    }

    public void Dispose()
    {
        _scratch.Dispose();
    }
}
