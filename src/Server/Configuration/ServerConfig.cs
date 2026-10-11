namespace Snail.MCP.CleanUp.Configuration;

/// <summary>The settings this server runs on.</summary>
public sealed class ServerConfig
{
    /// <summary>The section a settings file may nest these under.</summary>
    public const string SectionName = "Snail";

    /// <summary>Where the ledger, the learned rules and the logs of tracked processes live.</summary>
    /// <remarks>
    /// One directory for every session on the machine, because the point of the ledger is that a session
    /// which died is cleaned up by the next one: a directory per session would die with it.
    /// </remarks>
    public string DataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".snail-mcp-cleanup");

    /// <summary>Where what cannot be made again is moved to instead of being erased.</summary>
    public string TrashDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");

    /// <summary>Where Claude Code makes its per-user scratch root, <c>claude-&lt;uid&gt;</c>, with a scratchpad per session inside.</summary>
    public string ScratchRoot { get; set; } = "/private/tmp";

    /// <summary>The Android SDK, with <c>emulator/emulator</c>, <c>platform-tools/adb</c> and <c>system-images</c> inside.</summary>
    public string AndroidSdk { get; set; } =
        Environment.GetEnvironmentVariable("ANDROID_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Android", "sdk");

    /// <summary>Where Android virtual devices are defined: a <c>name.ini</c> and a <c>name.avd</c> directory each.</summary>
    public string AvdDirectory { get; set; } =
        Environment.GetEnvironmentVariable("ANDROID_AVD_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "avd");

    /// <summary>VS Code's user directory, with a folder of state per workspace it ever opened in <c>workspaceStorage</c>.</summary>
    public string EditorUserDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Code", "User");

    /// <summary>Claude Code's user settings, where the hooks of this server are installed beside everyone else's.</summary>
    public string ClaudeSettings { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    /// <summary>Where Snail.SleepOf keeps its account of agent sessions, the first place sessions are read from.</summary>
    public string SleepOfDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Snail.SleepOf");
}
