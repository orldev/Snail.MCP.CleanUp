using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Tools.Prompts;

namespace Snail.MCP.CleanUp.Tools;

/// <summary>The state of the server and of what it can see on this machine.</summary>
[McpServerToolType]
public sealed class DiagnosticsTools(Health health)
{
    /// <summary>Reports the server, the session it belongs to and the tools it can reach.</summary>
    [McpServerTool(Name = "cleanup_diagnose", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Diagnose)]
    public async Task<CallToolResult> DiagnoseAsync(CancellationToken cancellationToken = default) =>
        ToolResponse.From(await health.ReportAsync(cancellationToken));
}
