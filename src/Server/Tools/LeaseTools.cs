using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Tools.Prompts;

namespace Snail.MCP.CleanUp.Tools;

/// <summary>Devices of an agent's own, so that two agents never share one.</summary>
[McpServerToolType]
public sealed class LeaseTools(Leases leases)
{
    /// <summary>Makes a simulator for this session.</summary>
    [McpServerTool(Name = "cleanup_simulator_acquire", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.SimulatorAcquire)]
    public async Task<CallToolResult> SimulatorAsync(
        [Description(ToolDescriptions.SimulatorFrom)] string? from = null,
        [Description(ToolDescriptions.SimulatorBoot)] bool boot = false,
        CancellationToken cancellationToken = default) =>
        ToolResponse.From(await leases.SimulatorAsync(from, boot, cancellationToken));

    /// <summary>Makes and starts an Android emulator for this session.</summary>
    [McpServerTool(Name = "cleanup_emulator_acquire", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.EmulatorAcquire)]
    public async Task<CallToolResult> EmulatorAsync(
        [Description(ToolDescriptions.EmulatorImage)] string? image = null,
        [Description(ToolDescriptions.EmulatorWindow)] bool window = false,
        CancellationToken cancellationToken = default) =>
        ToolResponse.From(await leases.EmulatorAsync(image, window, cancellationToken));
}
