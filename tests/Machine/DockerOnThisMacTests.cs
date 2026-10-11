using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Application.Ports;
using Snail.MCP.CleanUp.Core.Domain;
using Snail.MCP.CleanUp.Extensions;

namespace Snail.MCP.CleanUp.Tests.Machine;

/// <summary>A real container with the owner label, found as this session's and removed by its cleanup.</summary>
[Collection(OnThisMac.Name)]
public sealed class DockerOnThisMacTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly string _name = $"snail-cleanup-test-{Guid.NewGuid():n}";

    [DockerFact]
    public async Task ContainerLabelledForThisSession_IsMine_AndMyCleanupRemovesIt()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services
            .AddSingleton(new ServerConfig { DataDirectory = _scratch.Path, TrashDirectory = _scratch.File("Trash"), ScratchRoot = _scratch.File("tmp") })
            .AddCleanUp();

        using var host = builder.Build();
        var services = host.Services;
        var me = await services.GetRequiredService<ISessions>().CurrentAsync(CancellationToken.None);

        Run("docker", "create", "--name", _name, "--label", $"snail.owner={me}", "alpine:3.20", "sleep", "300");

        var plan = Planner.Decide(await services.GetRequiredService<Census>().TakeAsync(me, CancellationToken.None), Scope.Mine, "machine");
        var removal = Assert.Single(plan.Removals, decision => decision.Resource.Kind == Kinds.Container && decision.Resource.Facts["name"] == _name);
        var release = Assert.Single(await services.GetRequiredService<Cleaner>().ApplyAsync(plan with { Decisions = [removal] }, CancellationToken.None));

        Assert.Equal(Outcome.Released, release.Outcome);
    }

    public void Dispose()
    {
        Run("docker", "rm", "-f", _name);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _scratch.Dispose();
    }

    private static void Run(string program, params string[] arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(program, arguments) { RedirectStandardOutput = true, RedirectStandardError = true })!;
            process.WaitForExit(120_000);
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
