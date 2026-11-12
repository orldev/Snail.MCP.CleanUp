using ModelContextProtocol.Protocol;
using Snail.MCP.CleanUp.Tools.Prompts;
using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.MCP.CleanUp.Tools;

/// <summary>How a tool whose work may outlast a call answers: within the wait its caller allows, with the result or the job in flight.</summary>
/// <remarks>
/// A removal of many things, a release that prunes Docker's build cache, a simulator or an emulator made and booted
/// take minutes, and a client gives up on a call it heard nothing from: Claude Code after 30 minutes of silence, which a
/// prune of 175 GB of build cache reached (2026-10-28). As a job the work goes on whatever the call does, and the
/// answer comes within the wait — the result when the work ended in time, the running job otherwise, as the resource
/// <c>job://cleanup/&lt;kind&gt;/&lt;id&gt;</c> whose text is its report. A wait below zero is refused, never read as none.
/// </remarks>
public static class Waits
{
    /// <summary>The seconds a tool waits for its job when the caller names none.</summary>
    public const int Default = 60;

    /// <summary>The job's answer within <paramref name="wait"/> seconds, or the refusal of a wait below zero.</summary>
    public static async Task<CallToolResult> AnsweredAsync(int wait, Func<Job> started, CancellationToken cancellationToken)
    {
        if (wait < 0)
            return ToolAnswer.Failed($"wait is a number of seconds from 0, not {wait}", ToolDescriptions.Wait, Failures.Arguments);

        var report = await started().ForAsync(TimeSpan.FromSeconds(wait), cancellationToken);

        return JobAnswer.Envelope(report);
    }
}
