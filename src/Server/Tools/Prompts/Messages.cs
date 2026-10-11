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

    /// <summary>What is said when cleanup_run is called without a command.</summary>
    public const string RunNeedsACommand = "there is no command to run";

    /// <summary>How cleanup_run is called, for a caller that called it wrongly.</summary>
    public const string RunHint = "Pass the whole command line as command, such as \"npm run dev\", and an existing directory to run it in.";

    /// <summary>What is said when the server cannot tell which session it serves.</summary>
    public const string NoSession = "this server cannot tell which session it belongs to, so nothing can be made in its name";

    /// <summary>What to do about it.</summary>
    public const string NoSessionHint = "Start the server from a Claude Code session, or call cleanup_diagnose to see what it sees.";

    /// <summary>How a simulator is asked for, for a caller that asked wrongly.</summary>
    public const string SimulatorHint = "Pass a shut-down device to clone as from, a device type such as iPhone 17 Pro, or nothing for the newest iPhone Pro.";

    /// <summary>How an emulator is asked for, for a caller that asked wrongly.</summary>
    public const string EmulatorHint = "Install the Android emulator and a system image with the SDK manager, or pass part of an installed image's path as image.";
}
