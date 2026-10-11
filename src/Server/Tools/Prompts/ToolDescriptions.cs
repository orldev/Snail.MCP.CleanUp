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

    /// <summary><c>cleanup_run</c></summary>
    public const string Run =
        """
        Start a program that keeps running — a dev server, a watcher, a worker — written down as yours from its
        first second, so that cleaning up after this session ends it for certain and nothing of anyone else's.
        It returns at once with the process id and a log file; read the log for its output. Prefer it to
        starting such a program in the background from a shell.
        """;

    /// <summary>The <c>command</c> of <c>cleanup_run</c>.</summary>
    public const string RunCommand = "The whole command line, run by zsh, such as npm run dev -- --port 3000.";

    /// <summary>The <c>directory</c> of <c>cleanup_run</c>.</summary>
    public const string RunDirectory = "Directory to run it in; the server's own working directory when omitted.";

    /// <summary><c>cleanup_simulator_acquire</c></summary>
    public const string SimulatorAcquire =
        """
        Get an iOS simulator of your own: a clone of a shut-down device, or a new device of a type, named for
        this session so that no other agent uses it and only this session's cleanup removes it. Use it instead
        of booting a simulator somebody may already be using, and drive it by the udid it returns.
        """;

    /// <summary>The <c>from</c> of <c>cleanup_simulator_acquire</c>.</summary>
    public const string SimulatorFrom = "A shut-down device to clone, by name or udid, or a device type such as iPhone 17 Pro; the newest iPhone Pro when omitted.";

    /// <summary>The <c>boot</c> of <c>cleanup_simulator_acquire</c>.</summary>
    public const string SimulatorBoot = "Whether to boot it and wait until it is ready; false by default.";

    /// <summary><c>cleanup_emulator_acquire</c></summary>
    public const string EmulatorAcquire =
        """
        Get an Android emulator of your own: a new virtual device from an installed system image, started on a
        free console port without saving a snapshot, named for this session so that only its cleanup removes
        it. It returns the adb serial at once; the device needs a minute or two to boot.
        """;

    /// <summary>The <c>image</c> of <c>cleanup_emulator_acquire</c>.</summary>
    public const string EmulatorImage = "Part of an installed system image path, such as android-37.0 or google_apis; the newest image without the Play Store when omitted.";

    /// <summary>The <c>window</c> of <c>cleanup_emulator_acquire</c>.</summary>
    public const string EmulatorWindow = "Whether to show the emulator's window; false runs it headless.";
}
