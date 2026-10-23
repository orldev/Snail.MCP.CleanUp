using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.CleanUp.Configuration;

/// <summary>The settings this server runs on.</summary>
public sealed class ServerConfig
{
    /// <summary>The section a settings file may nest these under.</summary>
    public const string SectionName = "Snail";

    /// <summary>Where the ledger, the learned rules and the logs of tracked processes live; <c>~/.s3l-mcp-cleanup</c> when not set.</summary>
    /// <remarks>
    /// One directory for every session on the machine, because the point of the ledger is that a session
    /// which died is cleaned up by the next one: a directory per session would die with it. The default is asked for
    /// only when nothing names another, so a ~/.snail-mcp-cleanup of before is moved to the new name by the server
    /// that uses it, never by one told to keep its account elsewhere.
    /// </remarks>
    public string DataDirectory
    {
        get => field ??= ServerFolders.DataDirectory("cleanup");
        set;
    }

    /// <summary>The private directory the hook socket is made in; <c>run</c> inside <see cref="DataDirectory"/> when not set.</summary>
    /// <remarks>
    /// A unix socket's path may be 104 bytes long on macOS at most, so a data directory deep in a temporary folder
    /// needs its socket somewhere shorter.
    /// </remarks>
    public string? HookDirectory { get; set; }

    /// <summary>The socket a hook hands its event to a running server through.</summary>
    public string HookSocket => Path.Combine(HookDirectory ?? Path.Combine(DataDirectory, "run"), "hooks.sock");

    /// <summary>Where what cannot be made again is moved to instead of being erased.</summary>
    public string TrashDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");

    /// <summary>Where Claude Code makes its per-user scratch root, <c>claude-&lt;uid&gt;</c>, with a scratchpad per session inside.</summary>
    public string ScratchRoot { get; set; } = "/private/tmp";

    /// <summary>How large the scratchpad of a running session may grow, in gigabytes, before the inventory and every plan say so; 10 when not set.</summary>
    /// <remarks>A running session's scratchpad is never released, and one grew to 50 GB in a day unseen (2026-10-13).</remarks>
    public double ScratchpadWarningGigabytes { get; set; } = 10;

    /// <summary>How far the free space may fall within a day, in gigabytes, before the inventory says so and names what grew; 20 when not set.</summary>
    /// <remarks>Free space fell by 50 GB in a day without a word on 2026-10-13.</remarks>
    public double DiskFallWarningGigabytes { get; set; } = 20;

    /// <summary>How often, in minutes, the server clears on its own what may go without asking; 10 when not set, 0 never.</summary>
    /// <remarks>
    /// A sweep at a session's end leaves its scratchpad, written moments before, and nothing came back for it: in twelve days
    /// 152 scratchpads of ended sessions, 29 GB of Docker's build cache and 11 001 endpoints of ended processes were left for
    /// somebody to ask about (2026-10-24).
    /// </remarks>
    public double SweepMinutes { get; set; } = 10;

    /// <summary>How large Docker's build cache may grow, in gigabytes, before the server's own sweep prunes it; 10 when not set.</summary>
    public double DockerCacheSweepGigabytes { get; set; } = 10;

    /// <summary>How long, in hours, nothing must have been written in the scratchpad of an ended session before it is erased rather than moved to the Trash; 24 when not set.</summary>
    /// <remarks>The Trash gives back no space: 148 scratchpads swept at their sessions' end waited there for somebody to empty it.</remarks>
    public double ScratchpadEraseHours { get; set; } = 24;

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
