using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.MCP.CleanUp.Tools.Prompts;

/// <summary>The kinds of this server's own failures: the <c>type</c> of the answer, a word a program branches on.</summary>
/// <remarks>
/// The SDK's words first — <c>arguments</c>, <c>absent</c>, <c>failed</c>, <c>timeout</c> — and a word of the server's own
/// only where none of them says it. Kept apart from <see cref="Messages"/> because the audience differs: a message is read by
/// a model, a type by a program that must not parse the message.
/// </remarks>
public static class Failures
{
    /// <summary>A call this server cannot act on as written: a word no description offers, a value left out that it needs.</summary>
    /// <remarks>The word the SDK's argument check answers with.</remarks>
    public const string Arguments = ToolFailures.Arguments;

    /// <summary>Something named that is not there: a directory to run in, a resource to release, a system image to start.</summary>
    public const string Absent = Reached.Absent;

    /// <summary>A program this server ran that did not do what was asked: simctl failing to list or make a device.</summary>
    public const string Failed = ToolFailures.Failed;

    /// <summary>Work that ran past the time it may take: a simulator not made and booted within ten minutes.</summary>
    public const string Timeout = "timeout";

    /// <summary>The server cannot tell which session it belongs to, so nothing can be made or planned in its name.</summary>
    public const string NoSession = "noSession";

    /// <summary>The sessions of the machine could not be told, so no census was taken and nothing was decided.</summary>
    public const string SessionsUnknown = "sessionsUnknown";

    /// <summary>What a device needs is not installed on this machine: the Android emulator, a system image, an iOS runtime; a person installs it.</summary>
    public const string NotInstalled = "notInstalled";

    /// <summary>What a device needs is in use: the device to clone is running, every emulator console port is taken.</summary>
    public const string Busy = "busy";

    /// <summary>A plan id this server does not hold.</summary>
    public const string NoSuchPlan = "noSuchPlan";

    /// <summary>A plan older than its lifetime, which is never carried out.</summary>
    public const string StalePlan = "stalePlan";

    /// <summary>A resource a plan over everything would not remove, so it is not released.</summary>
    public const string NotReleased = "notReleased";

    /// <summary>A quick tool that took longer than it may: the call may still finish in the background.</summary>
    public const string Overdue = "overdue";
}
