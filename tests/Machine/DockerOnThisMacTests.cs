using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Domain;
using Snail.MCP.CleanUp.Extensions;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Application.Ports;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Tests.Machine;

/// <summary>A real container with the owner label, found as this session's and removed by its cleanup.</summary>
[Collection(OnThisMac.Name)]
public sealed class DockerOnThisMacTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private const string Image = "alpine:3.20";

    private readonly string _name = $"snail-cleanup-test-{Guid.NewGuid():n}";

    /// <summary>Whether the image was here before the test, which decides whether the test removes it after.</summary>
    /// <remarks>
    /// <c>docker create</c> pulls an image it does not find, and a pulled image is a leftover like any other:
    /// measured on 2026-10-11, when the first run of this test left alpine behind on a Mac that had none.
    /// </remarks>
    private bool _hadImage = true;

    [DockerFact]
    public async Task ContainerLabelledForThisSession_IsMine_AndMyCleanupRemovesIt()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services
            .AddSingleton(new ServerConfig { DataDirectory = _scratch.Path, TrashDirectory = _scratch.File("Trash"), ScratchRoot = _scratch.File("tmp") })
            .AddCleanUp();

        using var host = builder.Build();
        var services = host.Services;
        var me = await services.GetRequiredService<IHolders>().CurrentAsync(CancellationToken.None);

        _hadImage = Run("docker", "image", "inspect", Image) == 0;
        Run("docker", "create", "--name", _name, "--label", $"snail.owner={me}", Image, "sleep", "300");

        var plan = Planner.Decide(await services.GetRequiredService<Census>().TakeAsync(me, CancellationToken.None), Scope.Mine, "machine", MachineKinds.Order);
        var removal = Assert.Single(plan.Removals, decision => decision.Resource.Kind == Kinds.Container && decision.Resource.Facts["name"] == _name);
        var release = Assert.Single(await services.GetRequiredService<Cleaner>().ApplyAsync(plan with { Decisions = [removal] }, CancellationToken.None));

        Assert.Equal(Outcome.Released, release.Outcome);
    }

    public void Dispose()
    {
        Run("docker", "rm", "-f", _name);

        if (!_hadImage)
            Run("docker", "image", "rm", Image);

        _scratch.Dispose();
    }

    private static int Run(string program, params string[] arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(program, arguments) { RedirectStandardOutput = true, RedirectStandardError = true })!;
            process.WaitForExit(120_000);

            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return -1;
        }
    }
}
