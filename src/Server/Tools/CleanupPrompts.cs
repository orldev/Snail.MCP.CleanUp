using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Snail.MCP.CleanUp.Tools.Prompts;

namespace Snail.MCP.CleanUp.Tools;

/// <summary>Requests a person picks from the client, such as a slash command, instead of typing them.</summary>
/// <remarks>
/// A prompt ships with the server, so it changes with the tools it names and reaches every client, where a skill
/// written beside one client would fall behind them.
/// </remarks>
[McpServerPromptType]
public sealed class CleanupPrompts
{
    /// <summary>A cleanup the person sees before it is done: plan, show, wait for agreement, apply.</summary>
    [McpServerPrompt(Name = "cleanup")]
    [Description(ToolDescriptions.CleanupPrompt)]
    public static string Cleanup([Description(ToolDescriptions.CleanupPromptScope)] string scope = "everything")
    {
        if (!ToolArguments.TryRead<Scope>(scope, out var parsed))
            throw new McpProtocolException($"there is no scope {scope}: {ToolDescriptions.CleanupPromptScope}", McpErrorCode.InvalidParams);

        var word = parsed.ToString().ToLowerInvariant();

        return ToolDescriptions.CleanupPromptRequest.Replace("{scope}", word, StringComparison.Ordinal);
    }
}
