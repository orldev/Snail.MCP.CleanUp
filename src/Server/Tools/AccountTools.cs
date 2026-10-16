using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Tools.Prompts;

namespace Snail.MCP.CleanUp.Tools;

/// <summary>Looking at what agents left, deciding a cleanup, and carrying it out.</summary>
[McpServerToolType]
public sealed class AccountTools(Account account)
{
    /// <summary>What is on the machine.</summary>
    [McpServerTool(Name = "cleanup_inventory", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Inventory)]
    public async Task<CallToolResult> InventoryAsync(CancellationToken cancellationToken = default) =>
        ToolResponse.From(await account.InventoryAsync(cancellationToken));

    /// <summary>A cleanup, decided and shown.</summary>
    [McpServerTool(Name = "cleanup_plan", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Plan)]
    public async Task<CallToolResult> PlanAsync(
        [Description(ToolDescriptions.PlanScope)] string scope = "mine",
        CancellationToken cancellationToken = default) =>
        ToolArguments.TryRead<Scope>(scope, out var parsed)
            ? ToolResponse.From(await account.PlanAsync(parsed, cancellationToken))
            : ToolResponse.Failure($"there is no scope {scope}", ToolDescriptions.PlanScope);

    /// <summary>A shown cleanup, carried out.</summary>
    [McpServerTool(Name = "cleanup_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Apply)]
    public async Task<CallToolResult> ApplyAsync(
        [Description(ToolDescriptions.ApplyPlan)] string planId,
        [Description(ToolDescriptions.ApplyOnly)] string[]? only = null,
        CancellationToken cancellationToken = default) =>
        ToolResponse.From(await account.ApplyAsync(planId, only, cancellationToken));

    /// <summary>One thing, released now.</summary>
    [McpServerTool(Name = "cleanup_release", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Release)]
    public async Task<CallToolResult> ReleaseAsync(
        [Description(ToolDescriptions.ReleaseId)] string id,
        CancellationToken cancellationToken = default) =>
        ToolResponse.From(await account.ReleaseAsync(id, cancellationToken));

    /// <summary>One thing, left alone on purpose.</summary>
    [McpServerTool(Name = "cleanup_keep", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Keep)]
    public async Task<CallToolResult> KeepAsync(
        [Description(ToolDescriptions.KeepId)] string id,
        [Description(ToolDescriptions.KeepReason)] string? reason = null,
        [Description(ToolDescriptions.KeepWithdraw)] bool withdraw = false,
        CancellationToken cancellationToken = default) =>
        ToolResponse.From(await account.KeepAsync(id, reason, withdraw, cancellationToken));

    /// <summary>A discovery, made a known kind.</summary>
    [McpServerTool(Name = "cleanup_learn", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Learn)]
    public async Task<CallToolResult> LearnAsync(
        [Description(ToolDescriptions.LearnPattern)] string pattern,
        [Description(ToolDescriptions.LearnAction)] string action,
        [Description(ToolDescriptions.LearnTrail)] string trail = "file",
        [Description(ToolDescriptions.LearnNote)] string? note = null,
        [Description(ToolDescriptions.LearnBlocked)] string? blockedWhileRunning = null,
        CancellationToken cancellationToken = default) =>
        ToolArguments.TryRead<RuleAction>(action, out var parsed)
            ? ToolResponse.From(await account.LearnAsync(new Rule(trail, pattern, parsed) { Note = note, BlockedWhileRunning = blockedWhileRunning }, cancellationToken))
            : ToolResponse.Failure($"there is no action {action}", ToolDescriptions.LearnAction);
}
