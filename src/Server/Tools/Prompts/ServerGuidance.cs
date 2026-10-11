namespace Snail.MCP.CleanUp.Tools.Prompts;

/// <summary>What the client is told about this server when it connects.</summary>
/// <remarks>
/// The instructions are read once, before any tool is called, so they carry what a model cannot discover
/// by trying: which question each tool answers, and what each one costs.
/// </remarks>
public static class ServerGuidance
{
    /// <summary>The text that rides along with initialize.</summary>
    public const string Instructions =
        """
        This server keeps account of what agents leave behind on this machine and removes what is proven
        theirs. Every tool is named cleanup_*.

        cleanup_diagnose says which session the server counts as yours and what it can reach.

        Every answer is JSON with an ok flag. When ok is false, read error and hint and change the call
        instead of repeating it.
        """;
}
