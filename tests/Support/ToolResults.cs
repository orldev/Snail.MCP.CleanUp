using ModelContextProtocol.Protocol;

namespace Snail.MCP.CleanUp.Tests.Support;

/// <summary>Reads a tool result the way a model does: the text, and the JSON inside it.</summary>
internal static class ToolResults
{
    public static string Text(this CallToolResult result) =>
        string.Join(string.Empty, result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    public static JsonNode Data(this CallToolResult result) =>
        JsonNode.Parse(result.Text())!["data"]!;
}
