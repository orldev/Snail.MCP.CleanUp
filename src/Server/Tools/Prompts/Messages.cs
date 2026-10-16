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

    /// <summary>What a run is told whose program ended before it could be written down.</summary>
    public const string RunEndedAtOnce = "the program ended at once, before it could be written down; read its log for why — anything it left running in the background is not on the record";

    /// <summary>What a run started for no session it can tell is told: it is on the record, but nobody's.</summary>
    public const string RunOfNobody = "started, but this server cannot tell which session it belongs to: it is written down as nobody's, so no cleanup removes it; end it yourself, or call cleanup_diagnose to see what the server sees";

    /// <summary>What is said when the server cannot tell which session it serves.</summary>
    public const string NoSession = "this server cannot tell which session it belongs to, so nothing can be made in its name";

    /// <summary>What to do about it.</summary>
    public const string NoSessionHint = "Start the server from a Claude Code session, or call cleanup_diagnose to see what it sees.";

    /// <summary>What to do when the sessions of the machine could not be told, so that no census was taken.</summary>
    public const string SessionsUnknownHint = "Nothing was decided or removed. Ask again in a moment; when it keeps failing, call cleanup_diagnose to see what the server can read.";

    /// <summary>How a simulator is asked for, for a caller that asked wrongly.</summary>
    public const string SimulatorHint = "Pass a shut-down device to clone as from, a device type such as iPhone 17 Pro, or nothing for the newest iPhone Pro.";

    /// <summary>How an emulator is asked for, for a caller that asked wrongly.</summary>
    public const string EmulatorHint = "Install the Android emulator and a system image with the SDK manager, or pass part of an installed image's path as image.";

    /// <summary>How a plan is carried out, for a caller whose plan was not found or went stale.</summary>
    public const string PlanHint = "Call cleanup_plan again and pass the planId it returns to cleanup_apply within ten minutes.";

    /// <summary>How one resource is released, for a caller that named one wrongly.</summary>
    public const string ReleaseHint = "Pass an id exactly as cleanup_inventory or cleanup_plan printed it, such as simulator:<udid>; something that is not proven yours is not released.";

    /// <summary>How a resource is kept.</summary>
    public const string KeepHint = "Pass the id and the reason it should stay, such as \"the dev server I keep open\".";

    /// <summary>How a rule is learned.</summary>
    public const string LearnHint = "Pass a pattern such as ~/Library/Caches/tool/*, an action (ignore, report or cache) and the trail of the discovery when it is not file.";
}
