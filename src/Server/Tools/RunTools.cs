using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Tools.Prompts;

namespace Snail.MCP.CleanUp.Tools;

/// <summary>Starting programs that outlive a tool call, on the record.</summary>
[McpServerToolType]
public sealed class RunTools(Runs runs)
{
    /// <summary>Starts a long-running program written down as the session's.</summary>
    [McpServerTool(Name = "cleanup_run", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Run)]
    public async Task<CallToolResult> RunAsync(
        [Description(ToolDescriptions.RunCommand)] string command,
        [Description(ToolDescriptions.RunDirectory)] string? directory = null,
        CancellationToken cancellationToken = default) =>
        ToolResponse.From(await runs.StartAsync(command, directory, cancellationToken));
}
