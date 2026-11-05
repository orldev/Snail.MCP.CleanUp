using ModelContextProtocol.Protocol;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Tools.Prompts;

namespace Snail.MCP.CleanUp.Tools;

/// <summary>How long a tool call keeps its caller waiting: a quick tool answers within <see cref="Quick"/>, busy or not.</summary>
/// <remarks>
/// A call that never answers stalls the whole session behind it: cleanup_run kept two sessions waiting 24 and 43 minutes
/// (2026-10-31), then 30 and 18 (2026-11-01), and cleanup_diagnose 17, with nothing in the client's log and no run log
/// written. An answer that says the server is stuck is something the agent can report and work around. The call is not
/// called off: cleanup_run may be a moment from writing its program down, and a census under way is joined by the next
/// call rather than started again. The tools that wait for something by design keep limits of their own — a removal of
/// many things, a release, a simulator or an emulator made and booted. Every call slower than <see cref="Performance.Slow"/> is
/// logged, so that the next hang names its tool and how long it took. The call runs apart from the caller, so that one
/// blocked before its first await is still answered in time.
/// </remarks>
public sealed class Patience(TimeProvider clock, ILogger<Patience> logger)
{
    private static readonly HashSet<string> Waiting = new(["cleanup_apply", "cleanup_release", "cleanup_simulator_acquire", "cleanup_emulator_acquire"], StringComparer.Ordinal);

    /// <summary>How long a quick tool may take before it is answered that the server is busy.</summary>
    public static TimeSpan Quick { get; } = TimeSpan.FromMinutes(2);

    /// <summary>The limit of a tool; null for one that waits for something by design.</summary>
    public static TimeSpan? For(string tool) => Waiting.Contains(tool) ? null : Quick;

    /// <summary>The call's own answer, or, once a quick tool has taken longer than <see cref="Quick"/>, the answer that the server is busy.</summary>
    public async Task<CallToolResult> AnswerAsync(string tool, Func<CancellationToken, Task<CallToolResult>> call, CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        var answering = Task.Run(() => call(cancellationToken), CancellationToken.None);

        try
        {
            return For(tool) is { } limit
                ? await answering.WaitAsync(limit, clock, cancellationToken)
                : await answering;
        }
        catch (TimeoutException)
        {
            Log.CallOverdue(logger, tool, Quick);

            return ToolAnswer.Failed(Messages.Overdue, Messages.OverdueHint, Failures.Overdue, new JsonObject { ["tool"] = tool, ["waitedSeconds"] = (int)Quick.TotalSeconds });
        }
        finally
        {
            var elapsed = clock.GetElapsedTime(started);

            if (elapsed >= Performance.Slow)
                Log.SlowCall(logger, tool, elapsed);
        }
    }
}
