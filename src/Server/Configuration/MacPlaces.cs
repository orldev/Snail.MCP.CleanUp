using System.Buffers;

namespace Snail.MCP.CleanUp.Configuration;

/// <summary>Where on a Mac leftovers show up, and what lives there that is nobody's leftover.</summary>
/// <remarks>
/// Global package places are among them — Homebrew's cellar, npm's global modules, .NET tools, pipx, cargo,
/// go — because a package installed for one task stays installed for every task after it, and a listing of
/// their directories costs nothing next to asking each package manager every half a minute.
/// <para>
/// The places are looked into only as deep as new things appear in them: a tool that writes into
/// <c>~/Library/Application Support</c> makes a folder there or one level below, and reading deeper would
/// trade a census of milliseconds for one of seconds without finding anything a shallower look misses.
/// </para>
/// </remarks>
public static class MacPlaces
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Where the system and installed applications keep their programs; one started from there is nobody's work.</summary>
    private static readonly SearchValues<string> System = SearchValues.Create(
        ["/Applications/", "/System/", "/usr/libexec/", "/usr/sbin/", "/sbin/", "/Library/Apple/", "/Library/Application Support/", "/usr/bin/"],
        StringComparison.Ordinal);

    /// <summary>Whether a command line starts a program from a place of the system or of an installed application.</summary>
    public static bool IsSystem(string command) => command.AsSpan().IndexOfAny(System) == 0;

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
        ($"{Home}/.android", 1),
        ("/opt/homebrew/Cellar", 1),
        ("/opt/homebrew/lib/node_modules", 1),
        ("/usr/local/lib/node_modules", 1),
        ($"{Home}/.dotnet/tools/.store", 1),
        ($"{Home}/.local/pipx/venvs", 1),
        ($"{Home}/.cargo/bin", 1),
        ($"{Home}/go/bin", 1),
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
        new("file", "~/.DS_Store", RuleAction.Ignore) { Note = "Finder's view settings." },
        new("file", "~/**/.DS_Store", RuleAction.Ignore) { Note = "Finder's view settings." },
        new("file", "~/Library/Application Support/Code/WebStorage", RuleAction.Cache)
        {
            Note = "VS Code's webview storage, which grows with every agent panel opened; Chromium rebuilds it, and it is removed whole because its quota database indexes every bucket",
            BlockedWhileRunning = "Code"
        },
        new("file", "~/Library/Application Support/Code/Service Worker", RuleAction.Cache)
        {
            Note = "VS Code's service workers of webviews, registered again on the next load",
            BlockedWhileRunning = "Code"
        },
        new("file", "~/Library/Application Support/Code/CachedData/*", RuleAction.Cache)
        {
            Note = "VS Code's compiled code, one folder per version it ever ran",
            BlockedWhileRunning = "Code"
        },
        new("file", "~/Library/Application Support/Code/Partitions/*", RuleAction.Report) { Note = "storage of webview partitions; may hold an extension's state" },
        new("file", "~/Library/Application Support/Code/logs/*", RuleAction.Report) { Note = "VS Code's logs, one folder per start" },
        new("file", "~/Library/Developer/Xcode/DerivedData/*", RuleAction.Cache)
        {
            Note = "Xcode's build products and test results of one project; the next build makes them again",
            BlockedWhileRunning = "Xcode"
        },
        new("file", "~/Library/Developer/CoreSimulator/Caches/*", RuleAction.Cache) { Note = "CoreSimulator's caches of runtimes and dyld" },
        new("file", "~/.android/modem-nv-ram-*", RuleAction.Report) { Note = "written by an Android emulator for its console port; each run on a new port leaves one" }
    ];
}
