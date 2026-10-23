using System.Globalization;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>The sweep log in the data directory: what every sweep did, a hook's at a session's end or the server's own, release by release.</summary>
public sealed class SweepLog(ServerConfig config, TimeProvider clock)
{
    /// <summary>The name of the log in the data directory.</summary>
    public const string FileName = "sweeps.log";

    /// <summary>Writes one entry, stamped with the local time.</summary>
    public async Task WriteAsync(string said, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(config.DataDirectory);

        var entry = $"{clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {said}\n";

        await File.AppendAllTextAsync(Path.Combine(config.DataDirectory, FileName), entry, cancellationToken);
    }

    /// <summary>The entry of a sweep: how each release ended, then — when asked for — what the plan left, and why.</summary>
    public static string Of(string heading, Plan plan, IReadOnlyList<Release> releases, bool withWhatWasLeft)
    {
        var released = releases.Select(release => $"  {release.Outcome.ToString().ToLowerInvariant()} {release.ResourceId}: {release.Detail}");
        var left = withWhatWasLeft
            ? plan.Decisions.Where(decision => decision.Verdict != Verdict.Remove).Select(decision => $"  {decision.Verdict.ToString().ToLowerInvariant()} {decision.Resource.Id}: {decision.Reason}")
            : [];

        return $"{heading}\n{string.Join('\n', released.Concat(left))}";
    }
}
