using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Native;
using Snail.MCP.CleanUp.Application;

namespace Snail.MCP.CleanUp.Tests.Machine;

/// <summary>The kernel's process table against the one ps prints: the same processes, keyed the same way.</summary>
/// <remarks>
/// The ledger already holds processes written down by their key, the id and the second they started; reading the
/// table another way must not give any of them another key, or the ledger would forget whose they are.
/// </remarks>
[Collection(OnThisMac.Name)]
public sealed class NativeProcessTableOnThisMacTests
{
    [MachineFact]
    public async Task KernelTable_AgreesWithPs_OnKeysParentsProgramsAndCommands()
    {
        var clock = TimeProvider.System;
        var native = (await new NativeProcessTable(clock).ListAsync(CancellationToken.None)).ToDictionary(process => process.Pid);
        var printed = await new ProcessTable(new Programs(new Performance(clock), clock), clock).ListAsync(CancellationToken.None);
        var mine = Mine();
        var both = printed.Where(process => native.ContainsKey(process.Pid)).ToList();
        var settled = DateTimeOffset.Now.AddSeconds(-5);
        var ours = both.Where(process => mine.Contains(process.Pid) && process.StartedAt < settled).ToList();

        Assert.True(both.Count > printed.Count * 0.95, $"{both.Count} of {printed.Count} processes seen both ways");
        Assert.True(ours.Count > 50, $"{ours.Count} processes of this user seen both ways");
        Assert.All(ours, process => Assert.Equal(process.Key, native[process.Pid].Key));
        Assert.All(both, process => Assert.Equal(process.ParentPid, native[process.Pid].ParentPid));
        Assert.All(ours.Where(process => !process.Command.StartsWith('/') || process.Command.StartsWith(process.Executable ?? "/", StringComparison.Ordinal)), process => Assert.Equal(process.Program, native[process.Pid].Program));

        var agents = ours.Where(process => MachineSessions.IsAgent(native[process.Pid])).ToList();

        Assert.NotEmpty(agents);

        Assert.All(agents, process => Assert.Equal(process.Command, native[process.Pid].Command));
    }

    /// <summary>Run under Claude Code, this test process carries its session as every program the agent starts does; Apple's programs hide theirs.</summary>
    [MachineFact]
    public async Task KernelTable_ReadsTheSessionFromTheEnvironment_OfThisUsersProcesses()
    {
        var native = (await new NativeProcessTable(TimeProvider.System).ListAsync(CancellationToken.None)).ToDictionary(process => process.Pid);
        var self = native[Environment.ProcessId];

        Assert.Equal(Environment.GetEnvironmentVariable(ProcessArguments.SessionVariable), self.SessionId);
        Assert.Equal(int.TryParse(Environment.GetEnvironmentVariable(ProcessArguments.AgentVariable), out var agent) ? agent : null, self.AgentPid);
        Assert.Null(native[1].SessionId);
    }

    private static HashSet<int> Mine()
    {
        using var ps = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ps", ["-U", Environment.UserName, "-o", "pid="]) { RedirectStandardOutput = true })!;
        var pids = ps.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToHashSet();

        ps.WaitForExit();

        return pids;
    }
}
