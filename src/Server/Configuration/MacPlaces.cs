namespace Snail.MCP.CleanUp.Configuration;

/// <summary>Where on a Mac leftovers show up, and what lives there that is nobody's leftover.</summary>
/// <remarks>
/// The places are looked into only as deep as new things appear in them: a tool that writes into
/// <c>~/Library/Application Support</c> makes a folder there or one level below, and reading deeper would
/// trade a census of milliseconds for one of seconds without finding anything a shallower look misses.
/// </remarks>
public static class MacPlaces
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>The directories the file trail looks into, and how many levels down.</summary>
    public static IReadOnlyList<(string Directory, int Depth)> Watched { get; } =
    [
        (Home, 1),
        ($"{Home}/Library/Application Support", 2),
        ($"{Home}/Library/Caches", 1),
        ($"{Home}/Library/Developer", 2),
        ($"{Home}/Library/LaunchAgents", 1),
        ($"{Home}/.cache", 1),
        ($"{Home}/.local/share", 1),
        (Path.TrimEndingDirectorySeparator(Path.GetTempPath()), 1),
        ("/private/tmp", 1)
    ];

    /// <summary>The rules every installation starts with; the learned ones are added after them.</summary>
    public static IReadOnlyList<Rule> Shipped { get; } =
    [
        new("file", "~/.claude/**", RuleAction.Ignore) { Note = "Claude Code's own state: transcripts, settings, memory." },
        new("file", "~/.snail-mcp-cleanup/**", RuleAction.Ignore) { Note = "This server's own account." },
        new("file", "~/Library/Application Support/Snail.SleepOf/**", RuleAction.Ignore) { Note = "Snail.SleepOf's account of sessions." },
        new("file", "~/.Trash/**", RuleAction.Ignore) { Note = "What was already thrown away." },
        new("file", "~/Library/Caches/com.apple.*", RuleAction.Ignore) { Note = "The system's own caches." },
        new("file", "/private/tmp/claude-*", RuleAction.Ignore) { Note = "Claude Code's scratch root; the scratchpads inside it are a kind of their own." },
        new("file", "~/.DS_Store", RuleAction.Ignore) { Note = "Finder's view settings." }
    ];
}
