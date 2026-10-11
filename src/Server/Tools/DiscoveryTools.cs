using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.CleanUp.Tools.Prompts;

namespace Snail.MCP.CleanUp.Tools;

/// <summary>Finding the way in: which tool does a thing, and what it takes to call it.</summary>
/// <remarks>Named <c>find_tool</c> without this server's prefix, as in the other servers of the fleet.</remarks>
[McpServerToolType]
public sealed class DiscoveryTools(ToolIndex index)
{
    /// <summary>The tools that do a thing described in words.</summary>
    [McpServerTool(Name = "find_tool", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.FindTool)]
    public CallToolResult Find(
        [Description(ToolDescriptions.FindToolTask)] string task,
        [Description(ToolDescriptions.FindToolLimit)] int limit = 5) =>
        ToolResponse.Success(index.Answer(task, limit));
}
