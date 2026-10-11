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

    /// <summary><c>cleanup_inventory</c></summary>
    public const string Inventory =
        """
        Say what is on this machine that agents may have left: what is yours, what sessions that ended left,
        caches, discoveries — new things no known kind covers — and how full the disk is. Changes nothing.
        """;

    /// <summary><c>cleanup_plan</c></summary>
    public const string Plan =
        """
        Decide a cleanup and show it before anything is done: what will be removed and why, what is only
        reported because nothing proves it is an agent's, what is kept. Pass the planId to cleanup_apply
        within 10 minutes; after that the plan is stale.
        """;

    /// <summary>The <c>scope</c> of <c>cleanup_plan</c>.</summary>
    public const string PlanScope =
        "mine (this session's), orphans (left by ended sessions), caches (shared caches that rebuild themselves) or everything; mine by default.";

    /// <summary><c>cleanup_apply</c></summary>
    public const string Apply =
        "Carry out the removals of a plan cleanup_plan showed, in its order. Each thing is checked again first and left alone if it changed.";

    /// <summary>The <c>planId</c> of <c>cleanup_apply</c>.</summary>
    public const string ApplyPlan = "The planId cleanup_plan returned.";

    /// <summary>The <c>only</c> of <c>cleanup_apply</c>.</summary>
    public const string ApplyOnly = "Ids of the plan's removals to carry out; all of them when omitted.";

    /// <summary><c>cleanup_release</c></summary>
    public const string Release =
        "Release one thing now, such as a simulator or an emulator you acquired, if it is proven yours or an ended session's.";

    /// <summary>The <c>id</c> of <c>cleanup_release</c>.</summary>
    public const string ReleaseId = "Its id as cleanup_inventory printed it, such as simulator:<udid>.";

    /// <summary><c>cleanup_keep</c></summary>
    public const string Keep = "Ask for something to be left alone by every cleanup, with the reason, or withdraw that.";

    /// <summary>The <c>id</c> of <c>cleanup_keep</c>.</summary>
    public const string KeepId = "Its id as cleanup_inventory printed it.";

    /// <summary>The <c>reason</c> of <c>cleanup_keep</c>.</summary>
    public const string KeepReason = "Why it stays; required unless withdrawing.";

    /// <summary>The <c>withdraw</c> of <c>cleanup_keep</c>.</summary>
    public const string KeepWithdraw = "True to let cleanups have it again.";

    /// <summary><c>cleanup_learn</c></summary>
    public const string Learn =
        """
        Turn a discovery into a known kind: a rule written to rules.json covering every trace like it from now
        on. ignore says it is harmless, report shows it as a leftover, cache makes it a shared cache.
        """;

    /// <summary>The <c>pattern</c> of <c>cleanup_learn</c>.</summary>
    public const string LearnPattern = "Keys it covers: * is one path segment, ** any number, ~ is home; such as ~/Library/Caches/tool/*.";

    /// <summary>The <c>action</c> of <c>cleanup_learn</c>.</summary>
    public const string LearnAction = "ignore, report or cache.";

    /// <summary>The <c>trail</c> of <c>cleanup_learn</c>.</summary>
    public const string LearnTrail = "The trail of the discovery, the part of its id before the colon; file by default.";

    /// <summary>The <c>note</c> of <c>cleanup_learn</c>.</summary>
    public const string LearnNote = "Why the rule exists, for the person who reads rules.json.";

    /// <summary>The <c>blockedWhileRunning</c> of <c>cleanup_learn</c>.</summary>
    public const string LearnBlocked = "A program that keeps what it covers open, such as Code; nothing covered is removed while it runs.";

    /// <summary><c>find_tool</c></summary>
    public const string FindTool =
        "Find which of this server's tools does a thing, described in your own words, with every argument each one takes.";

    /// <summary>The <c>task</c> of <c>find_tool</c>.</summary>
    public const string FindToolTask = "What you want to do, such as «free the port my dev server held».";

    /// <summary>The <c>limit</c> of <c>find_tool</c>.</summary>
    public const string FindToolLimit = "How many tools to return at most; 5 by default.";
}
