using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Snail.MCP.CleanUp.Tools;

/// <summary>Uniform tool results: the payload as JSON text with an ok flag, a failure flagged with <c>isError</c>.</summary>
/// <remarks>
/// The text keeps the ok flag the model reads; the error flag is what a client reads, because a failure
/// that looks like a success in the protocol is invisible to anything but the model.
/// </remarks>
public static class ToolResponse
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>An answer as a tool result: its payload, or its reason flagged as an error.</summary>
    public static CallToolResult From(Answer answer) =>
        answer.IsSuccess ? Success(answer.Data!) : Failure(answer.Error!, answer.Hint, answer.Data);

    /// <summary>A successful result.</summary>
    public static CallToolResult Success(JsonNode data) => new()
    {
        Content = [new TextContentBlock { Text = new JsonObject { ["ok"] = true, ["data"] = data.DeepClone() }.ToJsonString(Options) }]
    };

    /// <summary>A failed result: flagged for the protocol, explained with a hint for the model.</summary>
    public static CallToolResult Failure(string message, string? hint = null, JsonNode? data = null) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = Told(message, hint, data).ToJsonString(Options) }]
    };

    private static JsonObject Told(string message, string? hint, JsonNode? data) => new()
    {
        ["ok"] = false,
        ["error"] = message,
        ["hint"] = hint,
        ["data"] = data?.DeepClone()
    };
}
