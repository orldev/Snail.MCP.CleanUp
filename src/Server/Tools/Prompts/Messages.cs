namespace Snail.MCP.CleanUp.Tools.Prompts;

/// <summary>What a caller is told when something did not work: the reason, and the next move.</summary>
/// <remarks>
/// Kept apart from <see cref="ToolDescriptions"/> because the audience differs. A description is read
/// before a call and steers what is asked; a message is read after one and has to end the guessing.
/// </remarks>
public static class Messages
{
    /// <summary>What is said when a call fails for a reason with no better name.</summary>
    public const string Unexpected = "something failed that has no better name; one more attempt is reasonable";
}
