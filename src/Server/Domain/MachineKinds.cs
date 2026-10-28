namespace Snail.MCP.CleanUp.Domain;

/// <summary>The kinds of resource only a developer's machine has, and the order every kind on it is taken apart in.</summary>
/// <remarks>
/// The general kinds — a process, a container, a file — are <see cref="Kinds"/>; these are the ones Snail.Toolkit.Cleanup
/// leaves to its host. Each value is written into the ledger, into plans and into learned rules, so a value changed here
/// would leave every row written under the old one owned by a kind that no longer exists.
/// </remarks>
public static class MachineKinds
{
    /// <summary>An MCP server an agent runs, or one a session left running when it ended.</summary>
    public const string McpServer = "mcp";

    /// <summary>A rule forwarding a port between this machine and a device.</summary>
    public const string Forward = "forward";

    /// <summary>An iOS simulator device.</summary>
    public const string Simulator = "simulator";

    /// <summary>A running Android emulator.</summary>
    public const string Emulator = "emulator";

    /// <summary>An Android virtual device definition.</summary>
    public const string VirtualDevice = "avd";

    /// <summary>Docker's build cache.</summary>
    public const string BuildCache = "buildcache";

    /// <summary>Docker images no tag points at any more, left by every rebuild of a tag.</summary>
    public const string DanglingImages = "dangling";

    /// <summary>BuildKit's records of finished builds.</summary>
    public const string BuildHistory = "buildhistory";

    /// <summary>What an editor keeps about a folder or a window.</summary>
    public const string Editor = "editor";

    /// <summary>A git worktree.</summary>
    public const string Worktree = "worktree";

    /// <summary>The folder a repository's test runs write their dumps, coverage and logs into.</summary>
    public const string TestResults = "testresults";

    /// <summary>The <c>bin</c> and <c>obj</c> of a repository no build wrote into for a week.</summary>
    public const string ProjectOutputs = "outputs";

    /// <summary>What programs that have ended left for others to reach them, such as the debugger pipes of a .NET process.</summary>
    public const string Remains = "remains";

    /// <summary>A place programs write into outside their own data folders: reported, never removed.</summary>
    public const string Outside = "outside";

    /// <summary>The order every kind on this machine is taken apart in, the one every plan of this server is decided by.</summary>
    /// <remarks>
    /// The programs driving a device go after the processes and before the device, the device before the containers; the
    /// leftovers of builds after the images they come from; what lives in a folder before the files themselves.
    /// </remarks>
    public static KindOrder Order { get; } = KindOrder.General
        .After(Kinds.Process, McpServer, Forward, Emulator, Simulator, VirtualDevice)
        .After(Kinds.Image, DanglingImages, BuildCache, BuildHistory)
        .Before(Kinds.File, Worktree, Editor, TestResults, ProjectOutputs, Remains, Outside);
}
