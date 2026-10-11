namespace Snail.MCP.CleanUp.Tools.Prompts;

/// <summary>What the model reads before calling: the tool contract, guarded by the tests in <c>ToolCatalogTests</c>.</summary>
/// <remarks>
/// Separate from anything a human reads, because these strings steer behaviour: a limit stated here is a
/// promise about the code, and a guard test fails when the two drift apart. Every word is also paid for
/// in every session, so a description says what a model cannot find out by trying, and nothing else.
/// </remarks>
public static class ToolDescriptions
{
    /// <summary><c>cleanup_diagnose</c></summary>
    public const string Diagnose =
        """
        Say what state this server is in: its version, the session it counts as yours, and which of the
        programs it works through (simctl, adb, docker) it can reach. Call it when another tool reports that
        something is unavailable, not before every task.
        """;
}
