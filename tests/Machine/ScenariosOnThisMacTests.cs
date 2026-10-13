using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Application.Ports;
using Snail.MCP.CleanUp.Core.Domain;
using Snail.MCP.CleanUp.Extensions;

namespace Snail.MCP.CleanUp.Tests.Machine;

/// <summary>The scenarios the server exists for, on the real machine: a session that died, two that run, and what is nobody's.</summary>
[Collection(OnThisMac.Name)]
public sealed class ScenariosOnThisMacTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly List<string> _simulators = [];

    private readonly List<int> _processes = [];

    private IHost Host()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        builder.Services
            .AddSingleton(new ServerConfig { DataDirectory = _scratch.Path, TrashDirectory = _scratch.File("Trash"), ScratchRoot = _scratch.File("tmp") })
            .AddCleanUp();

        return builder.Build();
    }

    /// <summary>A session died with a server running, a simulator booted and a scratchpad written; the next session cleans all of it.</summary>
    [MachineFact]
    public async Task SessionThatDied_LeavesNothingAfterTheNextSessionCleansOrphans()
    {
        using var host = Host();
        var services = host.Services;
        var dead = Owner.Session(Guid.NewGuid().ToString());

        var started = await services.GetRequiredService<Runs>().StartAsync("sleep 300", _scratch.Path, CancellationToken.None);
        var process = started.Data!["id"]!.GetValue<string>();
        _processes.Add(started.Data!["pid"]!.GetValue<int>());
        await services.GetRequiredService<ILedger>().RecordAsync(new Holding(process, dead, Evidence.Ledger, DateTimeOffset.UtcNow.AddMinutes(-5), "sleep 300"), CancellationToken.None);

        var simulator = Simulator(Simulators.NameFor(dead, 1));
        var scratchpad = Scratchpad(dead.Id);

        var me = await services.GetRequiredService<ISessions>().CurrentAsync(CancellationToken.None);
        var plan = Planner.Decide(await services.GetRequiredService<Census>().TakeAsync(me, CancellationToken.None), Scope.Orphans, "machine");
        var ids = new[] { process, $"simulator:{simulator}", $"file:{scratchpad}" };
        var removals = plan.Removals.Where(decision => ids.Contains(decision.Resource.Id)).ToList();

        Assert.Equal(ids.Order(StringComparer.Ordinal), removals.Select(decision => decision.Resource.Id).Order(StringComparer.Ordinal));

        var releases = await services.GetRequiredService<Cleaner>().ApplyAsync(plan with { Decisions = removals }, CancellationToken.None);

        Assert.All(releases, release => Assert.Equal(Outcome.Released, release.Outcome));
        Assert.False(Directory.Exists(scratchpad));
        Assert.DoesNotContain(simulator, Listed());
    }

    /// <summary>The lesson of `simctl shutdown all`: what another running session holds is never anybody else's to remove.</summary>
    [MachineFact]
    public async Task SimulatorOfAnotherRunningSession_IsKept_EvenWhenEverythingIsCleaned()
    {
        using var host = Host();
        var services = host.Services;
        var sessions = services.GetRequiredService<ISessions>();
        var me = await sessions.CurrentAsync(CancellationToken.None);
        var neighbour = await NeighbourAsync(services);

        Assert.Contains(await sessions.ListAsync(CancellationToken.None), session => session.IsAlive && session.Owner == neighbour);

        var simulator = Simulator(Simulators.NameFor(neighbour, 1));
        var plan = Planner.Decide(await services.GetRequiredService<Census>().TakeAsync(me, CancellationToken.None), Scope.Everything, "machine");
        var decision = plan.Decisions.Single(candidate => candidate.Resource.Key == simulator);

        Assert.Equal(Verdict.Keep, decision.Verdict);
        Assert.Contains("still running", decision.Reason, StringComparison.Ordinal);
    }

    /// <summary>A simulator a person made and a program they detached are theirs: everything is cleaned, and they stay.</summary>
    [MachineFact]
    public async Task WhatNothingTiesToAnAgent_SurvivesCleaningEverything()
    {
        using var host = Host();
        var services = host.Services;
        var simulator = Simulator($"Developer phone {Guid.NewGuid():n}");
        var detached = Detached();

        var me = await services.GetRequiredService<ISessions>().CurrentAsync(CancellationToken.None);
        var plan = Planner.Decide(await services.GetRequiredService<Census>().TakeAsync(me, CancellationToken.None), Scope.Everything, "machine");
        var theirs = plan.Decisions.Where(decision => decision.Resource.Key == simulator || decision.Resource.Facts.GetValueOrDefault("pid") == $"{detached}").ToList();

        Assert.Equal(2, theirs.Count);
        Assert.All(theirs, decision => Assert.Equal(Verdict.Report, decision.Verdict));
        Assert.DoesNotContain(plan.Removals, decision => theirs.Contains(decision));
    }

    /// <summary>
    /// Another running agent, made for the test: <c>sleep</c> started through a link called <c>claude</c> is what
    /// the process table shows of an agent SleepOf never heard of, so it is a running session known by its
    /// process. A copy of <c>sleep</c> would not do — macOS kills a moved system binary on launch.
    /// </summary>
    private async Task<Owner> NeighbourAsync(IServiceProvider services)
    {
        var agent = _scratch.File("claude");

        File.CreateSymbolicLink(agent, "/bin/sleep");

        using var started = Process.Start(agent, ["300"]);
        _processes.Add(started.Id);

        var table = services.GetRequiredService<Snail.MCP.CleanUp.Application.Ports.IProcessTable>();

        table.Refresh();

        var running = (await table.ListAsync(CancellationToken.None)).Single(process => process.Pid == started.Id);

        return Owner.Session(MachineSessions.NameOf(running));
    }

    private string Simulator(string name)
    {
        var udid = Run("xcrun", "simctl", "create", name, "com.apple.CoreSimulator.SimDeviceType.iPhone-17-Pro").Trim();

        _simulators.Add(udid);

        return udid;
    }

    private string Scratchpad(string session)
    {
        var path = _scratch.File($"tmp/claude-501/-Users-dev-project/{session}");

        Directory.CreateDirectory(Path.Combine(path, "scratchpad"));
        File.WriteAllText(Path.Combine(path, "scratchpad", "note.txt"), "agent output");

        foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", SearchOption.AllDirectories).Append(new DirectoryInfo(path)))
            entry.LastWriteTimeUtc = DateTime.UtcNow.AddHours(-3);

        return path;
    }

    private int Detached()
    {
        var pid = int.Parse(Run("/bin/sh", "-c", "nohup /bin/sleep 301 >/dev/null 2>&1 & echo $!").Trim(), System.Globalization.CultureInfo.InvariantCulture);

        _processes.Add(pid);
        Thread.Sleep(500);

        return pid;
    }

    private static IReadOnlyList<string> Listed() => [.. Simulators.Parse(Run("xcrun", "simctl", "list", "devices", "-j")).Select(device => device.Udid)];

    private static string Run(string program, params string[] arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(program, arguments) { RedirectStandardOutput = true, RedirectStandardError = true })!;
        var output = process.StandardOutput.ReadToEnd();

        process.WaitForExit(120_000);

        return output;
    }

    public void Dispose()
    {
        foreach (var udid in _simulators.Where(Listed().Contains))
            Run("xcrun", "simctl", "delete", udid);

        foreach (var pid in _processes)
        {
            try
            {
                Process.GetProcessById(pid).Kill();
            }
            catch (ArgumentException)
            {
            }
        }

        _scratch.Dispose();
    }
}
