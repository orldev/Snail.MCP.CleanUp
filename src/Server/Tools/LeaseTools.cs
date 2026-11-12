using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Tools.Prompts;
using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.MCP.CleanUp.Tools;

/// <summary>Devices of an agent's own, so that two agents never share one.</summary>
[McpServerToolType]
public sealed class LeaseTools(Leases leases, Jobs jobs)
{
    /// <summary>Makes a simulator for this session.</summary>
    [McpServerTool(Name = "cleanup_simulator_acquire", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.SimulatorAcquire)]
    public async Task<CallToolResult> SimulatorAsync(
        [Description(ToolDescriptions.SimulatorFrom)] string? from = null,
        [Description(ToolDescriptions.SimulatorBoot)] bool boot = false,
        [Description(ToolDescriptions.Wait)] int wait = Waits.Default,
        CancellationToken cancellationToken = default) =>
        await Waits.AnsweredAsync(wait, () => jobs.Start("simulator", (_, token) => leases.SimulatorAsync(from, boot, token), ToolAnswer.ObjectInfo), cancellationToken);

    /// <summary>Makes and starts an Android emulator for this session.</summary>
    [McpServerTool(Name = "cleanup_emulator_acquire", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.EmulatorAcquire)]
    public async Task<CallToolResult> EmulatorAsync(
        [Description(ToolDescriptions.EmulatorImage)] string? image = null,
        [Description(ToolDescriptions.EmulatorWindow)] bool window = false,
        [Description(ToolDescriptions.Wait)] int wait = Waits.Default,
        CancellationToken cancellationToken = default) =>
        await Waits.AnsweredAsync(wait, () => jobs.Start("emulator", (_, token) => leases.EmulatorAsync(image, window, token), ToolAnswer.ObjectInfo), cancellationToken);
}
